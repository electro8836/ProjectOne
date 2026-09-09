using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 필드 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct FieldSlotData
	{
		public int fieldId;
		public int order;		// 액트 안에서의 순번 — Text_Num 에 그대로 쓴다
		public string name;
		public bool cleared;	// ReqLevel 을 채웠는가
		public bool current;	// 지금 서 있는 필드인가
	}

	// 액트 목록 팝업의 필드 칸(UIPrefab_FieldSlot).
	//
	// 상태는 셋이지만 축은 둘이다 — "클리어 했는가" 가 글자색을, "지금 여기인가" 가 틀 전체를 정한다.
	// 현재 칸은 나머지 색을 전부 덮어쓰므로 공통색을 먼저 칠하고 마지막에 덮는 순서로 그린다.
	public class FieldSlot : MonoBehaviour
	{
		[Header("프레임")]
		[SerializeField] private Image _bg;				// Bg
		[SerializeField] private Image _innerBorder;	// InnerBorder
		[SerializeField] private Image _border;			// Border
		[SerializeField] private Image _decoBg;			// DecoBg
		[SerializeField] private Image _decoBorder;		// DecoBorder
		[SerializeField] private GameObject _gradient;	// Gradient — 현재 칸에서만 켠다

		[Header("텍스트")]
		[SerializeField] private TMP_Text _numText;		// Group_Text/Text_Num
		[SerializeField] private TMP_Text _stageText;	// Group_Text/Text_Stage

		[Header("이동")]
		[SerializeField] private UIButton _moveButton;	// MoveButton

		[Header("색 — 현재가 아닐 때")]
		[SerializeField] private Color _bgNormal = new Color32(0x2C, 0x20, 0x49, 0xFF);
		[SerializeField] private Color _innerBorderNormal = new Color32(0x4A, 0x3C, 0x6D, 0xFF);
		[SerializeField] private Color _borderNormal = new Color32(0x25, 0x1C, 0x3E, 0xFF);
		[SerializeField] private Color _decoBgNormal = new Color32(0x2C, 0x20, 0x49, 0xFF);
		[SerializeField] private Color _decoBorderNormal = new Color32(0x4A, 0x3C, 0x6D, 0xFF);

		[Header("색 — 글자")]
		[SerializeField] private Color _textCleared = new Color32(0xC1, 0xB3, 0xFF, 0xFF);
		[SerializeField] private Color _textLocked = new Color32(0x5B, 0x50, 0x81, 0xFF);
		[SerializeField] private Color _textCurrent = new Color32(0xF2, 0xEE, 0xFF, 0xFF);

		[Header("색 — 현재 칸")]
		[SerializeField] private Color _bgCurrent = new Color32(0x84, 0x5C, 0xFD, 0xFF);
		[SerializeField] private Color _innerBorderCurrent = new Color32(0x9F, 0x87, 0xF7, 0xFF);
		[SerializeField] private Color _borderCurrent = new Color32(0x4E, 0x2A, 0x89, 0xFF);
		[SerializeField] private Color _decoBgCurrent = new Color32(0x55, 0x28, 0xA6, 0xFF);
		[SerializeField] private Color _decoBorderCurrent = new Color32(0x9F, 0x87, 0xF7, 0xFF);

		// 이동 요청. 어디로 갈지(그리고 갈 수 있는지)는 Presenter 가 정한다.
		public event Action<FieldSlot, int> OnMoveClicked;

		private int _fieldId;

		public int FieldId
		{
			get { return _fieldId; }
		}

		private void Awake()
		{
			_moveButton.OnClickEvent += onMoveClicked;
		}

		private void OnDestroy()
		{
			_moveButton.OnClickEvent -= onMoveClicked;
		}

		public void Bind(FieldSlotData data)
		{
			_fieldId = data.fieldId;

			_numText.text = data.order.ToString();
			_stageText.text = data.name;

			applyFrame(data.current);
			applyTextColor(data);

			// 이동 버튼은 "갈 수 있는 다른 곳" 에만 둔다 — 지금 있는 자리로 가는 버튼은 의미가 없고,
			// 아직 못 여는 곳은 눌러도 막힌다.
			_moveButton.gameObject.SetActive(data.cleared == true && data.current == false);
		}

		private void applyFrame(bool current)
		{
			_bg.color = current ? _bgCurrent : _bgNormal;
			_innerBorder.color = current ? _innerBorderCurrent : _innerBorderNormal;
			_border.color = current ? _borderCurrent : _borderNormal;
			_decoBg.color = current ? _decoBgCurrent : _decoBgNormal;
			_decoBorder.color = current ? _decoBorderCurrent : _decoBorderNormal;

			if (_gradient != null)
			{
				_gradient.SetActive(current);
			}
		}

		// 현재 칸은 클리어 여부와 무관하게 밝은 글자를 쓴다 — 틀이 이미 강조돼 있어 축을 하나로 줄인다.
		private void applyTextColor(FieldSlotData data)
		{
			Color color;
			if (data.current == true)
			{
				color = _textCurrent;
			}
			else if (data.cleared == true)
			{
				color = _textCleared;
			}
			else
			{
				color = _textLocked;
			}

			_numText.color = color;
			_stageText.color = color;
		}

		private void onMoveClicked()
		{
			if (OnMoveClicked != null)
			{
				OnMoveClicked.Invoke(this, _fieldId);
			}
		}
	}
}
