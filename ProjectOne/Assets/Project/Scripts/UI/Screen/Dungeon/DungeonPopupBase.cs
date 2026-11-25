using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 던전 입장 팝업 4종(골드·미궁·유적·균열)의 공용 View 틀(UI Toolkit).
	// 이름·썸네일·초기화 남은 시간·입장 버튼·닫기와 "닫힐 때까지 기다리기"를 맡는다.
	// 어느 Presenter 를 붙일지는 하위 클래스가 정한다 — Presenter 가 View 타입을 제네릭으로 물고 있어서다.
	public abstract class DungeonPopupBase : ToolkitScreen, IView
	{
		public event Action OnEnterClicked;

		// 1초마다 발행. 시간을 어떻게 해석해 무엇을 그릴지는 Presenter 가 정한다.
		public event Action OnSecondTick;

		protected readonly ToolkitIconLoader icons = new ToolkitIconLoader();

		private Label _name;
		private VisualElement _thumbnail;
		private Label _refreshTime;
		private Button _enter;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_name = root.Q<Label>("name");
			_thumbnail = root.Q("thumbnail");
			_refreshTime = root.Q<Label>("refresh-time");
			_enter = root.Q<Button>("enter");

			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
			root.Q<Button>("close").clicked += Close;
			_enter.clicked += onEnterClicked;

			BindContent(root);
			InitializePresenter();
		}

		protected override void OnDestroy()
		{
			icons.ReleaseAll();
			if (IsBound == true)
			{
				DisposePresenter();
			}

			base.OnDestroy();
		}

		// 남은 시간을 1초마다 다시 그리기 위한 틱. 오브젝트가 파괴되면 코루틴도 함께 멈춘다.
		private void OnEnable()
		{
			StartCoroutine(tickRoutine());
		}

		private IEnumerator tickRoutine()
		{
			WaitForSeconds wait = new WaitForSeconds(1f);
			while (true)
			{
				if (OnSecondTick != null)
				{
					OnSecondTick.Invoke();
				}

				yield return wait;
			}
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await OpenPresenterAsync(ct);
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── Presenter → View (공용) ───────────────────────────────────────

		public void SetName(string dungeonName)
		{
			_name.text = dungeonName;
		}

		public void SetThumbnail(string address)
		{
			icons.SetAsync(_thumbnail, address, GetDestroyToken()).Forget();
		}

		public void SetRefreshTime(string text)
		{
			_refreshTime.text = text;
		}

		public void SetEnterInteractable(bool value)
		{
			_enter.SetEnabled(value);
		}

		// ── 하위 클래스 ───────────────────────────────────────────────────

		// 팝업마다 다른 요소 찾기·입력 연결. Presenter 연결보다 먼저 불린다.
		protected abstract void BindContent(VisualElement root);

		protected abstract void InitializePresenter();

		protected abstract void DisposePresenter();

		protected abstract UniTask OpenPresenterAsync(CancellationToken ct);

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		private void onEnterClicked()
		{
			if (OnEnterClicked != null)
			{
				OnEnterClicked.Invoke();
			}
		}
	}
}
