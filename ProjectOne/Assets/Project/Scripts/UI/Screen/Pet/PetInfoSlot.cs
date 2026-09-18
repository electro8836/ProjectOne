using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 펫 목록의 슬롯 1칸(UIPrefab_PetInfoSlot) 렌더 데이터.
	// 보유/미보유 판정과 색·문구 결정은 Presenter 가 끝내고, 슬롯은 받은 값을 그리기만 한다.
	public struct PetSlotData
	{
		public EDT.Pet pet;
		public bool owned;
		public string imageAddress;
		public string name;
		public int level;			// 미보유는 0 — 뱃지 자체가 꺼지므로 쓰이지 않는다
		public string bonusText;	// 보유는 수치까지, 미보유는 "공격력 증가" 처럼 간략히
		public bool equipped;		// 장착 중인 한 마리만 true — 장착은 보유를 전제한다
		public Color bgColor;
		public Color gradientColor;
		public Color textColor;
	}

	// 펫 목록의 슬롯 1칸. 보유/미보유 두 상태를 같은 프리팹으로 표현한다.
	//
	// 잠긴 칸은 눌러도 열 것이 없으므로 버튼 자체를 잠근다 — 클릭 뒤에 팝업이 스스로 되돌아 나오는 것보다
	// 누를 수 없는 편이 분명하다.
	public class PetInfoSlot : MonoBehaviour
	{
		[Header("클릭")]
		[SerializeField] private UIButton _button;			// 루트

		[Header("배경 (등급색)")]
		[SerializeField] private Image _bgMask;				// Bg_Mask
		[SerializeField] private GameObject _bgGradient;	// Bg_Mask/BgGradient
		[SerializeField] private Image _bgGradientImage;	// Bg_Mask/BgGradient 의 Image
		[SerializeField] private GameObject _bgDeco;		// Bg_Mask/BgDeco

		[Header("내용")]
		[SerializeField] private Image _petImage;			// Image
		[SerializeField] private GameObject _levelRoot;		// Level
		[SerializeField] private TMP_Text _levelText;		// Level/LevelText
		[SerializeField] private GameObject _lockIcon;		// Icon_Lock
		[SerializeField] private TMP_Text _nameText;		// NameText
		[SerializeField] private TMP_Text _bonusTitle;		// BonusTitle
		[SerializeField] private TMP_Text _bonusText;		// BonusText

		[Header("장착 표시")]
		[SerializeField] private GameObject _equipText;		// EquipText ("[장착중]")
		[SerializeField] private GameObject _equipedFrame;	// Equiped (강조 테두리)

		public event Action<PetInfoSlot> OnClicked;

		public EDT.Pet PetId
		{
			get { return _petId; }
		}

		private EDT.Pet _petId = EDT.Pet.None;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;

			releaseIcon();
		}

		public UniTask BindAsync(PetSlotData data, CancellationToken ct)
		{
			_petId = data.pet;

			_bgMask.color = data.bgColor;
			_bgGradient.SetActive(data.owned);
			_bgDeco.SetActive(data.owned);

			// 꺼진 그라데이션의 색은 건드리지 않는다 — 어차피 보이지 않고, 켜질 때 다시 정해진다.
			if (data.owned == true)
			{
				_bgGradientImage.color = data.gradientColor;
				_levelText.text = data.level.ToString();
			}

			// 레벨 뱃지와 자물쇠는 같은 자리를 나눠 쓴다 — 항상 둘 중 하나만 켜진다.
			_levelRoot.SetActive(data.owned);
			_lockIcon.SetActive(data.owned == false);

			_nameText.text = data.name;
			_nameText.color = data.textColor;
			_bonusTitle.color = data.textColor;
			_bonusText.text = data.bonusText;
			_bonusText.color = data.textColor;

			// 장착은 보유를 전제하므로 미보유 슬롯에서는 자연히 꺼진다.
			// 색은 손대지 않는다 — 장착 중이면 디밍 대상이 아니고, 문구 색은 프리팹이 소유한다.
			_equipText.SetActive(data.equipped);
			_equipedFrame.SetActive(data.equipped);

			_button.interactable = data.owned;

			return setIcon(data.imageAddress, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}

		// MasteryInfoSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		// Pet_01~06 은 Atlas_Illust 에 포함되어 있어 실제로는 동기 경로로 끝난다.
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
				_petImage.sprite = null;
				_petImage.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_petImage.sprite = atlasSprite;
				_petImage.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_petImage.enabled = false;

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
				_petImage.sprite = icon;
				_petImage.enabled = true;
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
