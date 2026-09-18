using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 펫 정보 1회분 렌더 데이터.
	public struct PetPopupData
	{
		public string name;
		public string levelText;	// "레벨 : 1/20 일반"
		public string gradeText;	// "등급 : 일반"
		public string bonusText;
		public string equipText;	// "장착 효과 : ..."
		public bool equipped;
		public Color bgColor;		// Bg_Mask — 등급 bg
		public Color gradientColor;	// BgGradient — 등급 border
	}

	// 강화/승급 버튼 1개분 렌더 데이터.
	// 판정은 Presenter 가 Block enum 하나만 보고 끝낸다 — 버튼 잠금과 실제 실행이 같은 규칙을 본다.
	public struct PetCostData
	{
		public bool available;		// 버튼을 누를 수 있는가
		public bool notEnough;		// 재화 부족 — 이때만 비용을 빨갛게
		public bool hasCost;		// 한계 도달이면 false — 아이콘을 감추고 한계 문구를 낸다
		public string limitText;	// "최대레벨" / "최대등급"
		public string iconAddress;
		public string amountText;
	}

	// 펫 강화 팝업의 View(MVP). UIManager.ShowPetEnhancePopupAsync 가 닫힘을 기다린다.
	public class PetEnhancePopup : UIScreen, IView
	{
		// 재화가 모자랄 때의 비용 문구 색 (ShopProductSlotBase._lackColor 와 같은 역할).
		[SerializeField] private Color _lackColor = Color.red;
		[SerializeField] private Color _costColor = Color.white;

		[Header("등급")]
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO
		[SerializeField] private Image _bgMask;			// Frame/Middle/Bg_Mask
		[SerializeField] private Image _bgGradient;		// Frame/Middle/Bg_Mask/BgGradient

		[Header("펫")]
		[SerializeField] private Image _petImage;			// Frame/Middle/Frame/PetImage
		[SerializeField] private TMP_Text _nameText;		// Frame/Middle/Frame/NameText
		[SerializeField] private GameObject _equipStatText;	// Frame/Middle/Frame/EquipStatText
		[SerializeField] private UIButton _equipButton;		// Frame/Middle/Frame/EquipButton
		[SerializeField] private TMP_Text _equipButtonText;	// Frame/Middle/Frame/EquipButton/Text

		[Header("정보")]
		[SerializeField] private TMP_Text _levelText;	// Frame/Middle/Info/Group/Level
		[SerializeField] private TMP_Text _gradeText;	// Frame/Middle/Info/Group/Grade
		[SerializeField] private TMP_Text _bonusText;	// Frame/Middle/Info/BonusText
		[SerializeField] private TMP_Text _equipText;	// Frame/Middle/Info/EquipText

		[Header("강화")]
		[SerializeField] private UIButton _levelUpButton;	// Frame/Bottom/ButtonGrid/Button_LevelUp
		[SerializeField] private Image _levelUpIcon;		// .../Button_LevelUp/Group/Icon
		[SerializeField] private TMP_Text _levelUpCostText;	// .../Button_LevelUp/Group/CostText

		[Header("등급업")]
		[SerializeField] private UIButton _gradeUpButton;	// Frame/Bottom/ButtonGrid/Button_GradeUp
		[SerializeField] private Image _gradeUpIcon;		// .../Button_GradeUp/Group/Icon
		[SerializeField] private TMP_Text _gradeUpCostText;	// .../Button_GradeUp/Group/CostText

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;	// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;	// Dim

		public event Action OnEquipClicked;
		public event Action OnLevelUpClicked;
		public event Action OnGradeUpClicked;

		private readonly PetEnhancePopupPresenter _presenter = new PetEnhancePopupPresenter();

		private UniTaskCompletionSource _tcs;

		// 로드해 둔 펫 이미지 주소 (Acquire/Release 짝 맞춤용)
		private string _imageAddress;

		private void Awake()
		{
			_equipButton.OnClickEvent += onEquipClicked;
			_levelUpButton.OnClickEvent += onLevelUpClicked;
			_gradeUpButton.OnClickEvent += onGradeUpClicked;

			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dim 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			releaseImage();

			_equipButton.OnClickEvent -= onEquipClicked;
			_levelUpButton.OnClickEvent -= onLevelUpClicked;
			_gradeUpButton.OnClickEvent -= onGradeUpClicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			_presenter.Dispose();
		}

		// Presenter 가 등급 색을 정해야 해서 열어 둔다 (PetInfoUI 와 같은 자리).
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(EDT.Pet petId, CancellationToken ct)
		{
			await _presenter.ShowAsync(petId, ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void SetInfo(PetPopupData data)
		{
			_nameText.text = data.name;
			_levelText.text = data.levelText;
			_gradeText.text = data.gradeText;
			_bonusText.text = data.bonusText;
			_equipText.text = data.equipText;

			// BgDeco 는 프리팹 색(#FFFFFF)을 그대로 쓴다 — 슬롯도 등급색을 입히지 않는다.
			_bgMask.color = data.bgColor;
			_bgGradient.color = data.gradientColor;

			_equipStatText.SetActive(data.equipped);
			_equipButtonText.text = data.equipped ? "해제" : "장착";
		}

		public void SetLevelUpCost(PetCostData cost)
		{
			applyCost(cost, _levelUpButton, _levelUpIcon, _levelUpCostText);
		}

		public void SetGradeUpCost(PetCostData cost)
		{
			applyCost(cost, _gradeUpButton, _gradeUpIcon, _gradeUpCostText);
		}

		public UniTask SetPetImageAsync(string address, CancellationToken ct)
		{
			return setImage(address, ct);
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 한계에 닿으면 비용 자리를 한계 문구가 대신한다 — 재화 부족과 눈으로 구분되어야 한다.
		private void applyCost(PetCostData cost, UIButton button, Image icon, TMP_Text costText)
		{
			button.interactable = cost.available;

			icon.gameObject.SetActive(cost.hasCost);

			if (cost.hasCost == false)
			{
				costText.text = cost.limitText;
				costText.color = _costColor;
				return;
			}

			costText.text = cost.amountText;
			costText.color = cost.notEnough ? _lackColor : _costColor;

			setCostIcon(icon, cost.iconAddress);
		}

		// 비용 아이콘은 아틀라스 상주분이라 동기로 끝난다 — 참조카운트를 걸지 않는다.
		private static void setCostIcon(Image icon, string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			Sprite sprite = AtlasManager.Instance.Get(address);
			if (sprite != null)
			{
				icon.sprite = sprite;
			}
		}

		private async UniTask setImage(string address, CancellationToken ct)
		{
			if (_imageAddress == address)
			{
				return;
			}

			releaseImage();
			_imageAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_petImage.sprite = null;
				_petImage.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_petImage.sprite = atlasSprite;
				_petImage.enabled = true;
				_imageAddress = null;
				return;
			}

			_petImage.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			if (_imageAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_petImage.sprite = icon;
				_petImage.enabled = true;
			}
		}

		private void releaseImage()
		{
			if (string.IsNullOrEmpty(_imageAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_imageAddress);
			}

			_imageAddress = null;
		}

		private void onEquipClicked()
		{
			if (OnEquipClicked != null)
			{
				OnEquipClicked.Invoke();
			}
		}

		private void onLevelUpClicked()
		{
			if (OnLevelUpClicked != null)
			{
				OnLevelUpClicked.Invoke();
			}
		}

		private void onGradeUpClicked()
		{
			if (OnGradeUpClicked != null)
			{
				OnGradeUpClicked.Invoke();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
