using System;
using UnityEngine;
using TMPro;
using ProjectOne.Event;
using ProjectOne.Dungeon;

namespace ProjectOne.UI
{
	// 던전 임시재화(MagicEssence) 수량 표시. DungeonRunState 의 변경 이벤트를 구독해 갱신한다.
	// (변경 전용 이벤트가 있어 폴링하지 않는다 — MonsterCount 위젯과 동일한 자체 구독 패턴)
	public class EssenceCount : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private TMP_Text _countText;

		private Action<DungeonEssenceChangedEvent> _onChanged;

		private void Awake()
		{
			_onChanged = onEssenceChanged;
			EventManager.Instance.Subscribe<DungeonEssenceChangedEvent>(_onChanged);

			if (_countText != null)
			{
				_countText.text = DungeonRunState.Instance.EssenceAmount.ToString();
			}
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<DungeonEssenceChangedEvent>(_onChanged);
		}

		private void onEssenceChanged(DungeonEssenceChangedEvent evt)
		{
			if (_countText != null)
			{
				_countText.text = evt.Amount.ToString();
			}
		}
	}
}
