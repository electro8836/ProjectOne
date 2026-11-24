using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Network;
using ProjectOne.Ranking;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 닉네임 변경 팝업(UIPrefab_NicknamePopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Account/NicknamePopup.uxml.
	// 입력 길이(byte)를 실시간으로 보여 주고, 규칙 검사 → 서버 요청 → 성공 시 프로필·재화 반영 후 닫는다.
	// 실패 사유는 입력란 아래에 잠깐 띄웠다가 숨긴다.
	public class NicknamePopup : ToolkitScreen
	{
		private const long ErrorShowMs = 5000;

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private TextField _input;
		private Label _byteText;
		private Label _errorText;
		private Button _changeButton;
		private Label _costText;
		private VisualElement _costIcon;
		private IVisualElementScheduledItem _errorHide;

		private UniTaskCompletionSource _tcs;
		private bool _isRequesting;
		private bool _isDestroyed;

		protected override void OnBind(VisualElement root)
		{
			_input = root.Q<TextField>("input");
			_input.RegisterValueChangedCallback(onInputChanged);
			_byteText = root.Q<Label>("byte");
			root.Q<Label>("rule").text = $"{NicknameRules.MinBytes}~{NicknameRules.MaxBytes}byte";
			_errorText = root.Q<Label>("error");
			_errorText.style.display = DisplayStyle.None;

			_changeButton = root.Q<Button>("change");
			_changeButton.clicked += onChangeClicked;
			_costText = root.Q<Label>("cost");
			_costIcon = root.Q("cost-icon");

			root.Q<Button>("cancel").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
		}

		protected override void OnDestroy()
		{
			_isDestroyed = true;
			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();

			_input.SetValueWithoutNotify(string.Empty);
			refreshByteText(0);
			refreshCost();
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

		// ── 표시 ─────────────────────────────────────────────────────────

		// 최소 길이 미만이면 숫자를 빨갛게.
		private void refreshByteText(int bytes)
		{
			if (bytes < NicknameRules.MinBytes)
			{
				_byteText.text = $"<color=#FF6B6B>{bytes}</color>/{NicknameRules.MaxBytes}byte";
				return;
			}

			_byteText.text = $"{bytes}/{NicknameRules.MaxBytes}byte";
		}

		// 첫 변경은 무료, 이후는 재화. 모자라면 버튼을 잠근다.
		private void refreshCost()
		{
			int cost = NicknameRules.GetCost(MyPlayerProfile.NicknameChangeCount);
			if (cost <= 0)
			{
				_costText.text = "최초 무료";
				_costIcon.style.display = DisplayStyle.None;
				_changeButton.SetEnabled(true);
				return;
			}

			_costText.text = cost.ToString();
			_costIcon.style.display = DisplayStyle.Flex;
			Table_Currency.Row row = Table_Currency.Get(NicknameRules.CostCurrency);
			_icons.SetAsync(_costIcon, (row != null) ? row.Icon : string.Empty, this.GetCancellationTokenOnDestroy()).Forget();
			_changeButton.SetEnabled(CurrencyManager.Instance.GetAmount(NicknameRules.CostCurrency) >= cost);
		}

		private void showError(string message)
		{
			_errorText.text = message;
			_errorText.style.display = DisplayStyle.Flex;

			if (_errorHide != null)
			{
				_errorHide.Pause();
			}

			_errorHide = _errorText.schedule.Execute(hideError).StartingIn(ErrorShowMs);
		}

		private void hideError()
		{
			_errorText.style.display = DisplayStyle.None;
			_errorHide = null;
		}

		// ── 입력 ─────────────────────────────────────────────────────────

		// 최대 길이를 넘는 입력은 잘라 되돌린다.
		private void onInputChanged(ChangeEvent<string> evt)
		{
			string value = evt.newValue ?? string.Empty;
			string trimmed = NicknameRules.TrimToMaxBytes(value);
			if (trimmed.Length != value.Length)
			{
				_input.SetValueWithoutNotify(trimmed);
			}

			refreshByteText(NicknameRules.GetByteCount(trimmed));
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		private void onChangeClicked()
		{
			if (_isRequesting == true)
			{
				return;
			}

			string nickname = _input.value;
			NicknameError error = NicknameRules.Validate(nickname);
			if (error != NicknameError.None)
			{
				showError(toMessage(error));
				return;
			}

			if (nickname == MyPlayerProfile.PlayerName)
			{
				showError("현재 닉네임과 같습니다.");
				return;
			}

			ChangeNicknameRequest request = new ChangeNicknameRequest();
			request.nickname = nickname;

			_isRequesting = true;
			NetworkManager.Instance.RequestChangeNickname(request, onChanged);
		}

		private void onChanged(bool success, ChangeNicknameResponse data, string error)
		{
			_isRequesting = false;

			if (success == false || data == null)
			{
				Debug.LogWarning($"[NicknamePopup] 닉네임 변경 실패: {error}");
				if (_isDestroyed == false)
				{
					showError(toMessage(error));
				}

				return;
			}

			MyPlayerProfile.SetNickname(data.nickname);
			MyPlayerProfile.SetNicknameChangeCount(data.changeCount);
			if (data.spent != null)
			{
				CurrencyManager.Instance.TrySpend((EDT.Currency)data.spent.currencyId, data.spent.amount);
			}

			if (_isDestroyed == true)
			{
				return;
			}

			Close();
		}

		private static string toMessage(NicknameError error)
		{
			switch (error)
			{
				case NicknameError.TooShort:
					return $"닉네임은 {NicknameRules.MinBytes}byte 이상이어야 합니다.";

				case NicknameError.TooLong:
					return $"닉네임은 {NicknameRules.MaxBytes}byte 이하여야 합니다.";

				case NicknameError.InvalidChar:
					return "공백이나 특수문자는 사용할 수 없습니다.";

				case NicknameError.StartsWithDigit:
					return "첫 글자에는 숫자를 사용할 수 없습니다.";

				case NicknameError.BanWord:
					return "사용할 수 없는 단어가 포함되어 있습니다.";

				case NicknameError.BadWord:
					return "욕설이나 비속어는 사용할 수 없습니다.";

				default:
					return string.Empty;
			}
		}

		private static string toMessage(string serverError)
		{
			if (serverError == NicknameRules.ErrorDuplicated)
			{
				return "이미 사용 중인 닉네임입니다.";
			}

			if (serverError == NicknameRules.ErrorSame)
			{
				return "현재 닉네임과 같습니다.";
			}

			if (serverError == NicknameRules.ErrorNotEnoughCurrency)
			{
				return "다이아가 부족합니다.";
			}

			if (string.IsNullOrEmpty(serverError) == false && serverError.StartsWith(NicknameRules.ErrorInvalid) == true)
			{
				return "사용할 수 없는 닉네임입니다.";
			}

			return "닉네임 변경에 실패했습니다. 잠시 후 다시 시도해 주세요.";
		}
	}
}
