using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 코스튬 목록 창의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/Costume/CostumeScreen.uxml.
	// 장비창의 코스튬 버튼이 창 층에 겹쳐 연다 — 창 스택에 쌓이므로 닫으면 아래의 장비창이 다시 보인다.
	// 표시와 입력 전달만 담당하고, 무엇을 어떤 문구로 깔지는 CostumeInfoPresenter 가 정한다.
	public class CostumeInfoUI : ToolkitScreen, IView
	{
		private const int TAB_COUNT = 2;

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO
		[SerializeField] private CharacterPreview _characterPreview;	// 같은 오브젝트 — 입어보기 결과를 RenderTexture 로 찍는다

		public event Action<int> OnTabSelected;
		public event Action<int> OnPreviewClicked;
		public event Action<int> OnEquipClicked;
		public event Action OnReturnClicked;

		private readonly CostumeInfoPresenter _presenter = new CostumeInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<CostumeCell> _cells = new List<CostumeCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();
		private readonly Button[] _tabs = new Button[TAB_COUNT];

		private VisualElement _character;
		private ScrollView _list;

		// Presenter 가 등급 색을 정해야 해서 열어 둔다.
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		// 전체화면으로 목록을 덮으므로 네비게이션 바를 가린다.
		public override bool HidesNavigationBar
		{
			get { return true; }
		}

		protected override void OnBind(VisualElement root)
		{
			for (int i = 0; i < TAB_COUNT; i++)
			{
				Button tab = root.Q<Button>("tab-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onTabClicked);
				_tabs[i] = tab;
			}

			_character = root.Q("character");
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));
			root.Q<Button>("return-button").clicked += onReturnClicked;

			if (_characterPreview != null)
			{
				_characterPreview.OnTextureReady += onCharacterTextureReady;
			}

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				if (_characterPreview != null)
				{
					_characterPreview.OnTextureReady -= onCharacterTextureReady;
				}

				_presenter.Dispose();
			}

			_icons.ReleaseAll();
			base.OnDestroy();
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

		// OnTabSelected 를 발행하지 않는다 — 호출자가 이어서 렌더를 지시한다.
		public void SelectTab(int index)
		{
			for (int i = 0; i < TAB_COUNT; i++)
			{
				_tabs[i].EnableInClassList("costume-seg__item--on", i == index);
			}
		}

		// 입어보기 대상을 프리뷰에 넘긴다. -1 이면 그 부위는 실제 착용으로 되돌아간다.
		public void SetPreviewCostume(int weaponCostumeId, int bodyCostumeId)
		{
			if (_characterPreview != null)
			{
				_characterPreview.SetPreviewCostume(weaponCostumeId, bodyCostumeId);
			}
		}

		public async UniTask RenderListAsync(IReadOnlyList<CostumeSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				CostumeCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private CostumeCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			CostumeCell cell = new CostumeCell(_list.contentContainer, _icons);
			cell.OnPreviewClicked += onCellPreviewClicked;
			cell.OnEquipClicked += onCellEquipClicked;
			_cells.Add(cell);
			return cell;
		}

		private void onCharacterTextureReady(RenderTexture texture)
		{
			_character.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
		}

		private void onTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectTab(index);
			_list.scrollOffset = Vector2.zero;	// 탭을 바꾸면 목록을 맨 위부터 보인다

			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onCellPreviewClicked(CostumeCell sender)
		{
			if (OnPreviewClicked != null)
			{
				OnPreviewClicked.Invoke(sender.CostumeId);
			}
		}

		private void onCellEquipClicked(CostumeCell sender)
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
