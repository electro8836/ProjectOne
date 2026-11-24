using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Network;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 계정 팝업(UIPrefab_AccountPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Account/AccountPopup.uxml.
	// 닉네임(누르면 변경 팝업) · UUID(복사) · 계정 연동(애플은 iOS 만 · 구글 · 디스코드) · 로그아웃 · 계정 삭제.
	//
	// 연동 · 로그아웃 · 계정 삭제는 화면과 상태 표시만 있다 — 실제 기능이 붙기 전까지 누르면 "준비 중" 툴팁을 띄운다.
	// 연동된 계정이 하나도 없으면 게스트로 보고 배지와 경고 줄을 띄운다.
	public class AccountPopup : ToolkitScreen
	{
		private const string ComingSoon = "준비 중입니다";

		// 연동 줄 1개 — 연동 전엔 "연동" 버튼, 연동되면 연동 계정 + "연동됨" 칩.
		private sealed class LinkRow
		{
			public VisualElement root;
			public Label id;
			public Button link;
			public VisualElement linked;
		}

		private Label _name;
		private Label _uuid;
		private Button _copyButton;
		private VisualElement _guestBadge;
		private VisualElement _guestWarn;
		private LinkRow _apple;
		private LinkRow _google;
		private LinkRow _discord;

		private UniTaskCompletionSource _tcs;
		private CancellationToken _ct;
		private string _uuidValue = string.Empty;
		private bool _isNicknameOpen;
		private bool _isDestroyed;

		protected override void OnBind(VisualElement root)
		{
			_name = root.Q<Label>("name");
			_uuid = root.Q<Label>("uuid");
			_copyButton = root.Q<Button>("copy");
			_copyButton.clicked += onCopyClicked;
			_guestBadge = root.Q("guest-badge");
			_guestWarn = root.Q("guest-warn");

			_apple = bindLink(root.Q("link-apple"));
			_google = bindLink(root.Q("link-google"));
			_discord = bindLink(root.Q("link-discord"));

			root.Q<Button>("name-button").clicked += onNameClicked;
			root.Q<Button>("logout").RegisterCallback<ClickEvent>(onComingSoonClicked);
			root.Q<Button>("delete").RegisterCallback<ClickEvent>(onComingSoonClicked);
			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
		}

		protected override void OnDestroy()
		{
			_isDestroyed = true;
			base.OnDestroy();
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();
			_ct = ct;

			refreshName();
			refreshUuid();
			refreshLinks();
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

		private LinkRow bindLink(VisualElement root)
		{
			LinkRow row = new LinkRow();
			row.root = root;
			row.id = root.Q<Label>("id");
			row.link = root.Q<Button>("link");
			row.link.RegisterCallback<ClickEvent>(onComingSoonClicked);
			row.linked = root.Q("linked");
			return row;
		}

		private void refreshName()
		{
			_name.text = MyPlayerProfile.PlayerName;
		}

		private void refreshUuid()
		{
			_uuidValue = NetworkManager.Instance.GetGamerId();
			_uuid.text = _uuidValue;
		}

		// 연동 상태 — 연동 기능이 아직 없어 모두 미연동(게스트)이다. 기능이 붙으면 연동된 계정 표시(가린 메일 등)를 넘긴다.
		private void refreshLinks()
		{
#if UNITY_IOS
			_apple.root.style.display = DisplayStyle.Flex;
#else
			_apple.root.style.display = DisplayStyle.None;
#endif
			bool anyLinked = false;
			anyLinked |= applyLink(_apple, string.Empty);
			anyLinked |= applyLink(_google, string.Empty);
			anyLinked |= applyLink(_discord, string.Empty);

			DisplayStyle guest = (anyLinked == true) ? DisplayStyle.None : DisplayStyle.Flex;
			_guestBadge.style.display = guest;
			_guestWarn.style.display = guest;
		}

		// linkedId 가 비면 미연동. 연동 여부를 돌려준다.
		private static bool applyLink(LinkRow row, string linkedId)
		{
			bool linked = string.IsNullOrEmpty(linkedId) == false;
			row.id.text = linkedId;
			row.id.style.display = linked ? DisplayStyle.Flex : DisplayStyle.None;
			row.link.style.display = linked ? DisplayStyle.None : DisplayStyle.Flex;
			row.linked.style.display = linked ? DisplayStyle.Flex : DisplayStyle.None;
			return linked;
		}

		private void onComingSoonClicked(ClickEvent evt)
		{
			UIManager.Instance.ShowTooltipAsync(ComingSoon, string.Empty, (VisualElement)evt.currentTarget, _ct).Forget();
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		private void onNameClicked()
		{
			if (_isNicknameOpen == true)
			{
				return;
			}

			openNicknameAsync().Forget();
		}

		// 변경 팝업이 닫히면 바뀐 이름을 다시 읽는다.
		private async UniTaskVoid openNicknameAsync()
		{
			_isNicknameOpen = true;
			await UIManager.Instance.ShowNicknamePopupAsync(_ct);
			_isNicknameOpen = false;

			if (_isDestroyed == true)
			{
				return;
			}

			refreshName();
		}

		private void onCopyClicked()
		{
			if (string.IsNullOrEmpty(_uuidValue) == true)
			{
				return;
			}

			GUIUtility.systemCopyBuffer = _uuidValue;
			UIManager.Instance.ShowTooltipAsync("복사 완료!", string.Empty, _copyButton, _ct).Forget();
		}
	}
}
