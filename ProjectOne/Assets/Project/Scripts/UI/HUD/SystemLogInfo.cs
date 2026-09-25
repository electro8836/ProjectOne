using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 획득 로그 목록 — 보상이 인벤/지갑에 들어올 때마다 한 줄씩 쌓는다.
	//
	// BuffInfo · MapInfo 와 같은 결의 자율 HUD 위젯이다(MVP 를 쓰지 않는다).
	//   장비   : 획득 <color=등급색>이름</color>(품질)
	//   스택   : 획득 <color=등급색>이름</color> x수량   (1개면 수량 생략)
	//   재화   : 획득 수량 <color=재화색>이름</color>
	//
	// 최대 MaxSlots 개까지 두고, 넘치면 가장 오래된 슬롯을 재사용해 맨 아래(최신)로 옮긴다(큐).
	// 루트 VerticalLayoutGroup 이 ReverseArrangement 라 **첫 번째 형제가 맨 아래**에 놓인다.
	// 각 로그는 표시 후 일정 시간이 지나면 꺼져 대기 목록으로 돌아간다(파괴하지 않고 재사용).
	public class SystemLogInfo : MonoBehaviour
	{
		private const int MaxSlots = 10;

		[SerializeField] private SystemLogSlot _slotPrefab;			// UIPrefab_SystemLogSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		// 표시 중인 슬롯, 오래된 순서. 앞이 가장 먼저 찍힌 로그다.
		private readonly Queue<SystemLogSlot> _slots = new Queue<SystemLogSlot>();

		// 시간이 지나 꺼진 슬롯. 다음 로그에 재사용한다.
		private readonly Stack<SystemLogSlot> _idle = new Stack<SystemLogSlot>();

		private void Awake()
		{
			EventManager.Instance.Subscribe<RewardAcquiredEvent>(onRewardAcquired);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<RewardAcquiredEvent>(onRewardAcquired);
		}

		// 만료 시각은 들어온 순서와 같으므로 맨 앞만 보면 된다.
		private void Update()
		{
			while (_slots.Count > 0 && _slots.Peek().ExpireTime <= Time.time)
			{
				SystemLogSlot slot = _slots.Dequeue();
				slot.gameObject.SetActive(false);
				_idle.Push(slot);
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onRewardAcquired(RewardAcquiredEvent e)
		{
			string text = (e.Type == RewardType.Currency) ? formatCurrency(e) : formatItem(e);
			if (text == null)
			{
				return;
			}

			addLog(text);
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void addLog(string text)
		{
			if (_slotPrefab == null)
			{
				return;
			}

			SystemLogSlot slot;
			if (_slots.Count >= MaxSlots)
			{
				slot = _slots.Dequeue();
			}
			else if (_idle.Count > 0)
			{
				slot = _idle.Pop();
			}
			else
			{
				slot = Instantiate(_slotPrefab, this.transform);
			}

			// 최신 로그를 맨 아래로 — ReverseArrangement 에서는 첫 번째 형제가 맨 아래다.
			slot.gameObject.SetActive(true);
			slot.transform.SetAsFirstSibling();
			slot.SetText(text);
			_slots.Enqueue(slot);
		}

		private string formatCurrency(RewardAcquiredEvent e)
		{
			Table_Currency.Row row = Table_Currency.Get(e.Currency);
			if (row == null)
			{
				Debug.LogError($"[SystemLogInfo] Table_Currency.Get({e.Currency}) == null");
				return null;
			}

			string count = e.Count.ToString("N0");

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
			ItemGradeType grade = (e.IsEquipment == true) ? e.Grade : row.Grade;
			string name = "<color=#" + getGradeColorHex(grade) + ">" + row.Name + "</color>";

			if (e.IsEquipment == true)
			{
				return "획득 " + name + "(" + e.Quality.ToString() + ")";
			}

			if (e.Count > 1)
			{
				return "획득 " + name + " x" + e.Count.ToString("N0");
			}

			return "획득 " + name;
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
