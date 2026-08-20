using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 던전 결과창 보상 슬롯 1칸(Prefab_RewardSlot).
	// RewardItem 아이콘/수량을 표시하고, 추가(엑스트라) 스테이지 보상이면 Bonus 오브젝트를 켠다.
	// (슬롯 클릭 팝업은 프리팹에 버튼만 두고 이후 작업.)
	public class RewardSlot : MonoBehaviour
	{
		[SerializeField] private Image _icon;
		[SerializeField] private TMP_Text _countText;
		[SerializeField] private GameObject _bonus;

		[Header("등급 색상 대상")]
		[SerializeField] private Image _bgMask;
		[SerializeField] private Image _gradient;
		[SerializeField] private Image _glow;

		// 참조카운트 해제용 아이콘 주소 추적 (아틀라스 스프라이트는 null 로 두어 대상 제외)
		private string _iconAddress;

		// 아이템 모드 — 서버가 확정한 실제 획득을 등급 색상과 함께 표시한다(던전 결과창용).
		// 색상 테이블은 호출자(DungeonResultUI)가 주입한다.
		public async UniTask BindItemAsync(int rewardType, int itemId, int count, bool isBonus,
			GradeColorTable gradeColors, CancellationToken ct)
		{
			if (_countText != null)
			{
				_countText.text = count.ToString();
			}

			if (_bonus != null)
			{
				_bonus.SetActive(isBonus);
			}

			string iconAddress = string.Empty;
			switch ((RewardType)rewardType)
			{
			case RewardType.Item:
			case RewardType.ItemPool:
			{
				// 장비·재료·소모품이 Item 테이블 하나로 통합되어 등급 축도 Item.Grade 하나뿐이다.
				Table_Item.Row row = Table_Item.Get(itemId);
				if (row != null)
				{
					applyGradeColors(gradeColors != null ? gradeColors.Get(row.Grade) : null);
					iconAddress = row.Icon;
				}

				break;
			}
			case RewardType.Currency:
			{
				// 재화는 등급이 없어 색상은 프리팹 기본값 그대로 두고 아이콘/수량만 표시한다.
				Table_Currency.Row row = Table_Currency.Get((EDT.Currency)itemId);
				if (row != null)
				{
					iconAddress = row.Icon;
				}

				break;
			}
			}

			await setIconAsync(iconAddress, ct);
		}

		// 등급 색상 — Bg_Mask/Gradient/Glow 3개에 색을 적용한다(모두 활성).
		private void applyGradeColors(GradeColorTable.GradeColor gc)
		{
			if (gc == null)
			{
				return;
			}

			setColor(_bgMask, gc.bgMask, true);
			setColor(_gradient, gc.gradient, true);
			setColor(_glow, gc.glow, true);
		}

		private static void setColor(Image target, Color color, bool active)
		{
			if (target == null)
			{
				return;
			}

			target.gameObject.SetActive(active);
			target.color = color;
		}

		// 아틀라스 우선(동기), 미포함 시 비동기 로드. FieldSelectSlot/EquipmentSlot 패턴.
		private async UniTask setIconAsync(string address, CancellationToken ct)
		{
			if (_icon == null || _iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_iconAddress = null;
				_icon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;   // 아틀라스 스프라이트는 refcount 대상 아님
				return;
			}

			_icon.enabled = false;
			(bool cancelled, Sprite icon) = await ResourceManager.Instance
				.AcquireAsync<Sprite>(address, ct)
				.SuppressCancellationThrow();

			if (cancelled == true || _iconAddress != address)
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
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		private void OnDestroy()
		{
			releaseIcon();
		}
	}
}
