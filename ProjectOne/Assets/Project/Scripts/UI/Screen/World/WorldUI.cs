using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Resources;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 월드 화면 1회분 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct WorldInfoData
	{
		public string actIconAddress;
		public string actName;
		public string locationName;	// 지금 서 있는 곳 — 필드 이름 또는 마을 이름
		public float progress;		// 액트 진행률 0~1
	}

	// 월드 화면의 View(MVP). 네비게이션 바의 월드 탭이 UIPrefab_World 를 창 캔버스에 연다.
	// 표시와 입력 전달만 담당하고, 어느 액트를 보여줄지·어떤 팝업을 열지는 WorldPresenter 가 정한다.
	public class WorldUI : UIScreen, IView
	{
		[Header("액트")]
		[SerializeField] private Image _actIcon;			// Act/ActIcon
		[SerializeField] private TMP_Text _actNameText;		// Act/ActName
		[SerializeField] private TMP_Text _fieldNameText;	// Act/FieldNameText
		[SerializeField] private UIButton _actButton;		// Act

		[Header("진행도")]
		[SerializeField] private Image _progressFill;		// Act/ProgressInfo/FillArea/Fill
		[SerializeField] private TMP_Text _progressText;	// Act/ProgressInfo/ProgressText

		[Header("던전")]
		[SerializeField] private UIButton _goldButton;		// Dungeon/Dungeon_Gold

		[Header("닫기")]
		[SerializeField] private UIButton _homeButton;		// HomeButton

		public event Action OnActClicked;
		public event Action OnGoldDungeonClicked;
		public event Action OnHomeClicked;

		private readonly WorldPresenter _presenter = new WorldPresenter();

		// 로드해 둔 액트 아이콘 주소. 참조카운트 해제 대상을 스스로 기억한다.
		private string _iconAddress;

		private void Awake()
		{
			_actButton.OnClickEvent += onActClicked;
			_goldButton.OnClickEvent += onGoldClicked;
			_homeButton.OnClickEvent += onHomeClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			releaseIcon();

			_actButton.OnClickEvent -= onActClicked;
			_goldButton.OnClickEvent -= onGoldClicked;
			_homeButton.OnClickEvent -= onHomeClicked;

			_presenter.Dispose();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public async UniTask RenderAsync(WorldInfoData data, CancellationToken ct)
		{
			_actNameText.text = data.actName;
			_fieldNameText.text = $"위치 : {data.locationName}";

			_progressFill.fillAmount = data.progress;
			_progressText.text = $"{Mathf.RoundToInt(data.progress * 100f)}%";

			await setIcon(data.actIconAddress, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 아이콘 로드 관용구 — 아틀라스에 있으면 동기로 끝내고, 아니면 참조카운트를 걸어 비동기로 받는다.
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
				_actIcon.sprite = null;
				_actIcon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_actIcon.sprite = atlasSprite;
				_actIcon.enabled = true;
				_iconAddress = null;	// 참조카운트 대상이 아니다 — 해제가 헛돌지 않도록 지운다
				return;
			}

			_actIcon.enabled = false;	// 프리팹 기본 스프라이트가 잠깐 비치는 것을 막는다

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 늦게 도착한 로드가 그 사이 바뀐 아이콘을 덮어쓰지 않도록 확인한다.
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_actIcon.sprite = icon;
				_actIcon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		private void onActClicked()
		{
			if (OnActClicked != null)
			{
				OnActClicked.Invoke();
			}
		}

		private void onGoldClicked()
		{
			if (OnGoldDungeonClicked != null)
			{
				OnGoldDungeonClicked.Invoke();
			}
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null)
			{
				OnHomeClicked.Invoke();
			}
		}
	}
}
