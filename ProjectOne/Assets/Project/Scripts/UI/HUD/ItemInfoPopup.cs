using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 View(MVP). UIManager.ShowItemInfoPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 정보/옵션 표시와 입력 전달만 담당하고, 장착/해제 결정은 ItemInfoPresenter 가 한다.
	// (클래스명을 ItemInfoPopup 으로 유지 — 프리펩이 이 스크립트 GUID 를 참조.)
	public class ItemInfoPopup : UIScreen, IView
	{
		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _nameText;	// NameText
		[SerializeField] private TMP_Text _gradeText;	// GradeText
		[SerializeField] private TMP_Text _descText;	// DescText

		[Header("등급 색상 대상")]
		[SerializeField] private Image _topBg;	// 연한 색(bgMask)
		[SerializeField] private Image _deco2;	// 진한 색(gradient)

		[Header("슬롯 / 옵션")]
		[SerializeField] private Transform _itemSlotRoot;	// ItemSlotRoot
		[SerializeField] private Transform _gridParent;		// GridLayout_Equipment

		[Header("버튼")]
		[SerializeField] private UIButton _equipButton;			// EquipButton
		[SerializeField] private TMP_Text _equipButtonLabel;	// EquipButton 라벨
		[SerializeField] private UIButton _exitButton;			// ExitButton

		[Header("프리펩 / 데이터")]
		[SerializeField] private EquipmentSlot _inventorySlotPrefab;		// Prefab_InventorySlot
		[SerializeField] private EquipmentOptionSlot _statSlotPrefab;	// Prefab_EquipmentStatSlot
		[SerializeField] private EquipmentOptionSlot _skillSlotPrefab;	// Prefab_EquipmentSkillSlot
		[SerializeField] private GradeColorTable _gradeColors;

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnEquipToggleClicked;
		public event Action OnExitClicked;

		private readonly ItemInfoPresenter _presenter = new ItemInfoPresenter();
		private UniTaskCompletionSource<bool> _tcs;

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

			_equipButton.OnClickEvent += onEquipClicked;
			_exitButton.OnClickEvent += onExitClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_equipButton.OnClickEvent -= onEquipClicked;
			_exitButton.OnClickEvent -= onExitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(int itemId, CancellationToken ct)
		{
			return _presenter.ShowAsync(itemId, ct);
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

		public void SetInfo(Table_Equipment.Row row)
		{
			applyGradeColor(row.Grade);
			_nameText.text = row.Name;
			_gradeText.text = row.Grade.ToString();
			_descText.text = row.Desc;
		}

		// ItemSlotRoot 에 인벤토리 슬롯을 생성해 등급/아이콘/레벨/개수를 표시하되,
		// 미보유(Unlock)·장착(Focus) 표시는 숨긴다.
		public async UniTask BindItemSlotAsync(Table_Equipment.Row row, bool owned, int count, int level, CancellationToken ct)
		{
			EquipmentSlot slot = Instantiate(_inventorySlotPrefab, _itemSlotRoot);
			await slot.Bind(row, owned, count, level, false, _gradeColors, ct);
			slot.HideStatusObjects();
		}

		// 옵션 슬롯을 GridLayout 에 추가한다 (스탯/스킬 프리펩은 isSkill 로 구분).
		public void BuildOptions(IReadOnlyList<ItemOptionEntry> entries)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				ItemOptionEntry entry = entries[i];
				EquipmentOptionSlot prefab = entry.isSkill ? _skillSlotPrefab : _statSlotPrefab;
				EquipmentOptionSlot slot = Instantiate(prefab, _gridParent);
				slot.Set(entry.title, entry.text);
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

		private void applyGradeColor(ItemGradeTypes grade)
		{
			GradeColorTable.GradeColor gc = _gradeColors.Get(grade);
			_topBg.color = gc.bgMask;	// 연한 색
			_deco2.color = gc.gradient;	// 진한 색
		}

		private void onEquipClicked()
		{
			if (OnEquipToggleClicked != null) { OnEquipToggleClicked.Invoke(); }
		}

		private void onExitClicked()
		{
			if (OnExitClicked != null) { OnExitClicked.Invoke(); }
		}
	}
}
