using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Items;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 아이템 목록의 슬롯 1칸(Prefab_ItemSlot). 등급 색상 + 아이콘을 공통으로 표시하고,
	// 장비면 강화 레벨과 품질바를, 스택 아이템(소모품)이면 보유 개수를 표시한다.
	public class ItemSlot : MonoBehaviour
	{
		[Header("클릭")]
		[SerializeField] private UIButton _button;	// 슬롯 클릭 입력

		[Header("등급 색상 대상")]
		[SerializeField] private Image _bgMask;		// Bg_Mask
		[SerializeField] private Image _border;		// Border

		[Header("내용")]
		[SerializeField] private Image _itemIcon;			// ItemIcon
		[SerializeField] private TMP_Text _levelText;		// LevelText — 장비 전용
		[SerializeField] private TMP_Text _countText;		// CountText — 스택 아이템 전용
		[SerializeField] private TMP_Text _equipText;		// EquipText — 장착중 표시
		[SerializeField] private Slider _qualitySlider;	// QualitySlider — 장비 전용

		// 장비 품질(quality)의 최대치. 옵션 계산이 quality/100 을 쓰므로 여기서도 같은 축을 쓴다.
		private const float QualityMax = 100f;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 클릭 시 전달할 식별자. uid 는 장비 인스턴스, itemId 는 테이블 행.
		// 스택 아이템은 인스턴스가 없어 uid 가 0 이다 — 구독자는 이것으로 둘을 구분한다.
		private long _uid;
		private int _itemId;
		public event Action<long, int> OnClicked;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		// 장비 인스턴스 바인딩 — 등급·레벨·품질은 테이블이 아니라 인스턴스가 소유한다.
		// 장착 여부는 인스턴스가 아니라 Loadout 이 정하므로 호출자가 알려준다.
		public async UniTask BindEquipmentAsync(EquipmentInstance instance, bool equipped, ItemGradeColorTable colors, CancellationToken ct)
		{
			_uid = instance.uid;
			_itemId = instance.itemId;

			applyGradeColor(colors, instance.grade);
			SetEquipped(equipped);

			_levelText.gameObject.SetActive(true);
			_levelText.text = "Lv." + instance.level;

			_countText.gameObject.SetActive(false);

			if (_qualitySlider != null)
			{
				_qualitySlider.gameObject.SetActive(true);
				_qualitySlider.value = instance.quality / QualityMax;
			}

			Table_Item.Row row = instance.Item;
			await setIcon((row != null) ? row.Icon : string.Empty, ct);
		}

		// 스택 아이템(소모품) 바인딩 — 인스턴스가 없으므로 등급은 테이블 값을 쓴다.
		public async UniTask BindItemAsync(Table_Item.Row row, int count, ItemGradeColorTable colors, CancellationToken ct)
		{
			_uid = 0;
			_itemId = (row != null) ? row.ID : 0;

			applyGradeColor(colors, (row != null) ? row.Grade : ItemGradeType.None);

			// 스택 아이템은 장착 개념이 없다. 프리펩 기본값이 활성이라 끄지 않으면 소모품에도 "장착중" 이 뜬다.
			SetEquipped(false);

			_levelText.gameObject.SetActive(false);

			_countText.gameObject.SetActive(true);
			_countText.text = count.ToString();

			if (_qualitySlider != null)
			{
				_qualitySlider.gameObject.SetActive(false);
			}

			await setIcon((row != null) ? row.Icon : string.Empty, ct);
		}

		// 장착중 표시를 켜고 끈다. Bind 없이 상태만 뒤집을 때도 쓴다(팝업의 장착/해제 토글).
		public void SetEquipped(bool equipped)
		{
			if (_equipText != null)
			{
				_equipText.gameObject.SetActive(equipped);
			}
		}

		// 강화 레벨 표시를 끈다. 아이템 정보 팝업의 슬롯처럼 레벨을 따로 적는 화면에서 쓴다
		// (BindEquipmentAsync 가 매번 켜므로 Bind 뒤에 호출해야 한다).
		public void HideLevel()
		{
			_levelText.gameObject.SetActive(false);
		}

		private void applyGradeColor(ItemGradeColorTable colors, ItemGradeType grade)
		{
			if (colors == null)
			{
				return;
			}

			ItemGradeColorTable.GradeColor gc = colors.Get(grade);
			_bgMask.color = gc.bg;
			_border.color = gc.border;
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_uid, _itemId);
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		// 호출자가 await 하므로 로드 완료 시점에 아이콘이 채워진다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				_itemIcon.sprite = null;
				_itemIcon.enabled = false;
				return;
			}

			// 아틀라스에 있으면 await 없이 동기로 즉시 세팅 → 슬롯 생성과 같은 프레임에 표시(한 템포 늦음 방지).
			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_itemIcon.sprite = atlasSprite;
				_itemIcon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 아이콘을 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_itemIcon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 슬롯이 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_itemIcon.sprite = icon;
				_itemIcon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (!string.IsNullOrEmpty(_iconAddress) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
			releaseIcon();
		}
	}
}
