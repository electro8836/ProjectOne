using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 코스튬 목록의 슬롯 1칸(UIPrefab_CostumeSlot) 렌더 데이터.
	// 보유/미보유 판정과 색·문구 결정은 Presenter 가 끝내고, 슬롯은 받은 값을 그리기만 한다.
	public struct CostumeSlotData
	{
		public int costumeId;
		public bool owned;
		public string iconAddress;
		public string name;
		public string gradeText;			// 등급색이 입혀진 리치텍스트. 무기면 " / 쌍검" 처럼 마스터리가 붙는다
		public string acquisitionText;		// 미보유일 때만 보인다
		public Color bgColor;
		public Color innerBorderColor;
		public Color borderColor;
		public Color equipButtonColor;
		public Color previewIconColor;		// 입어보는 중이면 초록, 아니면 흰색
		public string equipLabel;			// "장착" / "해제"
	}

	// 코스튬 목록의 슬롯 1칸. 보유/미보유 두 상태를 같은 프리팹으로 표현한다.
	//
	// 미보유 칸도 입어보기는 된다 — 무엇을 사는지 보여 주는 것이 목록의 목적이라 미리보기를 막지 않는다.
	// 대신 장착 버튼은 아예 감추고 그 자리에 획득처를 띄운다.
	public class CostumeSlot : MonoBehaviour
	{
		[Header("배경")]
		[SerializeField] private Image _bg;				// Bg
		[SerializeField] private Image _innerBorder;	// InnerBorder
		[SerializeField] private Image _border;			// Border

		[Header("내용")]
		[SerializeField] private Image _icon;					// Icon
		[SerializeField] private TMP_Text _nameText;			// Group_Text/NameText
		[SerializeField] private TMP_Text _gradeText;			// Group_Text/GradeText
		[SerializeField] private TMP_Text _acquisitionText;		// AcquisitionText

		[Header("버튼")]
		[SerializeField] private UIButton _previewButton;	// Button_Preview
		[SerializeField] private Image _previewIcon;		// Button_Preview/Icon
		[SerializeField] private UIButton _equipButton;		// Button_Equip
		[SerializeField] private Image _equipButtonImage;	// Button_Equip 의 Image
		[SerializeField] private TMP_Text _equipLabel;		// Button_Equip/NameText

		public event Action<CostumeSlot> OnPreviewClicked;
		public event Action<CostumeSlot> OnEquipClicked;

		public int CostumeId
		{
			get { return _costumeId; }
		}

		private int _costumeId;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_previewButton.OnClickEvent += onPreviewClicked;
			_equipButton.OnClickEvent += onEquipClicked;
		}

		private void OnDestroy()
		{
			_previewButton.OnClickEvent -= onPreviewClicked;
			_equipButton.OnClickEvent -= onEquipClicked;

			releaseIcon();
		}

		public UniTask BindAsync(CostumeSlotData data, CancellationToken ct)
		{
			_costumeId = data.costumeId;

			_bg.color = data.bgColor;
			_innerBorder.color = data.innerBorderColor;
			_border.color = data.borderColor;

			_nameText.text = data.name;
			_gradeText.text = data.gradeText;

			// 미보유 칸도 입어보기가 되므로 보유 분기 밖에서 칠한다.
			_previewIcon.color = data.previewIconColor;

			// 획득처와 장착 버튼은 같은 자리를 나눠 쓴다 — 항상 둘 중 하나만 켜진다.
			_acquisitionText.gameObject.SetActive(data.owned == false);
			_equipButton.gameObject.SetActive(data.owned);

			if (data.owned == false)
			{
				_acquisitionText.text = data.acquisitionText;
			}
			else
			{
				_equipButtonImage.color = data.equipButtonColor;
				_equipLabel.text = data.equipLabel;
			}

			return setIcon(data.iconAddress, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onPreviewClicked()
		{
			if (OnPreviewClicked != null)
			{
				OnPreviewClicked.Invoke(this);
			}
		}

		private void onEquipClicked()
		{
			if (OnEquipClicked != null)
			{
				OnEquipClicked.Invoke(this);
			}
		}

		// PetInfoSlot.setIcon 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_icon.sprite = null;
				_icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
				_icon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}
