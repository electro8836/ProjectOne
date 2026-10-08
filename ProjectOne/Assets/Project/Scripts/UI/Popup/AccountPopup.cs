using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using ProjectOne.Network;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 계정 팝업. UIManager.ShowAccountPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 닉네임 변경 팝업이 이 위에 겹쳐 뜬다 — 닫히면 닉네임 표시를 다시 읽는다.
	public class AccountPopup : UIScreen
	{
		// "복사 완료" 안내 위치 — CopyButton 기준점에서 팝업 중심까지. 버튼 가로 중앙, 버튼 윗변에서 10 위.
		// 미리 잰 크기로 구한 값이다: 버튼 50x50(피벗이 오른쪽 변 중앙), 팝업 높이 112.
		//   x = -25(버튼 중앙), y = 25(버튼 절반) + 10(간격) + 56(팝업 절반)
		// 버튼·팝업 크기나 문구가 바뀌면 다시 잰다.
		private static readonly Vector2 CopyNoticeOffset = new Vector2(-25f, 91f);

		[Header("닫기")]
		[SerializeField] private UIButton _dimButton;		// Dim
		[SerializeField] private UIButton _exitButton;	// ExitButton

		[Header("닉네임")]
		[SerializeField] private TMP_Text _nameText;		// NameText
		[SerializeField] private UIButton _nameButton;	// NameButton — 닉네임 변경 팝업을 연다

		[Header("UUID")]
		[SerializeField] private TMP_Text _uuidText;		// UUIDText
		[SerializeField] private UIButton _copyButton;	// CopyButton — UUID 값만 클립보드에 복사한다

		private UniTaskCompletionSource _tcs;
		private CancellationToken _ct;
		private string _uuid = string.Empty;
		private bool _isNicknameOpen;
		private bool _isDestroyed;

		private void Awake()
		{
			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dim 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_dimButton.OnClickEvent += onCloseClicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_nameButton.OnClickEvent += onNameClicked;
			_copyButton.OnClickEvent += onCopyClicked;
		}

		private void OnDestroy()
		{
			_isDestroyed = true;

			_dimButton.OnClickEvent -= onCloseClicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_nameButton.OnClickEvent -= onNameClicked;
			_copyButton.OnClickEvent -= onCopyClicked;
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();
			_ct = ct;

			refreshName();
			refreshUuid();

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

		private void refreshName()
		{
			_nameText.text = $"닉네임 : {MyPlayerProfile.PlayerName}";
		}

		// 비로그인이거나 조회에 실패하면 빈 값이다.
		private void refreshUuid()
		{
			_uuid = NetworkManager.Instance.GetGamerId();
			_uuidText.text = $"UUID : {_uuid}";
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onCloseClicked()
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

		// 닉네임 변경 팝업이 닫히면 바뀌었을 수 있는 닉네임을 다시 표시한다.
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

		// "UUID : " 접두사는 빼고 값만 복사한다. 복사됐다는 안내는 버튼 위에 잠깐 띄운다.
		private void onCopyClicked()
		{
			if (string.IsNullOrEmpty(_uuid) == true)
			{
				return;
			}

			GUIUtility.systemCopyBuffer = _uuid;
			UIManager.Instance.ShowSimplePopupAsync("복사 완료", _copyButton.transform as RectTransform, _ct, CopyNoticeOffset).Forget();
		}
	}
}
