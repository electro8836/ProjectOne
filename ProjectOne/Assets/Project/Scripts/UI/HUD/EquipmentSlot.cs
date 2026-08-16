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
	// 장비 그리드의 슬롯 1칸. 등급 색상 + 아이콘 + 레벨/개수 + 미보유(Unlock)/장착(Focus)을 표시한다.
	public class EquipmentSlot : MonoBehaviour
	{
		[Header("클릭")]
		[SerializeField] private UIButton _button;	// 슬롯 클릭 입력

		[Header("등급 색상 대상")]
		[SerializeField] private Image _bgMask;
		[SerializeField] private Image _gradient;
		[SerializeField] private Image _glow;

		[Header("내용")]
		[SerializeField] private Image _itemIcon;
		[SerializeField] private TMP_Text _levelText;
		[SerializeField] private TMP_Text _countText;

		[Header("상태 오브젝트")]
		[SerializeField] private GameObject _unlock;	// 미보유 표시
		[SerializeField] private GameObject _focus;		// 장착 표시

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 클릭 시 전달할 장비 인스턴스 UID와 구독 이벤트
		private long _uid;
		public event Action<long> OnClicked;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		// 등급·레벨·순도는 아이템 테이블이 아니라 인스턴스가 소유한다.
		// 그리드는 보유 인스턴스만 나열하므로 미보유(Unlock) 표시는 쓰지 않는다.
		public async UniTask Bind(EquipmentInstance instance, bool equipped, GradeColorTable colors, CancellationToken ct)
		{
			_uid = instance.uid;

			Table_Item.Row row = instance.Item;

			GradeColorTable.GradeColor gc = colors.Get(instance.grade);
			_bgMask.color = gc.bgMask;
			_gradient.color = gc.gradient;
			_glow.color = gc.glow;

			_unlock.SetActive(false);
			_focus.SetActive(equipped);

			_levelText.gameObject.SetActive(true);
			_levelText.text = "Lv." + instance.level;

			// 장비는 스택이 아니므로 개수 자리에 순도 등급을 표기한다 (아이템 설계 8장).
			Table_EquipPurity.Row purity = Table_EquipPurity.Get(instance.purity);
			_countText.gameObject.SetActive(purity != null);
			if (purity != null)
			{
				_countText.text = purity.DisplayName;
			}

			await setIcon((row != null) ? row.Icon : string.Empty, ct);
		}

		// 미보유(Unlock)·장착(Focus) 표시를 숨긴다 (아이템 정보 팝업의 슬롯용).
		public void HideStatusObjects()
		{
			_unlock.SetActive(false);
			_focus.SetActive(false);
		}

		// 아이템 수량 표시를 숨긴다 (장착 슬롯용 — 수량 불필요).
		public void HideCount()
		{
			_countText.gameObject.SetActive(false);
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_uid);
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
