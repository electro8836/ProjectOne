using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Resources;
using ProjectOne.UI;
using ProjectOne.Utils;

namespace ProjectOne.Loading
{
	// 로딩 화면의 단일 소유자(중앙집중 로더).
	// - 흐름별 진행 구간 테이블 + 단계 라벨을 한곳에 보유(매직넘버/텍스트 일원화)
	// - 뷰(LoadingUI) 인스턴스화·표시·해제 생명주기 소유
	// - 분산된 State 는 Show / SetPhaseProgress(단계, 로컬비율) / Hide 만 호출
	public class LoadingManager : MonoSingleton<LoadingManager>
	{
		// 한 단계가 전체 진행률(0~1)에서 차지하는 구간 + 라벨.
		// 같은 단계라도 흐름에 따라 구간/라벨이 다르므로 흐름별로 보유한다.
		private readonly struct PhaseRange
		{
			public readonly LoadingPhase phase;
			public readonly float start;
			public readonly float end;
			public readonly string label;

			public PhaseRange(LoadingPhase phase, float start, float end, string label)
			{
				this.phase = phase;
				this.start = start;
				this.end = end;
				this.label = label;
			}
		}

		// ── 흐름별 단계 구성 (진행률/텍스트 단일 기준) ──────────────────
		private static readonly PhaseRange[] ToLobbyPhases =
		{
			new PhaseRange(LoadingPhase.Patch,      0.00f, 0.15f, "Downloading resources..."),
			new PhaseRange(LoadingPhase.ServerData, 0.15f, 0.70f, "Loading user data..."),
			new PhaseRange(LoadingPhase.SceneLoad,  0.70f, 0.90f, "Loading lobby..."),
			new PhaseRange(LoadingPhase.SceneReady, 0.90f, 1.00f, "Preparing..."),
		};

		private static readonly PhaseRange[] ToBattlePhases =
		{
			new PhaseRange(LoadingPhase.SceneLoad,  0.00f, 0.70f, "Loading battle map..."),
			new PhaseRange(LoadingPhase.SceneReady, 0.70f, 1.00f, "Preparing battle..."),
		};

		private static readonly PhaseRange[] ReturnToLobbyPhases =
		{
			new PhaseRange(LoadingPhase.SceneLoad,  0.00f, 0.70f, "Returning to lobby..."),
			new PhaseRange(LoadingPhase.SceneReady, 0.70f, 1.00f, "Preparing..."),
		};

		[SerializeField] private string _loadingAddress = "LoadingUI";

		private LoadingUI _view;
		private LoadingFlow _activeFlow;
		private bool _isShowing;

		public bool IsShowing => _isShowing;

		// 로딩 화면 표시 — 뷰를 띄우고(페이드인) 활성 흐름을 설정한다.
		public async UniTask ShowAsync(LoadingFlow flow, CancellationToken ct)
		{
			if (_isShowing == true)
			{
				return;
			}

			_activeFlow = flow;
			_isShowing = true;

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(_loadingAddress, ct);
			if (prefab == null)
			{
				_isShowing = false;
				return;
			}

			GameObject go = Instantiate(prefab, transform);
			_view = go.GetComponent<LoadingUI>();
			if (_view == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(_loadingAddress);
				_isShowing = false;
				return;
			}

			// 활성 흐름 첫 단계 라벨로 초기 문구 설정
			PhaseRange[] phases = phasesOf(_activeFlow);
			if (phases.Length > 0)
			{
				_view.SetPhase(phases[0].label);
			}

			await _view.OnOpenAsync(ct);
		}

		// 단계 진행률 설정 — 단계의 로컬 비율(0~1)을 활성 흐름 구간으로 매핑해 뷰에 반영한다.
		// 표시 중이 아니면 무해하게 무시한다(Show 없이 진입한 경로 대비).
		public void SetPhaseProgress(LoadingPhase phase, float localRatio01)
		{
			if (_isShowing == false || _view == null)
			{
				return;
			}

			PhaseRange[] phases = phasesOf(_activeFlow);
			for (int i = 0; i < phases.Length; i++)
			{
				if (phases[i].phase != phase)
				{
					continue;
				}

				float ratio = Mathf.Clamp01(localRatio01);
				float abs = phases[i].start + ratio * (phases[i].end - phases[i].start);
				_view.SetProgress(abs);
				_view.SetPhase(phases[i].label);
				return;
			}
		}

		// 로딩 화면 해제 — 100% 스냅 후 페이드아웃하고 뷰를 해제한다.
		public async UniTask HideAsync()
		{
			if (_isShowing == false)
			{
				return;
			}

			_isShowing = false;

			if (_view == null)
			{
				return;
			}

			LoadingUI view = _view;
			_view = null;
			await view.OnCloseAsync();
			Destroy(view.gameObject);
			ResourceManager.Instance.Release(_loadingAddress);
		}

		private static PhaseRange[] phasesOf(LoadingFlow flow)
		{
			switch (flow)
			{
				case LoadingFlow.ToBattle:
					return ToBattlePhases;
				case LoadingFlow.ReturnToLobby:
					return ReturnToLobbyPhases;
				default:
					return ToLobbyPhases;
			}
		}
	}
}
