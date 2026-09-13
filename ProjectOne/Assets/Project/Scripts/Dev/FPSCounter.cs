using Unity.Profiling;
using UnityEngine;

namespace ProjectOne.Dev
{
	// 개발용 프레임 측정 표시 — 빈 GameObject 에 붙여 사용
	//
	// 벽시계 FPS 하나만 보면 안 된다. 에디터에서는 창 재페인트·present 대기가
	// Time.unscaledDeltaTime 에 그대로 섞여 들어와, 게임이 실제로 쓰는 비용과 수 배 차이가 난다.
	// 그래서 두 축을 함께 낸다:
	//   - 벽시계: 평균 / 1% 최악 / 최고 / 최악 (체감 프레임)
	//   - 프로파일러 카운터: 메인·렌더 스레드 CPU 시간 (게임이 쓰는 실제 비용)
	// 벽시계가 나쁜데 스레드 시간이 낮으면 원인은 게임 밖이다(에디터·vSync·present).
	//
	// 자기 비용을 줄이는 것도 정확도의 일부다 — 문자열과 스타일은 갱신 주기마다 한 번만 손대고,
	// IMGUI 는 실제로 그리는 Repaint 이벤트에서만 처리한다.
	public class FPSCounter : MonoBehaviour
	{
		[SerializeField] private int _fontSize = 24;
		[SerializeField] private Color _color = Color.white;

		// 표시 갱신 주기(초). 매 프레임 문자열을 만들면 측정 대상에 자기 비용을 얹는다.
		[SerializeField] private float _refreshInterval = 0.5f;

		// 통계 창 크기(프레임 수). 1% 최악 프레임을 뽑을 표본이다.
		[SerializeField] private int _sampleCapacity = 240;

		// 프레임 시간(ms) 링버퍼와 정렬용 사본
		private float[] _samples;
		private float[] _sorted;
		private int _sampleCount;
		private int _sampleHead;

		private float _elapsed;

		private GUIStyle _style;
		private readonly GUIContent _content = new GUIContent(string.Empty);
		private Rect _rect;
		private bool _styleDirty = true;

#if ENABLE_PROFILER
		// 게임이 쓰는 실제 CPU 시간. 카운터 이름은 런타임에 유효성이 확인된 것만 쓴다.
		private ProfilerRecorder _mainThread;
		private ProfilerRecorder _renderThread;
#endif

		private void Awake()
		{
			int capacity = Mathf.Max(30, _sampleCapacity);
			_samples = new float[capacity];
			_sorted = new float[capacity];
			_rect = new Rect(10f, 10f, 520f, 220f);
		}

		private void OnEnable()
		{
#if ENABLE_PROFILER
			_mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time", 15);
			_renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time", 15);
#endif
		}

		private void OnDisable()
		{
#if ENABLE_PROFILER
			_mainThread.Dispose();
			_renderThread.Dispose();
#endif
		}

		private void Update()
		{
			_samples[_sampleHead] = Time.unscaledDeltaTime * 1000f;
			_sampleHead = (_sampleHead + 1) % _samples.Length;
			if (_sampleCount < _samples.Length)
			{
				_sampleCount++;
			}

			_elapsed += Time.unscaledDeltaTime;
			if (_elapsed < _refreshInterval)
			{
				return;
			}

			_elapsed = 0f;
			refresh();
		}

		private void refresh()
		{
			// 통계 창을 복사해 정렬한다 — 1% 최악 프레임을 뽑으려면 순서가 필요하다.
			// 링버퍼가 덜 찼을 때도 0..(_sampleCount-1) 이 곧 기록된 표본이다.
			float sum = 0f;
			for (int i = 0; i < _sampleCount; i++)
			{
				_sorted[i] = _samples[i];
				sum += _samples[i];
			}

			System.Array.Sort(_sorted, 0, _sampleCount);

			float avgMs = sum / _sampleCount;
			float bestMs = _sorted[0];
			float worstMs = _sorted[_sampleCount - 1];

			// 1% 최악 — 가장 느린 프레임 상위 1% 의 평균(최소 1개). 평균만 보면 숨는 끊김을 드러낸다.
			int lowCount = Mathf.Max(1, _sampleCount / 100);
			float lowSum = 0f;
			for (int i = _sampleCount - lowCount; i < _sampleCount; i++)
			{
				lowSum += _sorted[i];
			}

			float lowMs = lowSum / lowCount;

			string cpuLine = string.Empty;
#if ENABLE_PROFILER
			float mainMs = averageMs(_mainThread);
			float renderMs = averageMs(_renderThread);
			if (mainMs >= 0f)
			{
				cpuLine = string.Format("\nCPU  main {0:0.00} ms", mainMs);
				if (renderMs >= 0f)
				{
					cpuLine += string.Format("   render {0:0.00} ms", renderMs);
				}
			}
#endif

			_content.text = string.Format(
				"FPS  {0:0.0}   avg {1:0.00} ms\n1% low  {2:0.0}   {3:0.00} ms\nbest {4:0.00} / worst {5:0.00} ms{6}",
				toFps(avgMs), avgMs, toFps(lowMs), lowMs, bestMs, worstMs, cpuLine);

			_styleDirty = true;
		}

		private static float toFps(float ms)
		{
			return 1000f / Mathf.Max(ms, 0.0001f);
		}

#if ENABLE_PROFILER
		// 카운터 최근 표본의 평균(ms). 수집된 값이 없으면 -1 을 돌려 표시에서 뺀다.
		private static float averageMs(ProfilerRecorder recorder)
		{
			if (recorder.Valid == false)
			{
				return -1f;
			}

			int count = recorder.Count;
			if (count == 0)
			{
				return -1f;
			}

			double sum = 0.0;
			for (int i = 0; i < count; i++)
			{
				sum += recorder.GetSample(i).Value;
			}

			// 카운터 단위는 나노초다.
			return (float)(sum / count / 1000000.0);
		}
#endif

		private void OnGUI()
		{
			// IMGUI 는 한 프레임에 여러 이벤트로 호출된다 — 실제로 그리는 Repaint 만 처리한다.
			// Event 는 ProjectOne.Event 네임스페이스와 이름이 겹쳐 정규화해야 한다.
			if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint)
			{
				return;
			}

			if (_style == null)
			{
				// GUI.skin 은 OnGUI 안에서만 유효하다.
				_style = new GUIStyle(GUI.skin.label);
				_styleDirty = true;
			}

			if (_styleDirty == true)
			{
				_styleDirty = false;
				_style.fontSize = _fontSize;
				_style.normal.textColor = _color;
			}

			GUI.Label(_rect, _content, _style);
		}
	}
}
