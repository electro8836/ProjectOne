using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 획득 로그 목록 — 보상이 인벤/지갑에 들어올 때마다 한 줄씩 쌓는다.
	//
	// HUD 위젯(HudBuffList · HudMapInfo)과 같은 결로 자기 이벤트를 직접 구독한다(MVP 를 쓰지 않는다).
	//   장비   : 획득 <color=등급색>이름</color>(품질%)
	//   자동분해 : 자동분해 <color=등급색>이름</color>(품질%)   (환급 재화는 다음 줄에 따로)
	//   스택   : 획득 <color=등급색>이름</color> x수량   (1개면 수량 생략)
	//   재화   : 획득 수량 <color=재화색>이름</color>
	//
	// UI Toolkit 화면이다(레이아웃 UIToolkit/HUD/SystemLog/SystemLogInfo.uxml). UIManager 가 System 층에 붙인다.
	// 최대 MaxSlots 줄까지 두고, 넘치면 가장 오래된 줄을 재사용해 맨 아래(최신)로 옮긴다.
	// 목록이 column-reverse 라 **첫 번째 자식이 맨 아래**에 놓인다.
	// 각 줄은 표시 후 LifeSeconds 가 지나면 빠져 대기 목록으로 돌아간다(파괴하지 않고 재사용).
	// 재화는 종류마다 한 줄로 합산한다 — 그 줄이 떠 있는 동안 같은 재화가 또 들어오면 수량을 더하고
	// 맨 아래로 옮기며 수명을 CurrencyLifeSeconds 로 다시 채운다. 그 시간 안에 안 들어오면 사라진다.
	public class SystemLogInfo : ToolkitScreen
	{
		private const int MaxSlots = 10;

		// 표시 후 사라지기까지의 시간(초)
		private const float LifeSeconds = 5f;

		// 재화 합산 줄의 수명(초) — 같은 재화를 얻을 때마다 다시 채운다
		private const float CurrencyLifeSeconds = 10f;

		// 인벤토리 가득 참 안내를 다시 찍기까지의 시간(초)
		private const float InventoryFullLogCooldown = 3f;

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		// 로그 한 줄 — 만료 시각(Time.time)이 지나면 거둔다.
		private sealed class Line
		{
			public Label label;
			public float expireTime;
			public bool isCurrency;			// 재화 합산 줄이면 true
			public EDT.Currency currency;
			public long currencyCount;		// 지금까지 합산한 수량
		}

		private VisualElement _list;

		// 표시 중인 줄, 오래된 순서(맨 위 → 맨 아래). 재화 줄은 수명이 갱신되므로 만료 순서와 같지 않다.
		private readonly List<Line> _slots = new List<Line>();

		// 시간이 지나 빠진 줄. 다음 로그에 재사용한다.
		private readonly Stack<Line> _idle = new Stack<Line>();

		private float _nextInventoryFullLogTime;

		protected override void OnBind(VisualElement root)
		{
			_list = root.Q("syslog");

			EventManager.Instance.Subscribe<RewardAcquiredEvent>(onRewardAcquired);
			EventManager.Instance.Subscribe<EquipmentAutoDecomposedEvent>(onEquipmentAutoDecomposed);
			EventManager.Instance.Subscribe<HeroPassExpChangedEvent>(onHeroPassExpChanged);
			EventManager.Instance.Subscribe<InventoryFullEvent>(onInventoryFull);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<RewardAcquiredEvent>(onRewardAcquired);
				EventManager.Instance.Unsubscribe<EquipmentAutoDecomposedEvent>(onEquipmentAutoDecomposed);
				EventManager.Instance.Unsubscribe<HeroPassExpChangedEvent>(onHeroPassExpChanged);
				EventManager.Instance.Unsubscribe<InventoryFullEvent>(onInventoryFull);
			}

			base.OnDestroy();
		}

		// 재화 줄은 수명이 갱신되어 만료 순서가 섞인다 — 최대 MaxSlots 줄이라 전부 훑는다.
		private void Update()
		{
			float now = Time.time;
			for (int i = _slots.Count - 1; i >= 0; i--)
			{
				if (_slots[i].expireTime > now)
				{
					continue;
				}

				Line line = _slots[i];
				_slots.RemoveAt(i);
				line.label.RemoveFromHierarchy();
				_idle.Push(line);
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onRewardAcquired(RewardAcquiredEvent e)
		{
			if (e.Type == RewardType.Currency)
			{
				addCurrencyLog(e);
				return;
			}

			string text = formatItem(e);
			if (text == null)
			{
				return;
			}

			addLog(text);
		}

		// 자동 분해된 장비. 환급 재화는 뒤이어 오는 획득 이벤트가 따로 한 줄을 찍는다.
		private void onEquipmentAutoDecomposed(EquipmentAutoDecomposedEvent e)
		{
			Table_Item.Row row = Table_Item.Get(e.ItemId);
			if (row == null)
			{
				Debug.LogError($"[SystemLogInfo] Table_Item.Get({e.ItemId}) == null");
				return;
			}

			addLog("자동분해 " + formatEquipment(row, e.Grade, e.Quality));
		}

		// 한 번에 여러 레벨이 올라도 최종 레벨로 한 줄만 찍는다. 획득 로그와 구분되게 노란색으로 찍는다.
		private void onHeroPassExpChanged(HeroPassExpChangedEvent e)
		{
			if (e.Level <= e.PrevLevel)
			{
				return;
			}

			addLog($"<color=#DFE528>히어로패스 레벨 {e.Level} 달성!</color>");
		}

		// 못 주운 장비 드랍은 범위 안에 있는 동안 계속 획득을 시도한다 — 쿨타임을 두고 한 줄만 찍는다.
		private void onInventoryFull(InventoryFullEvent e)
		{
			if (Time.time < _nextInventoryFullLogTime)
			{
				return;
			}

			_nextInventoryFullLogTime = Time.time + InventoryFullLogCooldown;
			addLog("<color=red>인벤토리가 가득 차서 장비를 획득할 수 없습니다.</color>");
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void addLog(string text)
		{
			addLine(text, LifeSeconds);
		}

		// 같은 재화 줄이 떠 있으면 수량을 더해 맨 아래로 옮기고, 없으면 새 줄을 낸다.
		private void addCurrencyLog(RewardAcquiredEvent e)
		{
			Table_Currency.Row row = Table_Currency.Get(e.Currency);
			if (row == null)
			{
				Debug.LogError($"[SystemLogInfo] Table_Currency.Get({e.Currency}) == null");
				return;
			}

			Line line = findCurrencyLine(e.Currency);
			if (line == null)
			{
				line = addLine(formatCurrency(row, e.Count), CurrencyLifeSeconds);
				if (line == null)
				{
					return;
				}

				line.isCurrency = true;
				line.currency = e.Currency;
				line.currencyCount = e.Count;
				return;
			}

			line.currencyCount += e.Count;
			line.label.text = formatCurrency(row, line.currencyCount);
			line.expireTime = Time.time + CurrencyLifeSeconds;

			// 맨 아래(최신)로 — column-reverse 에서는 첫 번째 자식이 맨 아래다.
			_slots.Remove(line);
			_slots.Add(line);
			_list.Insert(0, line.label);
		}

		private Line findCurrencyLine(EDT.Currency currency)
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i].isCurrency == true && _slots[i].currency == currency)
				{
					return _slots[i];
				}
			}

			return null;
		}

		private Line addLine(string text, float lifeSeconds)
		{
			if (_list == null)
			{
				return null;
			}

			Line line;
			if (_slots.Count >= MaxSlots)
			{
				line = _slots[0];
				_slots.RemoveAt(0);
			}
			else if (_idle.Count > 0)
			{
				line = _idle.Pop();
			}
			else
			{
				line = new Line();
				line.label = new Label();
				line.label.AddToClassList("syslog__line");
				line.label.pickingMode = PickingMode.Ignore;
			}

			// 최신 로그를 맨 아래로 — column-reverse 에서는 첫 번째 자식이 맨 아래다.
			line.label.text = text;
			line.expireTime = Time.time + lifeSeconds;
			line.isCurrency = false;
			_list.Insert(0, line.label);
			_slots.Add(line);
			return line;
		}

		private static string formatCurrency(Table_Currency.Row row, long amount)
		{
			string count = amount.ToString("N0");

			// TextColor 가 비어 있으면 색 태그 없이 기본 색으로 찍는다.
			if (string.IsNullOrEmpty(row.TextColor) == true)
			{
				return "획득 " + count + " " + row.Name;
			}

			return "획득 " + count + " <color=" + row.TextColor + ">" + row.Name + "</color>";
		}

		private string formatItem(RewardAcquiredEvent e)
		{
			Table_Item.Row row = Table_Item.Get(e.ItemId);
			if (row == null)
			{
				Debug.LogError($"[SystemLogInfo] Table_Item.Get({e.ItemId}) == null");
				return null;
			}

			// 장비 등급은 인스턴스가, 스택 아이템 등급은 Item 테이블이 갖는다.
			if (e.IsEquipment == true)
			{
				return "획득 " + formatEquipment(row, e.Grade, e.Quality);
			}

			string name = formatName(row, row.Grade);

			if (e.Count > 1)
			{
				return "획득 " + name + " x" + e.Count.ToString("N0");
			}

			return "획득 " + name;
		}

		// <color=등급색>이름</color>(78.2%)
		private string formatEquipment(Table_Item.Row row, ItemGradeType grade, int quality)
		{
			return formatName(row, grade) + "(" + ProjectOne.Shared.EquipmentQuality.Format(quality) + "%)";
		}

		private string formatName(Table_Item.Row row, ItemGradeType grade)
		{
			return "<color=#" + getGradeColorHex(grade) + ">" + row.Name + "</color>";
		}

		private string getGradeColorHex(ItemGradeType grade)
		{
			if (_gradeColors == null)
			{
				return "FFFFFF";
			}

			return ColorUtility.ToHtmlStringRGB(_gradeColors.Get(grade).bg);
		}
	}
}
