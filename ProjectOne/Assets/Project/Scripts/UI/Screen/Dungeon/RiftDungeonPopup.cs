using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 균열 던전 현황 1회분 렌더 데이터 — 입장·소탕으로 바뀌므로 그때마다 다시 그린다.
	public struct RiftDungeonStatusData
	{
		public string maxWave;
		public string checkPoint;
		public string enterCount;
		public int enterReward;
		public int waveReward;
		public string rewardIconAddress;
	}

	// 균열 스킬 한 칸의 렌더 데이터
	public struct RiftSkillSlotData
	{
		public int riftSkillId;
		public string iconAddress;
	}

	// 균열던전 팝업의 View(MVP). UIManager.ShowRiftDungeonPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 입장·소탕 판정과 스킬 선택 상태는 RiftDungeonPopupPresenter 가 한다.
	public class RiftDungeonPopup : UIScreen, IView
	{
		[Header("던전 정보")]
		[SerializeField] private TMP_Text _nameText;			// Frame/Top/DungeonName
		[SerializeField] private Image _thumbnailImage;			// Frame/DungeonInfo/Frame/Thumbnail

		[Header("현황")]
		[SerializeField] private TMP_Text _maxWaveText;			// Info/MaxLevel/RightText
		[SerializeField] private TMP_Text _checkPointText;		// Info/CheckPoint/RightText
		[SerializeField] private TMP_Text _enterCountText;		// Info/EnterCount/RightText
		[SerializeField] private TMP_Text _refreshTimeText;		// Info/RefreshTime/RightText
		[SerializeField] private TMP_Text _enterRewardText;		// Info/EnterReward/RightGroup/RightText
		[SerializeField] private Image _enterRewardIcon;		// Info/EnterReward/RightGroup/RightIcon
		[SerializeField] private TMP_Text _waveRewardText;		// Info/WaveReward/RightGroup/RightText
		[SerializeField] private Image _waveRewardIcon;			// Info/WaveReward/RightGroup/RightIcon

		[Header("스킬 선택")]
		[SerializeField] private RectTransform _skillGrid;		// SkillSelect/ScrollRect/Veiwport/Content/Grid
		[SerializeField] private RiftSkillSlot _slotPrefab;		// UIPrefab_RiftSkillSlot
		[SerializeField] private UIButton _selectRoot;			// SkillSelect/SelectRoot
		[SerializeField] private TMP_Text _selectNameText;		// SelectRoot/BackImage/SkillNameText
		[SerializeField] private Image _selectIcon;				// SelectRoot/Icon
		[SerializeField] private RiftSkillInfoPopup _infoPopupPrefab;	// UIPrefab_RiftSkillInfoPopup

		[Header("버튼")]
		[SerializeField] private UIButton _enterButton;			// Frame/Bottom/BottomButtons/EnterButton
		[SerializeField] private UIButton _sweepButton;			// Frame/Bottom/BottomButtons/SweepButton
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		public event Action<int> OnSkillSelected;	// 고른 칸의 RiftSkill.ID
		public event Action OnSkillInfoClicked;
		public event Action OnEnterClicked;
		public event Action OnSweepClicked;

		// 1초마다 발행. 시간을 어떻게 해석해 무엇을 그릴지는 Presenter 가 정한다.
		public event Action OnSecondTick;

		private readonly RiftDungeonPopupPresenter _presenter = new RiftDungeonPopupPresenter();
		private readonly List<RiftSkillSlot> _slots = new List<RiftSkillSlot>();

		private SpriteBinder _thumbnailBinder;
		private SpriteBinder _enterRewardBinder;
		private SpriteBinder _waveRewardBinder;
		private SpriteBinder _selectIconBinder;

		private RiftSkillInfoPopup _infoPopup;
		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			_thumbnailBinder = new SpriteBinder(_thumbnailImage);
			_enterRewardBinder = new SpriteBinder(_enterRewardIcon);
			_waveRewardBinder = new SpriteBinder(_waveRewardIcon);
			_selectIconBinder = new SpriteBinder(_selectIcon);

			_selectRoot.OnClickEvent += onSelectRootClicked;
			_enterButton.OnClickEvent += onEnterClicked;
			_sweepButton.OnClickEvent += onSweepClicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_thumbnailBinder.Release();
			_enterRewardBinder.Release();
			_waveRewardBinder.Release();
			_selectIconBinder.Release();

			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnClicked -= onSlotClicked;
			}

			_selectRoot.OnClickEvent -= onSelectRootClicked;
			_enterButton.OnClickEvent -= onEnterClicked;
			_sweepButton.OnClickEvent -= onSweepClicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			_presenter.Dispose();
		}

		// 갱신 남은시간을 1초마다 다시 그리기 위한 틱. 오브젝트가 파괴되면 코루틴도 함께 멈춘다.
		private void OnEnable()
		{
			StartCoroutine(tickRoutine());
		}

		private IEnumerator tickRoutine()
		{
			WaitForSeconds wait = new WaitForSeconds(1f);
			while (true)
			{
				if (OnSecondTick != null)
				{
					OnSecondTick.Invoke();
				}

				yield return wait;
			}
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.OnOpenAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void RenderInfo(string dungeonName, string thumbnailAddress)
		{
			_nameText.text = dungeonName;
			_thumbnailBinder.SetAsync(thumbnailAddress, GetDestroyToken()).Forget();
		}

		public void RenderStatus(RiftDungeonStatusData data)
		{
			_maxWaveText.text = data.maxWave;
			_checkPointText.text = data.checkPoint;
			_enterCountText.text = data.enterCount;
			_enterRewardText.text = data.enterReward.ToString();
			_waveRewardText.text = data.waveReward.ToString();

			_enterRewardBinder.SetAsync(data.rewardIconAddress, GetDestroyToken()).Forget();
			_waveRewardBinder.SetAsync(data.rewardIconAddress, GetDestroyToken()).Forget();
		}

		public void SetRefreshTime(string text)
		{
			_refreshTimeText.text = text;
		}

		public void RenderSkills(IReadOnlyList<RiftSkillSlotData> data)
		{
			for (int i = 0; i < data.Count; i++)
			{
				RiftSkillSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(data[i].riftSkillId, data[i].iconAddress);
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}
		}

		// 고른 칸 하나만 테두리를 켜고, SelectRoot 에 이름과 아이콘을 띄운다.
		public void SetSelectedSkill(int riftSkillId, string skillName, string iconAddress)
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].SetSelected(_slots[i].RiftSkillId == riftSkillId);
			}

			_selectNameText.text = skillName;
			_selectIconBinder.SetAsync(iconAddress, GetDestroyToken()).Forget();
		}

		// SelectRoot 바로 위에 정보 프레임을 띄운다. 처음 열 때 한 번만 만든다.
		public void ShowSkillInfo(string skillName, int reqGauge, int maxGauge, string desc)
		{
			if (_infoPopup == null)
			{
				_infoPopup = Instantiate(_infoPopupPrefab, transform);
			}

			_infoPopup.transform.SetAsLastSibling();
			_infoPopup.Show(skillName, reqGauge, maxGauge, desc, (RectTransform)_selectRoot.transform);
		}

		public void SetEnterInteractable(bool value)
		{
			_enterButton.interactable = value;
		}

		public void SetSweepInteractable(bool value)
		{
			_sweepButton.interactable = value;
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private RiftSkillSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			RiftSkillSlot slot = Instantiate(_slotPrefab, _skillGrid);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);

			return slot;
		}

		private void onSlotClicked(RiftSkillSlot sender)
		{
			if (OnSkillSelected != null)
			{
				OnSkillSelected.Invoke(sender.RiftSkillId);
			}
		}

		private void onSelectRootClicked()
		{
			if (OnSkillInfoClicked != null)
			{
				OnSkillInfoClicked.Invoke();
			}
		}

		private void onEnterClicked()
		{
			if (OnEnterClicked != null)
			{
				OnEnterClicked.Invoke();
			}
		}

		private void onSweepClicked()
		{
			if (OnSweepClicked != null)
			{
				OnSweepClicked.Invoke();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
