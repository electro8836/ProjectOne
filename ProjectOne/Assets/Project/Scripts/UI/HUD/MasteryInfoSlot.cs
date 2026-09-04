using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 전체 마스터리 목록의 슬롯 1칸(UIPrefab_MastertyInfoSlot).
	// 마스터리 1종의 아이콘·이름·레벨·레벨 보너스를 표시하고, 착용 중인 무기의 마스터리는 색으로 강조한다.
	//
	// 클릭 입력이 없다 — 이 목록은 보기 전용이라 버튼도 이벤트도 두지 않는다.
	public class MasteryInfoSlot : MonoBehaviour
	{
		[Header("강조 대상")]
		[SerializeField] private Image _bg;			// Frame/Bg
		[SerializeField] private Image _innerBorder;	// Frame/InnerBorder

		[Header("내용")]
		[SerializeField] private Image _masteryIcon;	// MasteryIcon
		[SerializeField] private TMP_Text _masteryName;	// MasteryName
		[SerializeField] private TMP_Text _masteryLevel;	// MasteryLevel
		[SerializeField] private TMP_Text _masteryBonus;	// MasteryBonus
		[SerializeField] private Slider _levelSlider;	// LevelInfo
		[SerializeField] private TMP_Text _levelText;	// LevelInfo/LevelText

		// 활성/비활성 색. 두 쌍뿐이고 기획이 값을 지정했으므로 인스펙터에 열지 않는다.
		private static readonly Color ActiveBg = new Color32(0x7D, 0x57, 0xF2, 0xFF);
		private static readonly Color InactiveBg = new Color32(0x3E, 0x34, 0x5C, 0xFF);
		private static readonly Color ActiveBorder = new Color32(0xAA, 0x76, 0xF7, 0xFF);
		private static readonly Color InactiveBorder = new Color32(0x4C, 0x3F, 0x70, 0xFF);

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		public UniTask BindAsync(MasteryInfoSlotData data, CancellationToken ct)
		{
			_bg.color = data.isActive ? ActiveBg : InactiveBg;
			_innerBorder.color = data.isActive ? ActiveBorder : InactiveBorder;

			_masteryName.text = data.name;
			_masteryLevel.text = $"레벨 {data.level}";
			_masteryBonus.text = data.bonusText;

			_levelSlider.value = data.maxLevel > 0 ? (float)data.level / data.maxLevel : 0f;
			_levelText.text = $"{data.level}/{data.maxLevel}";

			return setIcon(data.iconAddress, ct);
		}

		private void OnDestroy()
		{
			releaseIcon();
		}

		// ── 내부: 아이콘 ───────────────────────────────────────────────────

		// ItemSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
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
				_masteryIcon.sprite = null;
				_masteryIcon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_masteryIcon.sprite = atlasSprite;
				_masteryIcon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_masteryIcon.enabled = false;

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
				_masteryIcon.sprite = icon;
				_masteryIcon.enabled = true;
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
