using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 내 캐릭터 능력치 팝업(UIPrefab_StatPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Stat/StatPopup.uxml.
	// UIManager.ShowStatPopupAsync 가 열고 닫힘을 기다린다. 값 계산은 StatPopupPresenter 가 하고,
	// 아이콘까지 준비되면 Reveal 로 한 번에 보인다(빈 줄이 깜빡이지 않게).
	public class StatPopup : ToolkitScreen, IView
	{
		// 이름 글자 줄임 기준(px) — 칸 안 글자 자리(약 340px)에 한 줄로 들어가게. 폭은 Pretendard 기준 추정
		// (한글 1칸 ≈ 글자 크기 0.85배, 영문·숫자 0.55배, 공백 0.3배 — ItemInfoPopup 제목과 같은 방식).
		private const float NAME_WIDTH_LIMIT = 330f;
		private const float NAME_SIZE_LARGE = 26f;
		private const float NAME_SIZE_MEDIUM = 22f;

		// 능력치 칸 하나 — 아이콘 + 위 이름 · 아래 값.
		private sealed class Row
		{
			public VisualElement root;
			public VisualElement icon;
			public Label name;
			public Label value;
		}

		private readonly StatPopupPresenter _presenter = new StatPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<Row> _offensiveRows = new List<Row>();
		private readonly List<Row> _defensiveRows = new List<Row>();
		private readonly List<Row> _utilityRows = new List<Row>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private VisualElement _offensive;
		private VisualElement _defensive;
		private VisualElement _utility;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_offensive = root.Q("offensive");
			_defensive = root.Q("defensive");
			_utility = root.Q("utility");

			ScrollView scroll = root.Q<ScrollView>("scroll");
			scroll.AddManipulator(new ScrollDragManipulator(scroll));

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

		public async UniTask RenderAsync(IReadOnlyList<StatSlotData> offensive, IReadOnlyList<StatSlotData> defensive,
			IReadOnlyList<StatSlotData> utility, CancellationToken ct)
		{
			_bindTasks.Clear();
			renderGroup(_offensiveRows, _offensive, offensive, ct);
			renderGroup(_defensiveRows, _defensive, defensive, ct);
			renderGroup(_utilityRows, _utility, utility, ct);
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 첫 렌더가 끝나면 보인다.
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

		private void renderGroup(List<Row> rows, VisualElement parent, IReadOnlyList<StatSlotData> data, CancellationToken ct)
		{
			// 2열 — 짝수 번째가 왼쪽 칸, 마지막 줄 칸은 아래 선을 뺀다
			int lastRowStart = ((data.Count - 1) / 2) * 2;
			for (int i = 0; i < data.Count; i++)
			{
				Row row = getOrCreateRow(rows, parent, i);
				row.root.style.display = DisplayStyle.Flex;
				row.root.EnableInClassList("stat-row--left", i % 2 == 0);
				row.root.EnableInClassList("stat-row--last", i >= lastRowStart);
				row.root.EnableInClassList("stat-row--zero", data[i].isZero);
				row.name.text = data[i].name;
				row.value.text = data[i].value;

				float width = estimateWidth(data[i].name);
				row.name.EnableInClassList("stat-row__name--medium", width * NAME_SIZE_LARGE > NAME_WIDTH_LIMIT && width * NAME_SIZE_MEDIUM <= NAME_WIDTH_LIMIT);
				row.name.EnableInClassList("stat-row__name--small", width * NAME_SIZE_MEDIUM > NAME_WIDTH_LIMIT);

				_bindTasks.Add(_icons.SetAsync(row.icon, data[i].iconAddress, ct));
			}

			for (int i = data.Count; i < rows.Count; i++)
			{
				rows[i].root.style.display = DisplayStyle.None;
			}
		}

		private static Row getOrCreateRow(List<Row> rows, VisualElement parent, int index)
		{
			if (index < rows.Count)
			{
				return rows[index];
			}

			Row row = new Row();
			row.root = new VisualElement();
			row.root.AddToClassList("stat-row");
			row.icon = new VisualElement();
			row.icon.AddToClassList("stat-row__icon");
			VisualElement texts = new VisualElement();
			texts.AddToClassList("stat-row__texts");
			row.name = new Label();
			row.name.AddToClassList("stat-row__name");
			row.value = new Label();
			row.value.AddToClassList("stat-row__value");
			texts.Add(row.name);
			texts.Add(row.value);
			row.root.Add(row.icon);
			row.root.Add(texts);
			parent.Add(row.root);

			rows.Add(row);
			return row;
		}

		// 글자 폭(글자 크기 배수) 추정 — 한글 0.85, 영문·숫자·기호 0.55, 공백 0.3.
		private static float estimateWidth(string text)
		{
			float width = 0f;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c == ' ')
				{
					width += 0.3f;
				}
				else if (c < 128)
				{
					width += 0.55f;
				}
				else
				{
					width += 0.85f;
				}
			}

			return width;
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
