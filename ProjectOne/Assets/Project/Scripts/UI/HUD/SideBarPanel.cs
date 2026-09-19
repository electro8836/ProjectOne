using UnityEngine;
using DG.Tweening;

namespace ProjectOne.UI
{
	// 메인 HUD 우측 사이드바. FoldButton 으로 ButtonGroup 을 화면 밖으로 밀거나 되돌린다.
	// 이동 중에는 FoldButton 을 잠가 중복 입력을 막는다.
	public class SideBarPanel : MonoBehaviour
	{
		private const float SlideDuration = 1f;	// 접기/펼치기 블렌드 시간(고정)
		private const float FoldedX = 150f;		// 접힘(화면 밖) 위치
		private const float ExpandedX = 0f;		// 펼침 위치

		[Header("Fold")]
		[SerializeField] private RectTransform _buttonGroup;
		[SerializeField] private UIButton _foldButton;
		[SerializeField] private GameObject _closeImage;	// 펼침 상태에서 표시
		[SerializeField] private GameObject _openIcon;		// 접힘 상태에서 표시

		[Header("Buttons")]
		[SerializeField] private UIButton _buttonRank;
		[SerializeField] private UIButton _buttonReward;
		[SerializeField] private UIButton _buttonPass;
		[SerializeField] private UIButton _buttonLogin;

		private bool _isExpanded = true;
		private bool _isSliding;

		private void Awake()
		{
			if (_foldButton != null)
			{
				_foldButton.OnClickEvent += onFoldClicked;
			}

			if (_buttonRank != null)
			{
				_buttonRank.OnClickEvent += onRankClicked;
			}

			if (_buttonReward != null)
			{
				_buttonReward.OnClickEvent += onRewardClicked;
			}

			if (_buttonPass != null)
			{
				_buttonPass.OnClickEvent += onPassClicked;
			}

			if (_buttonLogin != null)
			{
				_buttonLogin.OnClickEvent += onLoginClicked;
			}

			// 프리팹 저장값과 무관하게 펼침 상태로 시작한다
			applyState(true, false);
		}

		private void OnDestroy()
		{
			if (_buttonGroup != null)
			{
				_buttonGroup.DOKill();
			}

			if (_foldButton != null)
			{
				_foldButton.OnClickEvent -= onFoldClicked;
			}

			if (_buttonRank != null)
			{
				_buttonRank.OnClickEvent -= onRankClicked;
			}

			if (_buttonReward != null)
			{
				_buttonReward.OnClickEvent -= onRewardClicked;
			}

			if (_buttonPass != null)
			{
				_buttonPass.OnClickEvent -= onPassClicked;
			}

			if (_buttonLogin != null)
			{
				_buttonLogin.OnClickEvent -= onLoginClicked;
			}
		}

		private void onFoldClicked()
		{
			if (_isSliding) { return; }

			applyState(!_isExpanded, true);
		}

		// 펼침/접힘 상태를 적용한다. animate 가 false 면 즉시 반영.
		private void applyState(bool expanded, bool animate)
		{
			_isExpanded = expanded;

			if (_closeImage != null)
			{
				_closeImage.SetActive(expanded);
			}

			if (_openIcon != null)
			{
				_openIcon.SetActive(!expanded);
			}

			if (_buttonGroup == null) { return; }

			float targetX = expanded ? ExpandedX : FoldedX;
			_buttonGroup.DOKill();

			if (animate == false)
			{
				_buttonGroup.anchoredPosition = new Vector2(targetX, _buttonGroup.anchoredPosition.y);
				return;
			}

			_isSliding = true;
			if (_foldButton != null)
			{
				_foldButton.interactable = false;
			}

			_buttonGroup.DOAnchorPosX(targetX, SlideDuration).SetEase(Ease.OutCubic).OnComplete(onSlideComplete);
		}

		private void onSlideComplete()
		{
			_isSliding = false;

			if (_foldButton != null)
			{
				_foldButton.interactable = true;
			}
		}

		// 아래 4개는 정식 화면 연결 전까지의 임시 처리다. 화면이 준비되면 교체한다.
		private void onRankClicked()
		{
			Debug.Log("[SideBar] 랭킹 기능 준비 중");
		}

		private void onRewardClicked()
		{
			Debug.Log("[SideBar] 보상 기능 준비 중");
		}

		private void onPassClicked()
		{
			Debug.Log("[SideBar] 패스 기능 준비 중");
		}

		private void onLoginClicked()
		{
			Debug.Log("[SideBar] 출석 기능 준비 중");
		}
	}
}
