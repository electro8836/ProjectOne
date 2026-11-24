using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Mail;

namespace ProjectOne.UI
{
	// 메뉴 팝업이 무엇으로 닫혔는가. 이어서 할 일(우편함 열기·종료 확인)은 호출자가 정한다.
	public enum MenuPopupResult
	{
		None = 0,
		Mail,
		Quit,
		Account,
		Ranking,
		HeroPass,
		DailyBonus,
		Quest,
		Map
	}

	// 메인 메뉴 팝업(UIPrefab_MenuPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Menu/MenuPopup.uxml.
	// UIManager.ShowMenuPopupAsync 가 열고 결과를 기다린다.
	public class MenuPopup : ToolkitScreen
	{
		private UniTaskCompletionSource _tcs;
		private MenuPopupResult _result;
		private VisualElement _mailBadge;

		protected override void OnBind(VisualElement root)
		{
			_mailBadge = root.Q("mail-badge");
			_mailBadge.style.display = DisplayStyle.None;

			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
			root.Q<Button>("close").clicked += onCloseClicked;
			root.Q<Button>("quest").clicked += onQuestClicked;
			root.Q<Button>("setting").clicked += onSettingClicked;
			root.Q<Button>("account").clicked += onAccountClicked;
			root.Q<Button>("mail").clicked += onMailClicked;
			root.Q<Button>("ranking").clicked += onRankingClicked;
			root.Q<Button>("pass").clicked += onPassClicked;
			root.Q<Button>("daily").clicked += onDailyClicked;
			root.Q<Button>("afk").clicked += onAfkClicked;
			root.Q<Button>("quit").clicked += onQuitClicked;
			root.Q<Button>("map").clicked += onMapClicked;
		}

		public async UniTask<MenuPopupResult> ShowAsync(CancellationToken ct)
		{
			_result = MenuPopupResult.None;
			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();

			bool hasUnread = await MailSystem.HasUnreadAsync(ct);
			_mailBadge.style.display = hasUnread ? DisplayStyle.Flex : DisplayStyle.None;

			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			return _result;
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		private void onCloseClicked()
		{
			Close();
		}

		private void onMapClicked()
		{
			_result = MenuPopupResult.Map;
			Close();
		}

		private void onQuestClicked()
		{
			_result = MenuPopupResult.Quest;
			Close();
		}

		private void onMailClicked()
		{
			_result = MenuPopupResult.Mail;
			Close();
		}

		private void onQuitClicked()
		{
			_result = MenuPopupResult.Quit;
			Close();
		}

		private void onRankingClicked()
		{
			_result = MenuPopupResult.Ranking;
			Close();
		}

		private void onPassClicked()
		{
			_result = MenuPopupResult.HeroPass;
			Close();
		}

		private void onDailyClicked()
		{
			_result = MenuPopupResult.DailyBonus;
			Close();
		}

		private void onAccountClicked()
		{
			_result = MenuPopupResult.Account;
			Close();
		}

		private void onSettingClicked()
		{
			Debug.Log("[MenuPopup] 설정 기능 준비 중");
		}

		private void onAfkClicked()
		{
			Debug.Log("[MenuPopup] 방치모드 기능 준비 중");
		}
	}
}
