using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 월드 화면 1회분 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct WorldInfoData
	{
		public string actIconAddress;
		public string actName;
		public string locationName;	// 지금 서 있는 곳 — 필드 이름 또는 마을 이름
		public float progress;		// 액트 진행률 0~1
	}

	// 월드 화면의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/World/WorldScreen.uxml.
	// 네비게이션 바의 월드 탭이 UIPrefab_World 를 창 층에 연다.
	// 표시와 입력 전달만 담당하고, 어느 액트를 보여줄지·어떤 팝업을 열지는 WorldPresenter 가 정한다.
	public class WorldUI : ToolkitScreen, IView
	{
		public event Action OnActClicked;
		public event Action OnGoldDungeonClicked;
		public event Action OnRiftDungeonClicked;
		public event Action OnLabyrinthDungeonClicked;
		public event Action OnRuinsDungeonClicked;
		public event Action OnHomeClicked;

		private readonly WorldPresenter _presenter = new WorldPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private VisualElement _actIcon;
		private Label _actName;
		private Label _location;
		private VisualElement _progressFill;
		private Label _progressText;

		protected override void OnBind(VisualElement root)
		{
			_actIcon = root.Q("act-icon");
			_actName = root.Q<Label>("act-name");
			_location = root.Q<Label>("location");
			_progressFill = root.Q("progress-fill");
			_progressText = root.Q<Label>("progress-text");

			VisualElement act = root.Q("act");
			act.RegisterCallback<ClickEvent>(onActClicked);
			act.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			root.Q<Button>("dungeon-gold").clicked += onGoldClicked;
			root.Q<Button>("dungeon-rift").clicked += onRiftClicked;
			root.Q<Button>("dungeon-labyrinth").clicked += onLabyrinthClicked;
			root.Q<Button>("dungeon-ruins").clicked += onRuinsClicked;
			root.Q<Button>("home-button").clicked += onHomeClicked;

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

		public UniTask RenderAsync(WorldInfoData data, CancellationToken ct)
		{
			_actName.text = data.actName;
			_location.text = $"위치 : {data.locationName}";

			_progressFill.style.width = Length.Percent(Mathf.Clamp01(data.progress) * 100f);
			_progressText.text = $"{Mathf.RoundToInt(data.progress * 100f)}%";

			return _icons.SetAsync(_actIcon, data.actIconAddress, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onActClicked(ClickEvent evt)
		{
			if (OnActClicked != null)
			{
				OnActClicked.Invoke();
			}
		}

		private void onGoldClicked()
		{
			if (OnGoldDungeonClicked != null)
			{
				OnGoldDungeonClicked.Invoke();
			}
		}

		private void onRiftClicked()
		{
			if (OnRiftDungeonClicked != null)
			{
				OnRiftDungeonClicked.Invoke();
			}
		}

		private void onLabyrinthClicked()
		{
			if (OnLabyrinthDungeonClicked != null)
			{
				OnLabyrinthDungeonClicked.Invoke();
			}
		}

		private void onRuinsClicked()
		{
			if (OnRuinsDungeonClicked != null)
			{
				OnRuinsDungeonClicked.Invoke();
			}
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null)
			{
				OnHomeClicked.Invoke();
			}
		}
	}
}
