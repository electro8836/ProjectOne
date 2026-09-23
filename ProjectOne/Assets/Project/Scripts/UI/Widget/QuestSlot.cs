using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Reward;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 퀘스트 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct QuestSlotData
	{
		public int questId;
		public string name;
		public string desc;
		public bool isCurrent;		// 지금 진행 중인 퀘스트인가 — 틀 강조
		public bool isCleared;		// 이미 깨서 보상을 받았는가 — Clear / Check 표시
		public int rewardGroupId;
	}

	// 퀘스트 목록 팝업의 한 칸(UIPrefab_QuestSlot).
	//
	// 슬롯 자체는 클릭을 받지 않는다. 목록은 정보를 보는 곳이라 수령 경로가 없다 —
	// 보상 칸을 누르면 그 보상이 무엇인지 보여주는 읽기 전용 팝업만 열린다(수령은 QuestInfo 의 몫).
	// 보상 칸은 ItemPoolSlot 과 같은 방식으로 ItemSlotRoot 에 ItemSlot 하나를 꽂아 쓴다
	// — 퀘스트 보상은 항상 한 칸이다.
	public class QuestSlot : MonoBehaviour
	{
		// 진행 중인 퀘스트의 틀 색 — QuestInfo 의 달성 강조색과 같은 값.
		private static readonly Color CurrentBorderColor = new Color(0.0078431f, 1f, 0f, 1f);	// #02FF00

		// 진행 중이 아닌 퀘스트의 틀 색.
		private static readonly Color InactiveBorderColor = new Color32(0x89, 0x89, 0x89, 0xFF);

		// 진행 중인 퀘스트의 칸 바탕색.
		private static readonly Color CurrentFrameColor = new Color32(0x49, 0x5F, 0xB2, 0xFF);

		// 진행 중이 아닌 퀘스트의 칸 바탕색 — 프리펩 기본값.
		private static readonly Color InactiveFrameColor = new Color32(0x2F, 0x34, 0x48, 0xFF);

		[SerializeField] private Image _frame;					// Frame
		[SerializeField] private Image _innerBorder;			// Frame/InnerBorder
		[SerializeField] private TMP_Text _numberText;			// NumberText
		[SerializeField] private TMP_Text _nameText;			// NameText
		[SerializeField] private TMP_Text _descText;			// DescText
		[SerializeField] private GameObject _clear;				// Clear — 클리어한 칸을 덮는 오버레이
		[SerializeField] private RectTransform _itemSlotRoot;	// Reward/ItemSlotRoot
		[SerializeField] private GameObject _check;				// Reward/Check

		// 보상 미리보기 버퍼 — 퀘스트마다 다시 담는다.
		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>(2);

		private ItemSlot _itemSlot;

		private void OnDestroy()
		{
			if (_itemSlot != null)
			{
				_itemSlot.OnClicked -= onItemSlotClicked;
			}
		}

		public UniTask BindAsync(in QuestSlotData data, ItemSlot slotPrefab, ItemGradeColorTable colors, CancellationToken ct)
		{
			if (_frame != null)
			{
				_frame.color = data.isCurrent ? CurrentFrameColor : InactiveFrameColor;
			}

			if (_innerBorder != null)
			{
				_innerBorder.color = data.isCurrent ? CurrentBorderColor : InactiveBorderColor;
			}

			if (_numberText != null)
			{
				_numberText.text = data.questId.ToString();
			}

			if (_nameText != null)
			{
				_nameText.text = data.name;
			}

			if (_descText != null)
			{
				_descText.text = data.desc;
			}

			if (_clear != null)
			{
				_clear.SetActive(data.isCleared);
			}

			if (_check != null)
			{
				_check.SetActive(data.isCleared);
			}

			return bindRewardAsync(data, slotPrefab, colors, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 보상은 한 칸만 그린다 — 퀘스트 보상 그룹은 재화 1행으로 설계되어 있다.
		private UniTask bindRewardAsync(in QuestSlotData data, ItemSlot slotPrefab, ItemGradeColorTable colors, CancellationToken ct)
		{
			_preview.Clear();
			RewardPreview.Build(data.rewardGroupId, _preview);

			if (_preview.Count == 0)
			{
				hideItemSlot();
				return UniTask.CompletedTask;
			}

			if (_itemSlot == null)
			{
				if (slotPrefab == null || _itemSlotRoot == null)
				{
					return UniTask.CompletedTask;
				}

				_itemSlot = Instantiate(slotPrefab, _itemSlotRoot);
				_itemSlot.StretchToParent();

				// 슬롯은 한 번만 만들어 재사용하므로 구독도 여기서 한 번이면 된다.
				_itemSlot.OnClicked += onItemSlotClicked;
			}

			_itemSlot.gameObject.SetActive(true);

			RewardPreviewItem reward = _preview[0];
			if (reward.type == EDT.RewardType.Currency)
			{
				return _itemSlot.BindCurrencyAsync(reward.currency, reward.count, colors, ct);
			}

			if (reward.equipment != null)
			{
				return _itemSlot.BindEquipmentAsync(reward.equipment, false, colors, ct);
			}

			EDT.Table_Item.Row row = EDT.Table_Item.Get(reward.itemId);
			if (row == null)
			{
				hideItemSlot();
				return UniTask.CompletedTask;
			}

			return _itemSlot.BindItemAsync(row, reward.count, colors, ct);
		}

		// 보상 칸 클릭 = 이 보상이 무엇인지 보여주기. 지금 그린 보상 하나를 그대로 넘긴다.
		private void onItemSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			if (_preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(_preview[0], sender, this.GetCancellationTokenOnDestroy());
		}

		private void hideItemSlot()
		{
			if (_itemSlot != null)
			{
				_itemSlot.gameObject.SetActive(false);
			}
		}
	}
}
