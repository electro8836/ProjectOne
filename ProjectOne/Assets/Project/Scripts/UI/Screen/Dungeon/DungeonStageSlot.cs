using System;
using ProjectOne.Resources;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 던전 단계 슬롯의 상태. 프레임 스프라이트와 글자색이 여기서 갈린다.
	public enum DungeonStageState
	{
		Locked = 0,		// 아직 못 여는 단계
		Current,		// 지금 도전할 수 있는 단계
		Cleared			// 이미 깬 단계
	}

	// 던전 단계 슬롯 1칸 렌더 데이터.
	public struct DungeonStageSlotData
	{
		public int stage;
		public DungeonStageState state;
	}

	// 골드던전 팝업의 단계 칸(UIPrefab_DungeonStageSlot).
	//
	// 선택 표시(Select)는 스크롤 중앙이 어디냐로 정해지므로 슬롯이 스스로 켜지 않는다 —
	// CenterSnapScroll 이 알려주면 팝업이 SetSelected 로 지시한다.
	public class DungeonStageSlot : MonoBehaviour
	{
		[Header("프레임")]
		[SerializeField] private Image _frame;			// 루트 Image
		[SerializeField] private GameObject _select;	// Select
		[SerializeField] private GameObject _clearIcon;	// ClearIcon

		[Header("텍스트")]
		[SerializeField] private TMP_Text _stageText;	// StageText

		[Header("입력")]
		[SerializeField] private UIButton _button;		// 루트

		[Header("프레임 스프라이트 이름 (Atlas_Frame)")]
		[SerializeField] private string _spriteCurrent = "ItemFrame_Square_01_Blue";
		[SerializeField] private string _spriteCleared = "ItemFrame_Square_01_Navy";
		[SerializeField] private string _spriteLocked = "ItemFrame_Square_01_Gray";

		[Header("글자색")]
		[SerializeField] private Color _textOpen = Color.white;
		[SerializeField] private Color _textLocked = Color.red;

		// 눌린 칸을 중앙으로 가져오는 것은 스크롤의 일이다 — 슬롯은 자기가 눌렸다는 것만 알린다.
		// 잠긴 단계도 통지한다(중앙으로 와서 권장레벨을 보여줘야 한다). 입장 가능 여부는 Presenter 가 본다.
		public event Action<DungeonStageSlot> OnClicked;

		private int _stage;

		public int Stage
		{
			get { return _stage; }
		}

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
		}

		public void Bind(DungeonStageSlotData data)
		{
			_stage = data.stage;
			_stageText.text = data.stage.ToString();

			bool cleared = data.state == DungeonStageState.Cleared;
			bool open = data.state != DungeonStageState.Locked;

			_clearIcon.SetActive(cleared);
			_stageText.color = open ? _textOpen : _textLocked;

			applyFrame(data.state);
		}

		public void SetSelected(bool selected)
		{
			_select.SetActive(selected);
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}

		// 프레임 스프라이트 셋은 개별 어드레서블 등록이 없고 Atlas_Frame 아틀라스에만 들어 있다.
		// 아틀라스는 이미 메모리에 올라와 있으므로 동기로 꺼내 쓴다.
		private void applyFrame(DungeonStageState state)
		{
			string spriteName;
			if (state == DungeonStageState.Cleared)
			{
				spriteName = _spriteCleared;
			}
			else if (state == DungeonStageState.Current)
			{
				spriteName = _spriteCurrent;
			}
			else
			{
				spriteName = _spriteLocked;
			}

			Sprite sprite = AtlasManager.Instance.Get(spriteName);
			if (sprite == null)
			{
				Debug.LogWarning($"[DungeonStageSlot] 아틀라스에 {spriteName} 이 없습니다 — Atlas_Frame 로드를 확인하세요.");
				return;
			}

			_frame.sprite = sprite;
		}
	}
}
