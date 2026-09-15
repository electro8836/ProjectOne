using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Resources;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 던전 정보 1회분 렌더 데이터 — 단계를 바꿔도 변하지 않는 부분이다.
	public struct DungeonInfoData
	{
		public string name;
		public string thumbnailAddress;
	}

	// 골드던전 팝업의 View(MVP). UIManager.ShowGoldDungeonPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 어느 단계를 열 수 있는지·입장 판정은 GoldDungeonPopupPresenter 가 한다.
	public class GoldDungeonPopup : UIScreen, IView
	{
		[Header("던전 정보")]
		[SerializeField] private TMP_Text _nameText;			// Frame/Top/DungeonName
		[SerializeField] private Image _thumbnailImage;				// Frame/DungeonInfo/Frame/Thumbnail

		[Header("현황")]
		[SerializeField] private TMP_Text _maxStageText;		// Frame/DungeonInfo/Info/MaxLevel/RightText
		[SerializeField] private TMP_Text _enterCountText;		// Frame/DungeonInfo/Info/EnterCount/RightText
		[SerializeField] private TMP_Text _refreshTimeText;		// Frame/DungeonInfo/Info/RefreshTime/RightText

		// 보상 그리드(Frame/DungeonInfo/Reward/…/Grid)는 아직 비어 있다 —
		// DungeonStage.RewardGroupID 가 전 행 0 이라 채울 데이터가 없다. 값이 들어오면 여기 배선한다.

		[Header("단계 선택")]
		[SerializeField] private RectTransform _stageGrid;		// Frame/StageSelect/ScrollRect/Veiwport/Content/Grid
		[SerializeField] private DungeonStageSlot _slotPrefab;	// UIPrefab_DungeonStageSlot
		[SerializeField] private CenterSnapScroll _stageScroll;	// Frame/StageSelect/ScrollRect

		[Header("버튼")]
		[SerializeField] private UIButton _enterButton;			// Frame/Bottom/BottomButtons/EnterButton
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		public event Action<int> OnStageSelected;	// 선택된 단계 번호
		public event Action OnEnterClicked;

		// 1초마다 발행. 시간을 어떻게 해석해 무엇을 그릴지는 Presenter 가 정한다.
		public event Action OnSecondTick;

		private readonly GoldDungeonPopupPresenter _presenter = new GoldDungeonPopupPresenter();
		private readonly List<DungeonStageSlot> _slots = new List<DungeonStageSlot>();

		private string _thumbnailAddress;
		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			_stageScroll.OnCenterChanged += onCenterChanged;
			_enterButton.OnClickEvent += onEnterClicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			releaseThumbnail();

			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnClicked -= onSlotClicked;
			}

			_stageScroll.OnCenterChanged -= onCenterChanged;
			_enterButton.OnClickEvent -= onEnterClicked;
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

		public async UniTask RenderInfoAsync(DungeonInfoData data, CancellationToken ct)
		{
			_nameText.text = data.name;

			await setThumbnail(data.thumbnailAddress, ct);
		}

		// 팝업이 떠 있는 동안 변하지 않는 현황 — 입장하면 팝업이 닫히므로 한 번만 그린다.
		public void RenderStatus(string maxStage, string enterCount)
		{
			_maxStageText.text = maxStage;
			_enterCountText.text = enterCount;
		}

		public void SetRefreshTime(string text)
		{
			_refreshTimeText.text = text;
		}

		public void RenderStages(IReadOnlyList<DungeonStageSlotData> data)
		{
			for (int i = 0; i < data.Count; i++)
			{
				DungeonStageSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(data[i]);
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// 칸을 다 채운 뒤라야 여백과 중앙 판정을 잡을 수 있다.
			_stageScroll.Refresh();
		}

		public void SetEnterInteractable(bool value)
		{
			_enterButton.interactable = value;
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private DungeonStageSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			DungeonStageSlot slot = Instantiate(_slotPrefab, _stageGrid);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);

			return slot;
		}

		// 누른 칸을 중앙으로 보낸다. 선택 표시와 통지는 중앙이 바뀌면서 onCenterChanged 가 맡으므로
		// 여기서 따로 하지 않는다 — 클릭과 스크롤이 같은 출구를 쓰게 해 상태가 갈라지지 않도록.
		private void onSlotClicked(DungeonStageSlot sender)
		{
			int index = _slots.IndexOf(sender);
			if (index < 0)
			{
				return;
			}

			_stageScroll.SnapTo(index);
		}

		// 중앙에 온 칸 하나만 선택 표시를 켠다.
		private void onCenterChanged(int index)
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].SetSelected(i == index);
			}

			if (index < 0 || index >= _slots.Count)
			{
				return;
			}

			if (OnStageSelected != null)
			{
				OnStageSelected.Invoke(_slots[index].Stage);
			}
		}

		private async UniTask setThumbnail(string address, CancellationToken ct)
		{
			if (_thumbnailAddress == address)
			{
				return;
			}

			releaseThumbnail();
			_thumbnailAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_thumbnailImage.sprite = null;
				_thumbnailImage.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_thumbnailImage.sprite = atlasSprite;
				_thumbnailImage.enabled = true;
				_thumbnailAddress = null;	// 참조카운트 대상이 아니다 — 해제가 헛돌지 않도록 지운다
				return;
			}

			_thumbnailImage.enabled = false;

			(bool cancelled, Sprite sprite) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			if (_thumbnailAddress != address)
			{
				return;
			}

			if (sprite != null)
			{
				_thumbnailImage.sprite = sprite;
				_thumbnailImage.enabled = true;
			}
		}

		private void releaseThumbnail()
		{
			if (string.IsNullOrEmpty(_thumbnailAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_thumbnailAddress);
				_thumbnailAddress = null;
			}
		}

		private void onEnterClicked()
		{
			if (OnEnterClicked != null)
			{
				OnEnterClicked.Invoke();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
