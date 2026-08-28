using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Items;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 View(MVP). UIManager.ShowItemInfoPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 정보/옵션 표시와 입력 전달만 담당하고, 장착/해제 결정은 ItemInfoPresenter 가 한다.
	// (클래스명을 ItemInfoPopup 으로 유지 — 프리펩이 이 스크립트 GUID 를 참조.)
	//
	// 옵션 줄은 프리펩에 미리 배치되어 있다(BasicOptionInfo_1~4 / GradeOptionInfo_1~6).
	// 그래서 슬롯을 생성하지 않고 고정 칸을 켜고 끄며 채운다.
	public class ItemInfoPopup : UIScreen, IView
	{
		// 미해금 등급 줄의 색 — 수치만이 아니라 문구·범위를 포함한 줄 전체가 이 색이 된다.
		private static readonly Color LockedColor = new Color(0.42745098f, 0.42745098f, 0.42745098f, 1f);	// #6D6D6D

		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _nameText;		// NameText
		[SerializeField] private TMP_Text _gradeText;		// GradeText
		[SerializeField] private TMP_Text _levelText;		// LevelText
		[SerializeField] private TMP_Text _qualityText;	// QualityText
		[SerializeField] private TMP_Text _descText;		// DescText

		[Header("등급 색상 대상")]
		[SerializeField] private Image _topBg;	// TopBg — 등급 bg
		[SerializeField] private Image _deco2;	// Deco2 — 등급 border

		[Header("슬롯 / 옵션")]
		[SerializeField] private Transform _itemSlotRoot;			// ItemSlotRoot
		[SerializeField] private BasicOptionView[] _basicOptions;	// BasicOptionInfo_1~4
		[SerializeField] private GradeOptionView[] _gradeOptions;	// GradeOptionInfo_1~6

		[Header("버튼")]
		[SerializeField] private UIButton _equipButton;			// EquipButton
		[SerializeField] private TMP_Text _equipButtonLabel;	// EquipButton 라벨
		[SerializeField] private UIButton _enchantButton;		// EnchantButton

		// Dimmed — **이 팝업의 유일한 닫기 수단이다**(전용 닫기 버튼은 없앴다).
		// 본문 영역은 ItemInfo/Bg 가 레이캐스트를 흡수하므로, 여기까지 내려오는 클릭은
		// 곧 "팝업 밖을 눌렀다"는 뜻이다.
		[SerializeField] private UIButton _dimmedButton;

		[Header("프리펩 / 데이터")]
		[SerializeField] private ItemSlot _slotPrefab;				// Prefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;

		// 기본 옵션 1칸 — 아이콘 + 문구.
		[System.Serializable]
		private class BasicOptionView
		{
			public GameObject root;
			public Image icon;
			public TMP_Text optionText;
		}

		// 등급 옵션 1칸 — 등급 마크(테두리 포함) + 문구 + 범위.
		[System.Serializable]
		private class GradeOptionView
		{
			public GameObject root;
			public Image gradeMark;
			public Image gradeMarkBorder;
			public TMP_Text optionText;
			public TMP_Text rangeText;

			// 해금 줄로 되돌릴 때 쓸 프리펩 기본색. 회색을 덮어쓰기 전에 Awake 에서 받아 둔다.
			[System.NonSerialized] public Color defaultTextColor;
			[System.NonSerialized] public Color defaultRangeColor;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnEquipToggleClicked;
		public event Action OnEnchantClicked;
		public event Action OnExitClicked;

		private readonly ItemInfoPresenter _presenter = new ItemInfoPresenter();
		private UniTaskCompletionSource<bool> _tcs;

		// ItemSlotRoot 에 만든 슬롯 — 팝업 1회 표시에 하나만 쓴다.
		private ItemSlot _itemSlot;

		// 첫 렌더(아이콘 로드)가 끝날 때까지 숨겼다가 한 번에 보여주기 위한 그룹
		private CanvasGroup _canvasGroup;

		private void Awake()
		{
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			// 로드 완료 전까지 숨김 — Reveal 에서 보여준다.
			setVisible(false);

			cacheGradeTextColors();

			_equipButton.OnClickEvent += onEquipClicked;
			_enchantButton.OnClickEvent += onEnchantClicked;
			_dimmedButton.OnClickEvent += onExitClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_equipButton.OnClickEvent -= onEquipClicked;
			_enchantButton.OnClickEvent -= onEnchantClicked;
			_dimmedButton.OnClickEvent -= onExitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(long uid, CancellationToken ct)
		{
			return _presenter.ShowAsync(uid, ct);
		}

		// Presenter 가 첫 렌더(아이콘 로드)를 끝낸 뒤 호출 — 채워진 상태로 한 번에 표시.
		public void Reveal()
		{
			setVisible(true);
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// 등급·레벨·품질은 Item 테이블이 아니라 장비 인스턴스가 소유하므로 따로 받는다.
		public void SetInfo(Table_Item.Row row, ItemGradeType grade, int level, int maxLevel, int quality)
		{
			applyGradeColor(grade);
			_nameText.text = row.Name;
			_gradeText.text = ItemGradeNames.Get(grade);
			_levelText.text = "레벨: " + level + "/" + maxLevel;
			_qualityText.text = "품질 : " + quality + "%";
			_descText.text = row.Desc;
		}

		// 기본 옵션 — 데이터 개수만큼 칸을 켜고 나머지는 끈다.
		public void RenderBasicOptions(IReadOnlyList<OptionLine> lines)
		{
			if (_basicOptions == null)
			{
				return;
			}

			for (int i = 0; i < _basicOptions.Length; i++)
			{
				BasicOptionView slot = _basicOptions[i];
				if (i >= lines.Count)
				{
					setActive(slot.root, false);
					continue;
				}

				setActive(slot.root, true);
				slot.optionText.text = lines[i].text;
				setIcon(slot.icon, lines[i].iconAddress);
			}
		}

		// 등급 옵션 — 칸이 등급에 1:1 대응하므로 개수만큼 그대로 채운다.
		// 해금 옵션이 없는 등급(Normal 등)은 칸을 켠 채 문구만 비운다.
		public void RenderGradeOptions(IReadOnlyList<GradeOptionLine> lines)
		{
			if (_gradeOptions == null)
			{
				return;
			}

			for (int i = 0; i < _gradeOptions.Length; i++)
			{
				GradeOptionView slot = _gradeOptions[i];

				// 그 등급에 해금 옵션이 아예 없으면(Normal 등) 칸을 통째로 감춘다.
				if (i >= lines.Count || lines[i].hasOption == false)
				{
					setActive(slot.root, false);
					continue;
				}

				GradeOptionLine line = lines[i];
				setActive(slot.root, true);

				// 마크는 그 칸이 대표하는 등급의 색을 쓴다(해금 여부와 무관 — 어느 등급 칸인지 알려주는 표시다).
				if (_gradeColors != null)
				{
					ItemGradeColorTable.GradeColor gc = _gradeColors.Get(line.grade);
					if (slot.gradeMark != null) { slot.gradeMark.color = gc.bg; }
					if (slot.gradeMarkBorder != null) { slot.gradeMarkBorder.color = gc.border; }
				}

				// 미해금은 수치뿐 아니라 스탯 이름까지 줄 전체가 회색이다.
				// Presenter 가 미해금 문구에는 색 태그를 넣지 않으므로 여기서 색이 그대로 먹는다.
				slot.optionText.text = line.text;
				slot.optionText.color = line.unlocked ? slot.defaultTextColor : LockedColor;

				if (slot.rangeText != null)
				{
					slot.rangeText.gameObject.SetActive(line.hasRange);
					slot.rangeText.text = line.rangeText;
					slot.rangeText.color = line.unlocked ? slot.defaultRangeColor : LockedColor;
				}
			}
		}

		// ItemSlotRoot 에 아이템 슬롯을 만들어 등급/아이콘/품질을 표시한다.
		// 레벨은 팝업 상단(LevelText)이 따로 적으므로 슬롯에서는 끈다.
		public async UniTask BindItemSlotAsync(EquipmentInstance instance, bool equipped, CancellationToken ct)
		{
			if (_itemSlot == null)
			{
				_itemSlot = Instantiate(_slotPrefab, _itemSlotRoot);
			}

			await _itemSlot.BindEquipmentAsync(instance, equipped, _gradeColors, ct);
			_itemSlot.HideLevel();
		}

		// 장착/해제 토글 직후 슬롯 표시만 뒤집는다 — 재바인딩(아이콘 재로드)을 피한다.
		public void SetSlotEquipped(bool equipped)
		{
			if (_itemSlot != null)
			{
				_itemSlot.SetEquipped(equipped);
			}
		}

		public void SetEquipInteractable(bool interactable)
		{
			_equipButton.interactable = interactable;
		}

		public void SetEquipLabel(string label)
		{
			_equipButtonLabel.text = label;
		}

		// 닫힘 대기 — Presenter 의 ShowAsync 가 마지막에 await 한다.
		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 입력(Exit)으로 닫힘을 확정한다.
		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 회색으로 덮어쓰기 전에 프리펩이 지정한 색을 받아 둔다 — 해금 줄을 되돌릴 기준이다.
		private void cacheGradeTextColors()
		{
			if (_gradeOptions == null)
			{
				return;
			}

			for (int i = 0; i < _gradeOptions.Length; i++)
			{
				GradeOptionView slot = _gradeOptions[i];
				if (slot == null)
				{
					continue;
				}

				if (slot.optionText != null)
				{
					slot.defaultTextColor = slot.optionText.color;
				}

				if (slot.rangeText != null)
				{
					slot.defaultRangeColor = slot.rangeText.color;
				}
			}
		}

		private void applyGradeColor(ItemGradeType grade)
		{
			if (_gradeColors == null)
			{
				return;
			}

			ItemGradeColorTable.GradeColor gc = _gradeColors.Get(grade);
			_topBg.color = gc.bg;
			_deco2.color = gc.border;
			_gradeText.color = gc.text;
		}

		// 스탯 아이콘은 전부 아틀라스에 있으므로 동기 조회로 끝난다(없으면 아이콘만 감춘다).
		private static void setIcon(Image target, string address)
		{
			if (target == null)
			{
				return;
			}

			Sprite sprite = string.IsNullOrEmpty(address) ? null : ProjectOne.Resources.AtlasManager.Instance.Get(address);
			target.sprite = sprite;
			target.enabled = sprite != null;
		}

		private static void setActive(GameObject go, bool active)
		{
			if (go != null)
			{
				go.SetActive(active);
			}
		}

		private void onEquipClicked()
		{
			if (OnEquipToggleClicked != null) { OnEquipToggleClicked.Invoke(); }
		}

		private void onEnchantClicked()
		{
			if (OnEnchantClicked != null) { OnEnchantClicked.Invoke(); }
		}

		private void onExitClicked()
		{
			if (OnExitClicked != null) { OnExitClicked.Invoke(); }
		}
	}
}
