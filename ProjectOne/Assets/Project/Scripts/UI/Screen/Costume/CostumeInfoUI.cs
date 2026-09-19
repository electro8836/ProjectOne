using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.UI
{
	// 코스튬 목록 창의 View(MVP). 장비창의 CostumeButton 이 창 캔버스에 겹쳐 연다.
	//
	// 창 스택에 쌓이는 방식이라 아래의 장비창은 살아 있다 — 이 창을 닫으면 장비창이 그대로 다시 보인다.
	// 표시와 입력 전달만 담당하고, 무엇을 어떤 색·문구로 깔지는 CostumeInfoPresenter 가 정한다.
	public class CostumeInfoUI : UIScreen, IView
	{
		[Header("탭 / 목록")]
		[SerializeField] private TabGroup _tabGroup;					// Bottom/TabMenu_Middle (무기/바디)
		[SerializeField] private RectTransform _gridParent;			// Bottom/ScrollRect/Veiwport/Content/Grid
		[SerializeField] private CostumeSlot _slotPrefab;			// UIPrefab_CostumeSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("캐릭터")]
		[SerializeField] private CharacterPreview _characterPreview;	// Top/Character/UIPrefab_CharacterPreview

		[Header("닫기")]
		[SerializeField] private UIButton _returnButton;	// Return_Button

		public event Action<int> OnTabSelected;
		public event Action<int> OnPreviewClicked;
		public event Action<int> OnEquipClicked;
		public event Action OnReturnClicked;

		private readonly CostumeInfoPresenter _presenter = new CostumeInfoPresenter();

		private readonly List<CostumeSlot> _slots = new List<CostumeSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		// Presenter 가 등급 색을 정해야 해서 열어 둔다 (PetInfoUI 와 같은 자리).
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		// 전체화면으로 목록을 덮으므로 네비게이션 바를 가린다.
		public override bool HidesNavigationBar
		{
			get { return true; }
		}

		private void Awake()
		{
			_tabGroup.OnTabChanged += onTabChanged;
			_returnButton.OnClickEvent += onReturnClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnPreviewClicked -= onSlotPreviewClicked;
				_slots[i].OnEquipClicked -= onSlotEquipClicked;
			}

			_tabGroup.OnTabChanged -= onTabChanged;
			_returnButton.OnClickEvent -= onReturnClicked;

			_presenter.Dispose();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// Select 는 OnTabChanged 를 발행하지 않는다 — 호출자가 이어서 렌더를 지시한다.
		public void SelectTab(int index)
		{
			_tabGroup.Select(index);
		}

		// 입어보기 대상을 프리뷰에 넘긴다. -1 이면 그 부위는 실제 착용으로 되돌아간다.
		public void SetPreviewCostume(int weaponCostumeId, int bodyCostumeId)
		{
			_characterPreview.SetPreviewCostume(weaponCostumeId, bodyCostumeId);
		}

		// 슬롯은 파괴하지 않고 재사용한다 — 탭을 오갈 때마다 Instantiate 하면 GC 가 튄다.
		public async UniTask RenderListAsync(IReadOnlyList<CostumeSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < data.Count; i++)
			{
				CostumeSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private CostumeSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			CostumeSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnPreviewClicked += onSlotPreviewClicked;	// 생성 시 1회만 구독
			slot.OnEquipClicked += onSlotEquipClicked;
			_slots.Add(slot);

			return slot;
		}

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onSlotPreviewClicked(CostumeSlot sender)
		{
			if (OnPreviewClicked != null)
			{
				OnPreviewClicked.Invoke(sender.CostumeId);
			}
		}

		private void onSlotEquipClicked(CostumeSlot sender)
		{
			if (OnEquipClicked != null)
			{
				OnEquipClicked.Invoke(sender.CostumeId);
			}
		}

		private void onReturnClicked()
		{
			if (OnReturnClicked != null)
			{
				OnReturnClicked.Invoke();
			}
		}
	}
}
