using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Buff;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 메인 HUD 내 히어로 버프 아이콘 목록. 디버프는 싣지 않는다.
	//
	// 버프는 추가·갱신 이벤트가 없어(BuffContainer 에는 BuffRemoved 뿐) 0.1초마다 폴링한다.
	// 칸은 파괴하지 않고 재사용하며, 글자는 값이 바뀔 때만 쓴다 — 가만히 서 있어도 GC 가 돌지 않게.
	public sealed class HudBuffList
	{
		private const float RefreshInterval = 0.1f;

		// 버프 1칸 — 아이콘 + 중첩 수(오른쪽 위) + 남은 시간(아래).
		private sealed class Cell
		{
			public VisualElement root;
			public VisualElement icon;
			public Label stack;
			public Label time;
			public string iconAddress;
			public int lastStack = -1;
			public int lastRemain = -2;	// -1 은 무한 지속(빈칸)
		}

		private readonly VisualElement _list;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ct;

		private readonly List<Cell> _cells = new List<Cell>();
		// 같은 ID 중복 제거용. Independent 정책 버프는 인스턴스마다 목록에 한 번씩 나온다.
		private readonly List<EDT.Buff> _seen = new List<EDT.Buff>();

		private UnitBase _hero;
		private IntervalTimer _timer;

		public HudBuffList(VisualElement root, ToolkitIconLoader icons, CancellationToken ct)
		{
			_list = root.Q("buff-list");
			_icons = icons;
			_ct = ct;

			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			_hero = null;
		}

		public void Tick(float deltaTime)
		{
			if (_timer.Tick(deltaTime, RefreshInterval) <= 0)
			{
				return;
			}

			refresh();
		}

		private void refresh()
		{
			int shown = 0;

			// 스폰 전이거나 씬 전환으로 파괴됐으면 빈 목록으로 칸을 전부 접는다.
			if (_hero != null && _hero.BuffContainer != null)
			{
				BuffContainer container = _hero.BuffContainer;

				// GetActive 는 컨테이너 내부의 재사용 버퍼다 — 들고 있지 말고 그 자리에서 옮긴다.
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

					// 가장 늦게 끝나는 인스턴스를 본다 — 대표의 시간이 먼저 끝나면서 표시가 튀는 것을 막는다.
					BuffRuntime runtime = container.GetLongestRuntime(id);
					if (runtime == null || runtime.IsDebuff == true)
					{
						continue;
					}

					Table_Buff.Row row = Table_Buff.Get(id);
					Cell cell = getOrCreateCell(shown);
					bind(cell, (row != null) ? row.Icon : string.Empty, container.GetStack(id), runtime.RemainingDuration, runtime.IsInfinite);
					shown++;
				}
			}

			for (int i = 0; i < _cells.Count; i++)
			{
				DisplayStyle display = (i < shown) ? DisplayStyle.Flex : DisplayStyle.None;
				if (_cells[i].root.style.display != display)
				{
					_cells[i].root.style.display = display;
				}
			}
		}

		private void bind(Cell cell, string iconAddress, int stack, float remainingSeconds, bool isInfinite)
		{
			if (cell.iconAddress != iconAddress)
			{
				cell.iconAddress = iconAddress;
				_icons.SetAsync(cell.icon, iconAddress, _ct).Forget();
			}

			if (cell.lastStack != stack)
			{
				cell.lastStack = stack;
				cell.stack.text = (stack > 1) ? stack.ToString() : string.Empty;
			}

			// 무한 지속 버프는 남은 시간이 없다 — 칸을 비운다.
			int remain = -1;
			if (isInfinite == false)
			{
				remain = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
			}

			if (cell.lastRemain != remain)
			{
				cell.lastRemain = remain;
				cell.time.text = (remain < 0) ? string.Empty : formatRemain(remain);
			}
		}

		// 가장 큰 단위 하나만 — 칸 아래 한 줄에 들어가야 한다.
		private static string formatRemain(int totalSeconds)
		{
			if (totalSeconds >= 86400)
			{
				return (totalSeconds / 86400) + "일";
			}

			if (totalSeconds >= 3600)
			{
				return (totalSeconds / 3600) + "시간";
			}

			if (totalSeconds >= 60)
			{
				return (totalSeconds / 60) + "분";
			}

			return totalSeconds + "초";
		}

		private Cell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			Cell cell = new Cell();
			cell.root = new VisualElement();
			cell.root.AddToClassList("buff");
			cell.root.pickingMode = PickingMode.Ignore;

			VisualElement frame = new VisualElement();
			frame.AddToClassList("buff__frame");
			frame.pickingMode = PickingMode.Ignore;
			cell.root.Add(frame);

			cell.icon = new VisualElement();
			cell.icon.AddToClassList("buff__icon");
			cell.icon.pickingMode = PickingMode.Ignore;
			frame.Add(cell.icon);

			cell.stack = createLabel("buff__stack");
			frame.Add(cell.stack);

			cell.time = createLabel("buff__time");
			cell.root.Add(cell.time);

			_list.Add(cell.root);
			_cells.Add(cell);
			return cell;
		}

		private static Label createLabel(string className)
		{
			Label label = new Label();
			label.AddToClassList("hud-text");
			label.AddToClassList(className);
			label.pickingMode = PickingMode.Ignore;
			return label;
		}

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
