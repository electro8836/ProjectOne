using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Items;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 플레이어 정보 팝업. UIManager.ShowPlayerInfoPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 넘겨받은 PlayerProfile 스냅샷만 그린다 — 내 정보든 남의 정보든 같은 경로다.
	// 그려 놓고 끝이라 Presenter 를 두지 않는다. 장비 칸 배치는 EquipmentUI 의 장착 칸과 같은 방식이다.
	public class PlayerInfoPopup : MonoBehaviour
	{
		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";

		[Header("정보")]
		[SerializeField] private TMP_Text _playerNameText;		// Frame/Top/PlayerNameText
		[SerializeField] private TMP_Text _levelText;			// InfoGroup/HeroInfo/LevelText
		[SerializeField] private TMP_Text _battlePowerText;	// InfoGroup/HeroInfo/BattlePowerText
		[SerializeField] private CharacterPreview _characterPreview;	// InfoGroup/Character

		[Header("장비")]
		[SerializeField] private EquippedSlotView[] _equippedSlots;	// InfoGroup/Group_Slot/Slot_Weapon ~ Slot_Boots
		[SerializeField] private ItemSlot _itemSlotPrefab;				// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;		// 등급 색상 SO

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		[System.Serializable]
		private class EquippedSlotView
		{
			public EquipSlotTypes type;
			public Transform root;	// Slot_XXX 컨테이너
			[System.NonSerialized] public ItemSlot instance;
			[System.NonSerialized] public EquipmentInstance equipment;
		}

		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			for (int i = 0; i < _equippedSlots.Length; i++)
			{
				if (_equippedSlots[i].instance != null)
				{
					_equippedSlots[i].instance.OnClicked -= onItemSlotClicked;
				}
			}
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(PlayerProfile profile, CancellationToken ct)
		{
			_playerNameText.text = profile.playerName;
			// "레벨 12" / "전투력 12,345" — 마스터리 레벨은 표시하지 않는다.
			_levelText.text = "레벨 " + profile.level.ToString();
			_battlePowerText.text = "전투력 " + profile.battlePower.ToString("N0");

			_characterPreview.SetExternalAppearance(profile.weaponCostumeId, profile.bodyCostumeId, profile.equipped[(int)EquipSlotTypes.Weapon]);

			await renderEquippedAsync(profile, ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 빈 칸은 프레임(ItemFrame_Square_02_Empty)만 남긴다. 장비가 있으면 그 위에 슬롯을 덮는다 —
		// UI 의 앞뒤는 형제 순서로 정해지므로 마지막 자식으로 보낸다.
		private async UniTask renderEquippedAsync(PlayerProfile profile, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < _equippedSlots.Length; i++)
			{
				EquippedSlotView slotView = _equippedSlots[i];
				EquipmentInstance equipment = profile.equipped[(int)slotView.type];
				slotView.equipment = equipment;

				if (equipment == null)
				{
					continue;
				}

				slotView.instance = Instantiate(_itemSlotPrefab, slotView.root);
				slotView.instance.StretchToParent();
				slotView.instance.transform.SetAsLastSibling();
				slotView.instance.OnClicked += onItemSlotClicked;

				// 장착 칸이라 장착중 표시는 중복이다 — 끈다.
				_bindTasks.Add(slotView.instance.BindEquipmentAsync(equipment, false, _gradeColors, ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 남의 장비일 수 있어 인벤토리를 보지 않는 읽기 전용 경로로 연다.
		// 이 팝업 위에 뜨고, 이 팝업이 닫히면(파괴 토큰) 함께 닫힌다.
		private void onItemSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			for (int i = 0; i < _equippedSlots.Length; i++)
			{
				if (_equippedSlots[i].instance != sender)
				{
					continue;
				}

				UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, _equippedSlots[i].equipment, this.GetCancellationTokenOnDestroy()).Forget();
				return;
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
