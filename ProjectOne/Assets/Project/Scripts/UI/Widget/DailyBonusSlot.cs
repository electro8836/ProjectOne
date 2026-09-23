using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Resources;
using ProjectOne.Reward;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 출석 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct DailyBonusSlotData
	{
		public int dayCount;
		public int rewardGroupId;
		public bool isClaimed;		// 이미 받았는가 — Check
		public bool isFocused;		// 오늘의 칸인가 — Focus (받고 나서도 켜진 채로 남는다)
		public bool isClaimable;	// 지금 누르면 수령되는가 — Focus 와 별개다
	}

	// 출석 팝업의 한 칸(UIPrefab_DailyBonusSlot). 주간 7칸과 월간 30칸이 같은 프리펩을 쓴다.
	//
	// 보상은 한 칸만 그린다 — 출석 보상 그룹은 재화 1행으로 설계되어 있다(QuestSlot 과 같은 관례).
	//
	// NormalFrame / AdvancedFrame / ExtraFrame 과 NameText · TextBox 의 **활성 상태는 건드리지 않는다**.
	// 어느 칸이 특별한 칸인지는 프리펩 인스턴스에 이미 박혀 있는 연출이고, 코드는 글자만 채운다.
	public class DailyBonusSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;		// 루트
		[SerializeField] private Image _icon;			// Icon
		[SerializeField] private TMP_Text _countText;	// CountText
		[SerializeField] private TMP_Text _nameText;	// NameText
		[SerializeField] private TMP_Text _dayText;		// TextBox/DayText
		[SerializeField] private GameObject _check;		// Check
		[SerializeField] private GameObject _focus;		// Focus

		// 보상 미리보기 버퍼 — 칸마다 다시 담는다.
		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>(2);

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 지금 눌러서 받을 수 있는 칸인가. Focus 가 켜져 있는 칸과 같다.
		private bool _isClaimable;

		// 이 칸이 몇 일차인가. 수령 요청에 실어 보낸다.
		public int DayCount { get; private set; }

		// 받을 수 있는 칸을 눌렀다. 실제 수령 판단은 Presenter 가 한다.
		public event Action<DailyBonusSlot> OnClaimClicked;

		private void Awake()
		{
			if (_button != null)
			{
				_button.OnClickEvent += onClicked;
			}
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onClicked;
			}

			releaseIcon();
		}

		public UniTask BindAsync(in DailyBonusSlotData data, CancellationToken ct)
		{
			DayCount = data.dayCount;
			_isClaimable = data.isClaimable;

			if (_dayText != null)
			{
				_dayText.text = data.dayCount.ToString();
			}

			if (_check != null)
			{
				_check.SetActive(data.isClaimed);
			}

			if (_focus != null)
			{
				_focus.SetActive(data.isFocused);
			}

			_preview.Clear();
			RewardPreview.Build(data.rewardGroupId, _preview);

			if (_preview.Count == 0)
			{
				return clearRewardAsync(ct);
			}

			return bindRewardAsync(_preview[0], ct);
		}

		// ── 내부: 보상 ────────────────────────────────────────────────────

		private UniTask bindRewardAsync(in RewardPreviewItem reward, CancellationToken ct)
		{
			if (reward.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(reward.currency);

				applyCount(reward.count);
				applyName((currency != null) ? currency.Name : string.Empty);

				return setIconAsync((currency != null) ? currency.Icon : string.Empty, ct);
			}

			Table_Item.Row item = Table_Item.Get(reward.itemId);

			// 장비는 낱개라 수량이 의미 없다 — 1개로 본다.
			applyCount((reward.equipment != null) ? 1 : reward.count);
			applyName((item != null) ? item.Name : string.Empty);

			return setIconAsync((item != null) ? item.Icon : string.Empty, ct);
		}

		private UniTask clearRewardAsync(CancellationToken ct)
		{
			applyCount(1);
			applyName(string.Empty);

			return setIconAsync(string.Empty, ct);
		}

		// 1개면 숫자를 적지 않는다. 재화는 자릿수가 커서 CurrencySlot 과 같은 축약 표기를 쓴다.
		private void applyCount(int count)
		{
			if (_countText == null)
			{
				return;
			}

			if (count <= 1)
			{
				_countText.gameObject.SetActive(false);
				return;
			}

			_countText.gameObject.SetActive(true);
			_countText.text = CurrencySlot.FormatAmount(count);
		}

		// 이름칸은 특별한 칸에만 켜져 있다 — 켜고 끄지 않고 글자만 채운다.
		private void applyName(string value)
		{
			if (_nameText == null)
			{
				return;
			}

			_nameText.text = value;
		}

		// ── 내부: 아이콘 ──────────────────────────────────────────────────

		// ItemSlot.setIcon 과 같은 규약이다 — 아틀라스에 있으면 동기로 즉시, 없으면 참조카운트 로드.
		private async UniTask setIconAsync(string address, CancellationToken ct)
		{
			if (_icon == null)
			{
				return;
			}

			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				_icon.sprite = null;
				_icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 참조카운트 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
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
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (!string.IsNullOrEmpty(_iconAddress) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		// ── 내부: 클릭 ────────────────────────────────────────────────────

		// 받을 수 있는 칸이면 수령, 아니면 "이게 무엇인가" 를 보여주는 읽기 전용 팝업.
		private void onClicked()
		{
			if (_isClaimable == true)
			{
				if (OnClaimClicked != null)
				{
					OnClaimClicked.Invoke(this);
				}

				return;
			}

			if (_preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(_preview[0], transform as RectTransform, this.GetCancellationTokenOnDestroy());
		}
	}
}
