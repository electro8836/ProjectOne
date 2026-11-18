using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Items;
using ProjectOne.Mastery;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 다른 플레이어 정보 팝업(UIPrefab_PlayerInfoPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Ranking/PlayerInfoPopup.uxml.
	// 이름·레벨·마스터리·전투력과 외형(캐릭터 프리뷰), 착용 장비 8칸을 보여 준다.
	// 장비 칸을 누르면 읽기 전용 장비 팝업을 연다.
	public class PlayerInfoPopup : ToolkitScreen
	{
		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";

		// 장착 칸 — EquipSlotTypes 의 Weapon(1) ~ Relic(8). UXML 의 equip-1 ~ equip-8 과 같은 순서다.
		private const int EQUIP_SLOT_FIRST = 1;
		private const int EQUIP_SLOT_LAST = 8;

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO
		[SerializeField] private CharacterPreview _characterPreview;	// 같은 오브젝트 — 그 플레이어 외형을 찍는다

		private sealed class EquipSlot
		{
			public EquipSlotTypes type;
			public ItemCellView cell;
			public EquipmentInstance equipment;
		}

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<EquipSlot> _slots = new List<EquipSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private Label _name;
		private Label _heroLevel;
		private Label _masteryLevel;
		private Label _power;
		private Label _bestPower;
		private VisualElement _character;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_name = root.Q<Label>("name");
			_heroLevel = root.Q<Label>("hero-level");
			_masteryLevel = root.Q<Label>("mastery-level");
			_power = root.Q<Label>("power");
			_bestPower = root.Q<Label>("best-power");
			_character = root.Q("character");

			for (int i = EQUIP_SLOT_FIRST; i <= EQUIP_SLOT_LAST; i++)
			{
				EquipSlot slot = new EquipSlot();
				slot.type = (EquipSlotTypes)i;
				slot.cell = new ItemCellView(root.Q("equip-" + i), _gradeColors, _icons);
				slot.cell.Tag = slot;
				slot.cell.OnClicked += onCellClicked;
				slot.cell.SetShown(false);
				_slots.Add(slot);
			}

			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);

			if (_characterPreview != null)
			{
				_characterPreview.OnTextureReady += onCharacterTextureReady;
			}
		}

		protected override void OnDestroy()
		{
			if (IsBound == true && _characterPreview != null)
			{
				_characterPreview.OnTextureReady -= onCharacterTextureReady;
			}

			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public async UniTask ShowAsync(PlayerProfile profile, CancellationToken ct)
		{
			_name.text = profile.playerName;
			_heroLevel.text = profile.level.ToString() + "/" + MasteryCatalog.MasteryMaxLevel.ToString();
			_masteryLevel.text = profile.masteryLevel.ToString() + "/" + MasteryCatalog.TotalMasteryMaxLevel.ToString();
			_power.text = profile.battlePower.ToString("N0");
			_bestPower.text = profile.bestBattlePower.ToString("N0");

			if (_characterPreview != null)
			{
				_characterPreview.SetExternalAppearance(profile.weaponCostumeId, profile.bodyCostumeId, profile.equipped[(int)EquipSlotTypes.Weapon]);
			}

			await renderEquippedAsync(profile, ct);

			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		private async UniTask renderEquippedAsync(PlayerProfile profile, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < _slots.Count; i++)
			{
				EquipSlot slot = _slots[i];
				slot.equipment = profile.equipped[(int)slot.type];
				if (slot.equipment == null)
				{
					slot.cell.SetShown(false);
					continue;
				}

				slot.cell.SetShown(true);
				_bindTasks.Add(slot.cell.BindEquipmentAsync(slot.equipment, false, ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		private void onCellClicked(ItemCellView sender, long uid, int itemId)
		{
			EquipSlot slot = sender.Tag as EquipSlot;
			if (slot == null || slot.equipment == null)
			{
				return;
			}

			UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, slot.equipment, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void onCharacterTextureReady(RenderTexture texture)
		{
			_character.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
