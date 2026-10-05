using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using ProjectOne.Currency;
using ProjectOne.Network;
using ProjectOne.Ranking;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 닉네임 변경 팝업. UIManager.ShowNicknamePopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 입력 검사는 서버와 같은 규칙(NicknameRules)으로 미리 하고, 중복·비용 차감은 서버가 확정한다.
	// 첫 변경은 무료, 그 뒤로는 다이아가 모자라면 변경 버튼을 누를 수 없다.
	public class NicknamePopup : UIScreen
	{
		// 실패 사유를 보여 주는 시간(초).
		private const float ErrorShowSeconds = 5f;

		[Header("닫기")]
		[SerializeField] private UIButton _dimButton;		// Dimmed
		[SerializeField] private UIButton _cancelButton;	// Frame/BottomButtons/Button_Cancel

		[Header("입력")]
		[SerializeField] private TMP_InputField _input;	// Frame/Input
		[SerializeField] private TMP_Text _byteText;		// Frame/ByteText
		[SerializeField] private TMP_Text _errorText;		// Frame/ErrorText — 실패 사유. 잠깐 띄웠다가 숨긴다

		[Header("변경")]
		[SerializeField] private UIButton _changeButton;	// Frame/BottomButtons/Button_Change
		[SerializeField] private TMP_Text _costText;		// Button_Change/Group/CostText

		private UniTaskCompletionSource _tcs;
		private Coroutine _errorRoutine;
		private bool _isRequesting;
		private bool _isDestroyed;

		private void Awake()
		{
			// 프리펩 기본 문구가 비치지 않도록 숨긴 채로 시작한다.
			_errorText.gameObject.SetActive(false);

			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dimmed 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_dimButton.OnClickEvent += onCloseClicked;
			_cancelButton.OnClickEvent += onCloseClicked;
			_changeButton.OnClickEvent += onChangeClicked;
			_input.onValueChanged.AddListener(onInputChanged);
		}

		private void OnDestroy()
		{
			_isDestroyed = true;

			_dimButton.OnClickEvent -= onCloseClicked;
			_cancelButton.OnClickEvent -= onCloseClicked;
			_changeButton.OnClickEvent -= onChangeClicked;
			_input.onValueChanged.RemoveListener(onInputChanged);
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();

			_input.SetTextWithoutNotify(string.Empty);
			refreshByteText(0);
			refreshCost();

			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 표시 ──────────────────────────────────────────────────────

		// 최소 길이에 못 미치는 동안은 현재 값을 빨갛게 보인다.
		private void refreshByteText(int bytes)
		{
			if (bytes < NicknameRules.MinBytes)
			{
				_byteText.text = $"<color=red>{bytes}</color>/{NicknameRules.MaxBytes}byte";
				return;
			}

			_byteText.text = $"{bytes}/{NicknameRules.MaxBytes}byte";
		}

		// 비용 표시와 변경 버튼 — 다이아가 모자라면 누를 수 없다.
		private void refreshCost()
		{
			int cost = NicknameRules.GetCost(MyPlayerProfile.NicknameChangeCount);
			if (cost <= 0)
			{
				_costText.text = "무료";
				_changeButton.interactable = true;
				return;
			}

			_costText.text = cost.ToString();
			_changeButton.interactable = (CurrencyManager.Instance.GetAmount(NicknameRules.CostCurrency) >= cost);
		}

		// 실패 사유를 띄우고 ErrorShowSeconds 뒤에 숨긴다.
		// 떠 있는 동안 다시 불리면 앞선 타이머를 끊고 새로 센다 — 앞선 타이머가 새 문구를 일찍 꺼 버리지 않는다.
		private void showError(string message)
		{
			if (_errorRoutine != null)
			{
				StopCoroutine(_errorRoutine);
			}

			_errorText.text = message;
			_errorText.gameObject.SetActive(true);
			_errorRoutine = StartCoroutine(hideErrorAfterDelay());
		}

		// 일시정지(timeScale 0) 중에도 사라지도록 실제 시간으로 센다.
		private IEnumerator hideErrorAfterDelay()
		{
			yield return new WaitForSecondsRealtime(ErrorShowSeconds);

			_errorText.gameObject.SetActive(false);
			_errorRoutine = null;
		}

		// ── 입력 ──────────────────────────────────────────────────────

		// 최대 길이를 넘는 글자는 받지 않는다.
		private void onInputChanged(string value)
		{
			string trimmed = NicknameRules.TrimToMaxBytes(value);
			if (trimmed.Length != value.Length)
			{
				_input.SetTextWithoutNotify(trimmed);
			}

			refreshByteText(NicknameRules.GetByteCount(trimmed));
		}

		private void onCloseClicked()
		{
			Close();
		}

		private void onChangeClicked()
		{
			if (_isRequesting == true)
			{
				return;
			}

			string nickname = _input.text;

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

		// 변경과 차감은 서버에 이미 저장됐다 — 팝업이 먼저 닫혔어도 로컬 값은 맞춘다.
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

		// ── 안내 문구 ─────────────────────────────────────────────────

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

		// 서버 거절 사유(NicknameRules.Error*) → 안내 문구.
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
