using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 장비 그리드의 슬롯 1칸. 등급 색상 + 아이콘 + 레벨/개수 + 미보유(Unlock)/장착(Focus)을 표시한다.
	public class EquipmentSlot : MonoBehaviour
	{
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

		public async UniTask Bind(Table_Equipment.Row row, bool owned, int count, int enhanceLevel, bool equipped, GradeColorTable colors, CancellationToken ct)
		{
			GradeColorTable.GradeColor gc = colors.Get(row.Grade);
			_bgMask.color = gc.bgMask;
			_gradient.color = gc.gradient;
			_glow.color = gc.glow;

			_unlock.SetActive(!owned);
			_focus.SetActive(equipped);

			if (owned)
			{
				_levelText.gameObject.SetActive(true);
				_countText.gameObject.SetActive(true);
				_levelText.text = "Lv." + (enhanceLevel + 1);
				_countText.text = count.ToString();
			}
			else
			{
				_levelText.gameObject.SetActive(false);
				_countText.gameObject.SetActive(false);
			}

			await setIcon(row.Icon, ct);
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
				return;
			}

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
			}
		}

		private void releaseIcon()
		{
			if (!string.IsNullOrEmpty(_iconAddress))
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
