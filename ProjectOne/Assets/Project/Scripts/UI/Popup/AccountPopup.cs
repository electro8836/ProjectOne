using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Network;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 계정 팝업(UIPrefab_AccountPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Account/AccountPopup.uxml.
	// 닉네임(누르면 변경 팝업)과 UUID(복사 버튼)를 보여 준다.
	public class AccountPopup : ToolkitScreen
	{
		private Label _name;
		private Label _uuid;
		private Button _copyButton;

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

			root.Q<Button>("name-button").clicked += onNameClicked;
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

		private void refreshName()
		{
			_name.text = $"닉네임 : {MyPlayerProfile.PlayerName}";
		}

		private void refreshUuid()
		{
			_uuidValue = NetworkManager.Instance.GetGamerId();
			_uuid.text = $"UUID : {_uuidValue}";
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

		// UUID 값만 복사한다(앞의 "UUID : " 는 빼고).
		private void onCopyClicked()
		{
			if (string.IsNullOrEmpty(_uuidValue) == true)
			{
				return;
			}

			GUIUtility.systemCopyBuffer = _uuidValue;
			UIManager.Instance.ShowTooltipAsync("복사 완료", string.Empty, _copyButton, _ct).Forget();
		}
	}
}
