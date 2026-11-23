using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 퀘스트 목록 팝업(UIPrefab_QuestListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Quest/QuestListPopup.uxml.
	// Presenter 가 정한 줄을 그리고, 진행 중인 줄로 스크롤을 옮긴 뒤 Reveal 로 보인다.
	public class QuestListPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private readonly QuestListPopupPresenter _presenter = new QuestListPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<QuestCell> _cells = new List<QuestCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private ScrollView _list;
		private UniTaskCompletionSource _tcs;
		private int _focusIndex = -1;

		protected override void OnBind(VisualElement root)
		{
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));

			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
			}

			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public async UniTask RenderAsync(IReadOnlyList<QuestSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				QuestCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindAsync(data[i], i == data.Count - 1, ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 진행 중 줄의 진행도. 범위 밖이면 무시한다.
		public void SetProgress(int index, string label, int current, int required)
		{
			if (index < 0 || index >= _cells.Count)
			{
				return;
			}

			_cells[index].SetProgress(label, current, required);
		}

		// 지정한 줄이 보이도록 스크롤한다. 범위 밖이면 맨 위. 배치가 끝난 다음 프레임에 옮긴다.
		public void FocusSlot(int index)
		{
			_focusIndex = index;
			_list.schedule.Execute(applyFocus);
		}

		public void Reveal()
		{
			PlayPopupOpen();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		private void applyFocus()
		{
			if (_focusIndex < 0 || _focusIndex >= _cells.Count)
			{
				_list.scrollOffset = Vector2.zero;
				return;
			}

			_list.ScrollTo(_cells[_focusIndex].Root);
		}

		private QuestCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			QuestCell cell = new QuestCell(_list.contentContainer, _gradeColors, _icons, GetDestroyToken());
			_cells.Add(cell);
			return cell;
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
