using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 스테이지 선택 화면(다음 스테이지 선택 + 엑스트라 도전/마을 귀환 통합).
	// DungeonDirector 가 OpenOverlayAsync 로 열고, 모드에 맞는 Wait...Async 로 결과를 기다린다.
	public class StageSelectUI : UIScreen
	{
		// 엑스트라 화면 선택 결과 — 닫기/도전/귀환
		public enum ExtraChoice
		{
			Exit,
			Challenge,
			Return,
		}

		// 스테이지 후보 1개 — stageId + 배정된 롤 보상 RewardItemId
		public readonly struct StageOption
		{
			public readonly int StageId;
			public readonly int RollRewardItemId;

			public StageOption(int stageId, int rollRewardItemId)
			{
				this.StageId = stageId;
				this.RollRewardItemId = rollRewardItemId;
			}
		}

		[Header("공통")]
		[SerializeField] private StageModeVisualData _modeVisuals;
		[SerializeField] private TMP_Text _subTitleText;
		[SerializeField] private UIButton _enterButton;
		[SerializeField] private UIButton _returnButton;
		[SerializeField] private UIButton _exitButton;

		[Header("스테이지 선택")]
		[SerializeField] private GameObject _stageList;
		[SerializeField] private StageSelectSlot[] _slots;   // StageInfo_1, StageInfo_2

		[Header("엑스트라")]
		[SerializeField] private GameObject _extraStageInfo;
		[SerializeField] private Image _extraModeIcon;
		[SerializeField] private TMP_Text _extraModeNameText;
		[SerializeField] private TMP_Text _extraModeRewardText;

		private UniTaskCompletionSource<int> _selectionSource;
		private UniTaskCompletionSource<ExtraChoice> _choiceSource;
		private List<StageOption> _options;
		private int _optionCount;
		private int _selectedIndex = -1;
		private bool _extraMode;
		private string _extraIconAddress;

		private void Awake()
		{
			_enterButton.OnClickEvent += onEnterClicked;
			_returnButton.OnClickEvent += onReturnClicked;
			_exitButton.OnClickEvent += onExitClicked;

			if (_slots != null && _slots.Length > 0 && _slots[0] != null)
			{
				_slots[0].Button.OnClickEvent += onSlot0Clicked;
			}

			if (_slots != null && _slots.Length > 1 && _slots[1] != null)
			{
				_slots[1].Button.OnClickEvent += onSlot1Clicked;
			}
		}

		private void OnDestroy()
		{
			_enterButton.OnClickEvent -= onEnterClicked;
			_returnButton.OnClickEvent -= onReturnClicked;
			_exitButton.OnClickEvent -= onExitClicked;

			if (_slots != null && _slots.Length > 0 && _slots[0] != null)
			{
				_slots[0].Button.OnClickEvent -= onSlot0Clicked;
			}

			if (_slots != null && _slots.Length > 1 && _slots[1] != null)
			{
				_slots[1].Button.OnClickEvent -= onSlot1Clicked;
			}

			releaseExtraIcon();
			_selectionSource?.TrySetCanceled();
			_choiceSource?.TrySetCanceled();
		}

		// 다음 스테이지 선택. 선택된 StageID 반환.
		public async UniTask<int> WaitStageSelectionAsync(List<StageOption> options, int stageNumber, int totalStages, CancellationToken ct)
		{
			_extraMode = false;
			_options = options;
			_optionCount = options != null ? options.Count : 0;

			_stageList.SetActive(true);
			_extraStageInfo.SetActive(false);
			_returnButton.gameObject.SetActive(false);

			_subTitleText.text = "다음 스테이지를 선택해주세요\n(" + stageNumber + "/" + totalStages + ")";

			CancellationToken iconCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < _slots.Length; i++)
			{
				StageSelectSlot slot = _slots[i];
				if (slot == null)
				{
					continue;
				}

				if (i < _optionCount)
				{
					slot.gameObject.SetActive(true);
					slot.SetSelected(false);

					StageOption option = options[i];
					MapModeType mode = stageMode(option.StageId);
					slot.SetTexts(modeName(mode), buildRewardText(new int[] { option.RollRewardItemId }));
					slot.SetIconAsync(iconAddress(mode), iconCt).Forget();
				}
				else
				{
					slot.gameObject.SetActive(false);
				}
			}

			_selectedIndex = -1;
			_enterButton.interactable = false;

			_selectionSource = new UniTaskCompletionSource<int>();
			using (ct.Register(onCanceled))
			{
				return await _selectionSource.Task;
			}
		}

		// 엑스트라 도전 / 마을 귀환 / 닫기 선택.
		public async UniTask<ExtraChoice> WaitExtraChoiceAsync(MapModeType extraMode, int[] fixRewardItemIds, CancellationToken ct)
		{
			_extraMode = true;

			_stageList.SetActive(false);
			_extraStageInfo.SetActive(true);
			_returnButton.gameObject.SetActive(true);

			_subTitleText.text = "최종 스테이지에 도전하거나 마을로 귀환하세요";

			if (_extraModeNameText != null)
			{
				_extraModeNameText.text = modeName(extraMode);
			}

			if (_extraModeRewardText != null)
			{
				_extraModeRewardText.text = buildRewardText(fixRewardItemIds);
			}

			setExtraIconAsync(iconAddress(extraMode), this.GetCancellationTokenOnDestroy()).Forget();

			_enterButton.interactable = true;

			_choiceSource = new UniTaskCompletionSource<ExtraChoice>();
			using (ct.Register(onCanceled))
			{
				return await _choiceSource.Task;
			}
		}

		private void onSlot0Clicked()
		{
			select(0);
		}

		private void onSlot1Clicked()
		{
			select(1);
		}

		private void select(int index)
		{
			if (index < 0 || index >= _optionCount)
			{
				return;
			}

			_selectedIndex = index;
			for (int i = 0; i < _slots.Length; i++)
			{
				if (_slots[i] != null)
				{
					_slots[i].SetSelected(i == index);
				}
			}

			_enterButton.interactable = true;
		}

		private void onEnterClicked()
		{
			if (_extraMode == true)
			{
				_choiceSource?.TrySetResult(ExtraChoice.Challenge);
				return;
			}

			if (_selectedIndex < 0 || _selectedIndex >= _optionCount)
			{
				return;
			}

			_selectionSource?.TrySetResult(_options[_selectedIndex].StageId);
		}

		private void onReturnClicked()
		{
			_choiceSource?.TrySetResult(ExtraChoice.Return);
		}

		// 닫기 — 스테이지 선택은 0(미선택), 엑스트라는 Exit 를 반환해 호출자가 포탈 버튼을 다시 노출한다.
		private void onExitClicked()
		{
			if (_extraMode == true)
			{
				_choiceSource?.TrySetResult(ExtraChoice.Exit);
				return;
			}

			_selectionSource?.TrySetResult(0);
		}

		private void onCanceled()
		{
			_selectionSource?.TrySetCanceled();
			_choiceSource?.TrySetCanceled();
		}

		// 스테이지 모드 조회
		private static MapModeType stageMode(int stageId)
		{
			Table_Stage.Row row = Table_Stage.Get(stageId);
			return row != null ? row.ModeType : MapModeType.None;
		}

		// TODO(향후 모드명 유틸로 교체) — 임시로 enum 이름 표기
		private static string modeName(MapModeType mode)
		{
			switch (mode)
			{
				case MapModeType.Defense:			return "디펜스";
				case MapModeType.BuildingDestroy:	return "파괴";
				case MapModeType.Capture:			return "점령";
				case MapModeType.Breakthrough:		return "돌파";
				case MapModeType.BossBattle:		return "보스";
			}

			return mode.ToString();
		}

		private string iconAddress(MapModeType mode)
		{
			return _modeVisuals != null ? _modeVisuals.GetIconAddress(mode) : string.Empty;
		}

		// "보상" + 각 RewardItem 이름을 줄바꿈으로
		private static string buildRewardText(int[] rewardItemIds)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("보상");
			if (rewardItemIds != null)
			{
				for (int i = 0; i < rewardItemIds.Length; i++)
				{
					Table_RewardItem.Row item = Table_RewardItem.Get(rewardItemIds[i]);
					if (item != null)
					{
						sb.Append('\n');
						sb.Append(item.Name);
					}
				}
			}

			return sb.ToString();
		}

		// 엑스트라 모드 아이콘 로드 (아틀라스 우선, 비동기 폴백)
		private async UniTask setExtraIconAsync(string address, CancellationToken ct)
		{
			if (_extraModeIcon == null || _extraIconAddress == address)
			{
				return;
			}

			releaseExtraIcon();
			_extraIconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_extraIconAddress = null;
				_extraModeIcon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_extraModeIcon.sprite = atlasSprite;
				_extraModeIcon.enabled = true;
				_extraIconAddress = null;
				return;
			}

			_extraModeIcon.enabled = false;
			(bool cancelled, Sprite icon) = await ResourceManager.Instance
				.AcquireAsync<Sprite>(address, ct)
				.SuppressCancellationThrow();

			if (cancelled == true || _extraIconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_extraModeIcon.sprite = icon;
				_extraModeIcon.enabled = true;
			}
		}

		private void releaseExtraIcon()
		{
			if (string.IsNullOrEmpty(_extraIconAddress) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_extraIconAddress);
				_extraIconAddress = null;
			}
		}
	}
}
