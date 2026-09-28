using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 균열 스킬 정보 팝업 — 이름, 게이지(Req/Max), 설명. Dim·ExitButton 으로 닫는다.
	//
	// UIManager 를 거치지 않고 RiftDungeonPopup 이 자기 아래에 한 번 만들어 켜고 끈다 — UIManager 의 팝업 토큰을
	// 같이 쓰면 이 팝업을 여는 순간 부모 팝업이 취소된다.
	public class RiftSkillInfoPopup : MonoBehaviour
	{
		[SerializeField] private RectTransform _frame;	// Frame
		[SerializeField] private TMP_Text _nameText;		// Frame/NameText
		[SerializeField] private TMP_Text _gaugeText;		// Frame/GaugeText
		[SerializeField] private TMP_Text _descText;		// Frame/DescText
		[SerializeField] private UIButton _exitButton;		// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;		// Dim

		[Tooltip("앵커 위쪽 가장자리와 프레임 아래쪽 사이 간격")]
		[SerializeField] private float _gap = 5f;

		[Tooltip("화면 가장자리와 프레임 사이 여유 — 프레임 밖으로 튀어나온 테두리·닫기 버튼(약 10) + 여유 5")]
		[SerializeField] private float _screenMargin = 15f;

		// GetWorldCorners 버퍼 — 열 때마다 새로 만들지 않는다. 0:좌하 1:좌상 2:우상 3:우하
		private readonly Vector3[] _anchorCorners = new Vector3[4];
		private readonly Vector3[] _frameCorners = new Vector3[4];
		private readonly Vector3[] _screenCorners = new Vector3[4];

		private void Awake()
		{
			_exitButton.OnClickEvent += Hide;
			_dimButton.OnClickEvent += Hide;
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= Hide;
			_dimButton.OnClickEvent -= Hide;
		}

		// anchor 의 위쪽 가장자리 가운데에 프레임 아래쪽 가운데를 붙이고, 화면 밖으로 나간 만큼 되민다.
		public void Show(string skillName, int reqGauge, int maxGauge, string desc, RectTransform anchor)
		{
			_nameText.text = skillName;
			_gaugeText.text = "게이지 " + reqGauge.ToString() + "/" + maxGauge.ToString();
			_descText.text = desc;

			gameObject.SetActive(true);
			placeAbove(anchor);
			keepInsideScreen();
		}

		public void Hide()
		{
			gameObject.SetActive(false);
		}

		// 월드 좌표로 맞춘다 — 프레임과 앵커가 다른 부모 아래에 있어도 캔버스가 같으면 성립한다.
		private void placeAbove(RectTransform anchor)
		{
			if (anchor == null)
			{
				return;
			}

			anchor.GetWorldCorners(_anchorCorners);
			Vector3 anchorTop = (_anchorCorners[1] + _anchorCorners[2]) * 0.5f;

			_frame.GetWorldCorners(_frameCorners);
			Vector3 frameBottom = (_frameCorners[0] + _frameCorners[3]) * 0.5f;

			_frame.position += anchorTop - frameBottom;

			// 간격은 부모 로컬 단위로 준다 — 캔버스 스케일과 무관하게 같은 거리가 된다.
			_frame.anchoredPosition += new Vector2(0f, _gap);
		}

		// 루트는 화면 전체로 늘어나 있다 — 프레임이 그 밖으로 나간 방향으로만 되민다.
		// 앵커가 화면 가장자리에 붙어 있으면(SelectRoot 는 오른쪽 끝) 프레임도 가장자리에 붙은 채 앵커 위에 뜬다.
		private void keepInsideScreen()
		{
			((RectTransform)transform).GetWorldCorners(_screenCorners);
			_frame.GetWorldCorners(_frameCorners);

			// 여백은 UI 단위로 준다 — 월드 좌표에서 비교하므로 캔버스 스케일을 곱한다.
			float margin = _screenMargin * transform.lossyScale.x;
			Vector3 inset = new Vector3(margin, margin, 0f);

			Vector3 screenMin = _screenCorners[0] + inset;
			Vector3 screenMax = _screenCorners[2] - inset;
			Vector3 frameMin = _frameCorners[0];
			Vector3 frameMax = _frameCorners[2];

			Vector3 shift = Vector3.zero;

			if (frameMin.x < screenMin.x)
			{
				shift.x = screenMin.x - frameMin.x;
			}
			else if (frameMax.x > screenMax.x)
			{
				shift.x = screenMax.x - frameMax.x;
			}

			if (frameMin.y < screenMin.y)
			{
				shift.y = screenMin.y - frameMin.y;
			}
			else if (frameMax.y > screenMax.y)
			{
				shift.y = screenMax.y - frameMax.y;
			}

			_frame.position += shift;
		}
	}
}
