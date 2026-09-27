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
	// 히어로패스 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct HeroPassRewardSlotData
	{
		public int level;
		public float sliderValue;			// 미도달 0 / 현재 레벨 0.5 / 지난 레벨 1
		public bool isLocked;				// 패스 미구매 — Reward_Pass 의 Lock

		public int normalGroupId;
		public bool normalClaimed;
		public bool normalClaimable;

		public int advancedGroupId;
		public bool advancedClaimed;
		public bool advancedClaimable;
	}

	// 히어로패스 팝업의 한 줄(UIPrefab_PassRewardSlot). 왼쪽이 일반 보상, 오른쪽이 추가(패스) 보상이다.
	//
	// 보상은 칸마다 한 개만 그린다 — 패스 보상 그룹은 재화 1행으로 설계되어 있다(DailyBonusSlot 과 같은 관례).
	public class HeroPassRewardSlot : MonoBehaviour
	{
		// 보상 칸 하나(Reward_Normal / Reward_Pass). 두 칸이 같은 구성이라 묶어 둔다.
		[Serializable]
		private sealed class RewardCell
		{
			public UIButton button;
			public Image icon;
			public TMP_Text countText;
			public GameObject check;

			// 칸마다 다시 담는 보상 미리보기 버퍼 — 역직렬화가 필드 초기화를 보장하지 않아 Awake 에서 만든다.
			[NonSerialized] public List<RewardPreviewItem> preview;

			// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
			[NonSerialized] public string iconAddress;

			[NonSerialized] public bool isClaimable;
		}

		[SerializeField] private TMP_Text _levelText;		// Level/LevelText
		[SerializeField] private Slider _slider;			// Slider
		[SerializeField] private RewardCell _normal;		// Reward_Normal
		[SerializeField] private RewardCell _advanced;		// Reward_Pass
		[SerializeField] private GameObject _lock;			// Reward_Pass/Lock

		// 이 칸이 몇 레벨인가. 수령 요청에 실어 보낸다.
		public int Level { get; private set; }

		// 받을 수 있는 칸을 눌렀다(bool = 추가 보상인가). 실제 수령 판단은 Presenter 가 한다.
		public event Action<HeroPassRewardSlot, bool> OnClaimClicked;

		private void Awake()
		{
			_normal.preview = new List<RewardPreviewItem>(2);
			_advanced.preview = new List<RewardPreviewItem>(2);

			if (_normal.button != null)
			{
				_normal.button.OnClickEvent += onNormalClicked;
			}

			if (_advanced.button != null)
			{
				_advanced.button.OnClickEvent += onAdvancedClicked;
			}
		}

		private void OnDestroy()
		{
			if (_normal.button != null)
			{
				_normal.button.OnClickEvent -= onNormalClicked;
			}

			if (_advanced.button != null)
			{
				_advanced.button.OnClickEvent -= onAdvancedClicked;
			}

			releaseIcon(_normal);
			releaseIcon(_advanced);
		}

		public UniTask BindAsync(in HeroPassRewardSlotData data, CancellationToken ct)
		{
			Level = data.level;

			if (_levelText != null)
			{
				_levelText.text = data.level.ToString();
			}

			if (_slider != null)
			{
				_slider.value = data.sliderValue;
			}

			if (_lock != null)
			{
				_lock.SetActive(data.isLocked);
			}

			UniTask normalTask = bindCellAsync(_normal, data.normalGroupId, data.normalClaimed, data.normalClaimable, ct);
			UniTask advancedTask = bindCellAsync(_advanced, data.advancedGroupId, data.advancedClaimed, data.advancedClaimable, ct);

			return UniTask.WhenAll(normalTask, advancedTask);
		}

		// ── 내부: 보상 ────────────────────────────────────────────────────

		private UniTask bindCellAsync(RewardCell cell, int groupId, bool claimed, bool claimable, CancellationToken ct)
		{
			cell.isClaimable = claimable;

			if (cell.check != null)
			{
				cell.check.SetActive(claimed);
			}

			cell.preview.Clear();
			RewardPreview.Build(groupId, cell.preview);

			if (cell.preview.Count == 0)
			{
				applyCount(cell, 0);
				return setIconAsync(cell, string.Empty, ct);
			}

			RewardPreviewItem reward = cell.preview[0];
			if (reward.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(reward.currency);

				applyCount(cell, reward.count);
				return setIconAsync(cell, (currency != null) ? currency.Icon : string.Empty, ct);
			}

			Table_Item.Row item = Table_Item.Get(reward.itemId);

			// 장비는 낱개라 수량이 의미 없다 — 1개로 본다.
			applyCount(cell, (reward.equipment != null) ? 1 : reward.count);
			return setIconAsync(cell, (item != null) ? item.Icon : string.Empty, ct);
		}

		// 재화는 자릿수가 커서 CurrencySlot 과 같은 축약 표기를 쓴다.
		private static void applyCount(RewardCell cell, int count)
		{
			if (cell.countText == null)
			{
				return;
			}

			cell.countText.text = (count > 0) ? CurrencySlot.FormatAmount(count) : string.Empty;
		}

		// ── 내부: 아이콘 ──────────────────────────────────────────────────

		// DailyBonusSlot.setIconAsync 와 같은 규약이다 — 아틀라스에 있으면 동기로 즉시, 없으면 참조카운트 로드.
		private static async UniTask setIconAsync(RewardCell cell, string address, CancellationToken ct)
		{
			if (cell.icon == null)
			{
				return;
			}

			if (cell.iconAddress == address)
			{
				return;
			}

			releaseIcon(cell);
			cell.iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				cell.icon.sprite = null;
				cell.icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 참조카운트 대상이 아니므로 iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				cell.icon.sprite = atlasSprite;
				cell.icon.enabled = true;
				cell.iconAddress = null;
				return;
			}

			// 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			cell.icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (cell.iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				cell.icon.sprite = icon;
				cell.icon.enabled = true;
			}
		}

		private static void releaseIcon(RewardCell cell)
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (!string.IsNullOrEmpty(cell.iconAddress) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(cell.iconAddress);
				cell.iconAddress = null;
			}
		}

		// ── 내부: 클릭 ────────────────────────────────────────────────────

		private void onNormalClicked()
		{
			onCellClicked(_normal, false);
		}

		private void onAdvancedClicked()
		{
			onCellClicked(_advanced, true);
		}

		// 받을 수 있는 칸이면 수령, 아니면 "이게 무엇인가" 를 보여주는 읽기 전용 팝업.
		private void onCellClicked(RewardCell cell, bool advanced)
		{
			if (cell.isClaimable == true)
			{
				if (OnClaimClicked != null)
				{
					OnClaimClicked.Invoke(this, advanced);
				}

				return;
			}

			if (cell.preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(cell.preview[0], cell.button.transform as RectTransform, this.GetCancellationTokenOnDestroy());
		}
	}
}
