using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Buff;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 내 히어로에게 걸린 버프 아이콘 목록.
	//
	// HeroInfo · BossUI 와 같은 결의 자율 컴포넌트다(MVP 를 쓰지 않는다). MainHud 는 화면(UIScreen)이라
	// Presenter 를 두지만, 이쪽은 HUD 위젯이라 자기 이벤트를 직접 구독한다.
	//
	// **프리팹 루트에 Canvas 가 붙어 있다(리빌드 격리).** 0.1초마다 다시 바인드되고 남은 시간 텍스트가
	// 1초마다 바뀌며 슬롯이 켜졌다 꺼지기까지 해서, HUD 에서 가장 자주 더럽혀지는 영역이다.
	// 같은 캔버스에 두면 그 리빌드가 MainHUD 전체로 번진다 — ResourceBar 와 같은 처리다.
	//
	// 슬롯의 부모는 자기 자신이다 — 루트에 GridLayoutGroup 이 붙어 있어 배치는 그쪽이 맡는다.
	public class BuffInfo : MonoBehaviour
	{
		// 버프는 추가·갱신 이벤트가 없어(BuffContainer 에는 BuffRemoved 뿐) 주기 폴링으로 읽는다.
		// 남은 시간도 매 프레임 줄어드는 값이라 어차피 주기적으로 다시 봐야 한다.
		private const float RefreshInterval = 0.1f;

		[SerializeField] private BuffSlot _slotPrefab;	// UIPrefab_BuffSlot

		private Action<UnitSpawnedEvent> _onUnitSpawned;

		// 추적 중인 히어로. 씬마다 새로 스폰되고 부활 때도 다시 통지되므로 갈아끼운다.
		private UnitBase _hero;

		private IntervalTimer _timer;

		private readonly List<BuffSlotData> _data = new List<BuffSlotData>();

		// 같은 ID 중복 제거용. Independent 정책 버프는 인스턴스마다 목록에 한 번씩 나온다.
		private readonly List<EDT.Buff> _seen = new List<EDT.Buff>();

		// 슬롯은 파괴하지 않고 재사용한다 — 0.1초마다 들어오는 갱신에서 매번 Instantiate 하면 GC 가 튄다.
		private readonly List<BuffSlot> _slots = new List<BuffSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private void Awake()
		{
			_onUnitSpawned = onUnitSpawned;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			_hero = null;
		}

		private void Update()
		{
			if (_timer.Tick(Time.deltaTime, RefreshInterval) <= 0)
			{
				return;
			}

			refresh();
		}

		// ── 갱신 ──────────────────────────────────────────────────────

		// 히어로에게 걸린 버프를 슬롯 데이터로 옮긴다. 디버프는 싣지 않는다.
		private void refresh()
		{
			_data.Clear();

			// 스폰 전이거나 씬 전환으로 파괴됐으면 빈 목록으로 슬롯을 전부 접는다.
			if (_hero == null || _hero.BuffContainer == null)
			{
				render();
				return;
			}

			BuffContainer container = _hero.BuffContainer;

			// GetActive 가 돌려주는 것은 컨테이너 내부의 재사용 버퍼다 — 들고 있지 말고 그 자리에서 옮긴다.
			IReadOnlyList<EDT.Buff> active = container.GetActive();

			_seen.Clear();

			for (int i = 0; i < active.Count; i++)
			{
				EDT.Buff id = active[i];
				if (_seen.Contains(id) == true)
				{
					continue;
				}

				_seen.Add(id);

				// 대표가 아니라 가장 늦게 끝나는 인스턴스를 본다 — Independent 로 여러 개 중첩되면
				// 대표(가장 먼저 걸린 것)의 시간이 먼저 끝나면서 표시가 위로 튄다.
				BuffRuntime runtime = container.GetLongestRuntime(id);
				if (runtime == null || runtime.IsDebuff == true)
				{
					continue;
				}

				Table_Buff.Row row = Table_Buff.Get(id);

				BuffSlotData data;
				data.buff = id;
				data.iconAddress = (row != null) ? row.Icon : string.Empty;

				// 중첩 합산은 컨테이너가 한다 — Independent 는 인스턴스 개수, 나머지는 대표의 Stack.
				data.stack = container.GetStack(id);

				// 남은 시간과 무한 여부는 같은 인스턴스에서 읽어야 짝이 어긋나지 않는다.
				data.remainingSeconds = runtime.RemainingDuration;
				data.isInfinite = runtime.IsInfinite;

				_data.Add(data);
			}

			render();
		}

		// ── 렌더 ──────────────────────────────────────────────────────

		private void render()
		{
			if (_slotPrefab == null)
			{
				return;
			}

			CancellationToken ct = this.GetCancellationTokenOnDestroy();

			_bindTasks.Clear();

			for (int i = 0; i < _data.Count; i++)
			{
				BuffSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(_data[i], ct));
			}

			for (int i = _data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// HUD 는 아이콘 로드를 기다릴 이유가 없다 — 늦게 도착하면 그때 그려진다.
			UniTask.WhenAll(_bindTasks).SuppressCancellationThrow().Forget();
		}

		private BuffSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			// 부모는 자기 자신 — 루트의 GridLayoutGroup 이 칸을 배치한다.
			BuffSlot slot = Instantiate(_slotPrefab, this.transform);
			_slots.Add(slot);

			return slot;
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 부활·씬 전환으로 여러 번 도착한다. 히어로가 바뀌면 다음 주기에 새 컨테이너를 읽는다.
		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero)
			{
				return;
			}

			_hero = e.Unit;
		}
	}
}
