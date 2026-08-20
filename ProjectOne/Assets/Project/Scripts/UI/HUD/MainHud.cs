using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 화면 맥락 — 어디에 있느냐에 따라 MainHUD 의 어떤 부분이 보이는가.
	//
	// [Flags] 인 이유는 "마을과 필드에서만 보이는 버튼" 같은 조합을 인스펙터 체크박스로
	// 표현하기 위해서다. 그래야 코드가 버튼 목록을 알 필요가 없다.
	[Flags]
	public enum HudContext
	{
		None = 0,
		Town = 1 << 0,
		Field = 1 << 1,
		Dungeon = 1 << 2,
		Raid = 1 << 3
	}

	// 게임 전역 HUD 의 View(MVP).
	//
	// **씬별 HUD 를 두지 않는다.** 이 하나가 씬을 가로질러 살아 있고, 어디서 무엇을 보일지는
	// ContextGroup 가시성으로 정한다. 던전 전용 위젯(웨이브·보스바·나가기)도 여기 그룹으로 들어온다.
	//
	// View 는 그리기와 입력 전달만 한다. 데이터 조회·판단은 MainHudPresenter 가 갖는다 (Passive View).
	public class MainHud : UIScreen, IView
	{
		// 맥락별로 보일 오브젝트 묶음.
		//
		// 어떤 버튼이 어디서 보이는지를 **인스펙터가 소유한다.** UI 를 재구성해도 코드를 고치지 않는다.
		[Serializable]
		public class ContextGroup
		{
			[Tooltip("이 묶음이 보일 맥락(복수 선택 가능)")]
			public HudContext contexts;

			public GameObject[] objects;
		}

		[Header("맥락별 표시 묶음")]
		[SerializeField] private ContextGroup[] _contextGroups;

		[Header("캐릭터 정보")]
		[SerializeField] private TMP_Text _levelText;
		[SerializeField] private Slider _expSlider;
		[SerializeField] private TMP_Text _expText;

		[Header("체력")]
		[SerializeField] private Slider _hpSlider;
		[SerializeField] private TMP_Text _hpText;

		// 화면 열기 버튼 — 비워두면 자식에서 자동 수집한다. 사용자가 UI 를 재구성해도 배열을 다시 채울 필요가 없다.
		[Header("화면 열기 버튼 (비우면 자식에서 자동 수집)")]
		[SerializeField] private ScreenOpenButton[] _screenButtons;

		// 하단 메뉴. 배타 선택은 TabGroup 이 알아서 하고, 여기서는 "선택되면 무엇을 여는가"만 정한다.
		[Header("하단 메뉴")]
		[SerializeField] private TabGroup _bottomTabs;

		// 탭 순서대로의 화면. None 이거나 배열이 짧으면 로그만 남기고 넘어간다 —
		// 화면 프리팹이 생기면 코드를 고치지 않고 인스펙터에서 enum 만 고르면 연결된다.
		[SerializeField] private UIScreenId[] _tabScreens;

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────
		public event Action<UIScreenId> OnScreenRequested;

		private readonly MainHudPresenter _presenter = new MainHudPresenter();

		private void Awake()
		{
			collectScreenButtons();
			bindScreenButtons();

			if (_bottomTabs != null)
			{
				_bottomTabs.OnTabChanged += onBottomTabChanged;
			}

			_presenter.Initialize(this);
		}

		// 체력은 이벤트가 없어 값 비교로 갱신한다 — 판단은 Presenter 가 하고 View 는 위임만 한다.
		private void Update()
		{
			_presenter.Tick();
		}

		private void OnDestroy()
		{
			_presenter.Dispose();
			unbindScreenButtons();

			if (_bottomTabs != null)
			{
				_bottomTabs.OnTabChanged -= onBottomTabChanged;
			}
		}

		// ── 표시 (Presenter 가 지시) ───────────────────────────────────

		public void SetLevel(int level)
		{
			if (_levelText != null)
			{
				_levelText.text = level.ToString();
			}
		}

		// requiredExp 가 0이면 최대 레벨이다.
		public void SetExp(int exp, int requiredExp)
		{
			if (requiredExp <= 0)
			{
				if (_expText != null)
				{
					_expText.text = "MAX";
				}

				if (_expSlider != null)
				{
					_expSlider.value = 1f;
				}

				return;
			}

			if (_expText != null)
			{
				_expText.text = exp.ToString() + "/" + requiredExp.ToString();
			}

			if (_expSlider != null)
			{
				// 수동 레벨업이라 현재 경험치가 필요치를 넘을 수 있어 클램프한다.
				_expSlider.value = Mathf.Clamp01((float)exp / requiredExp);
			}
		}

		public void SetHp(float current, float max)
		{
			if (_hpSlider != null)
			{
				_hpSlider.value = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
			}

			if (_hpText != null)
			{
				_hpText.text = Mathf.CeilToInt(current).ToString() + "/" + Mathf.CeilToInt(max).ToString();
			}
		}

		// 맥락에 맞지 않는 묶음을 숨긴다. 레이드에서 가이드 버튼을 감추는 장치가 이것이다.
		public void ApplyContext(HudContext context)
		{
			if (_contextGroups == null)
			{
				return;
			}

			for (int i = 0; i < _contextGroups.Length; i++)
			{
				ContextGroup group = _contextGroups[i];
				if (group == null || group.objects == null)
				{
					continue;
				}

				bool visible = (group.contexts & context) != 0;
				for (int n = 0; n < group.objects.Length; n++)
				{
					if (group.objects[n] != null)
					{
						group.objects[n].SetActive(visible);
					}
				}
			}
		}

		// ── 입력 ──────────────────────────────────────────────────────

		// 비활성 자식까지 훑는다 — 맥락으로 꺼 둔 버튼도 나중에 켜지면 동작해야 한다.
		private void collectScreenButtons()
		{
			if (_screenButtons != null && _screenButtons.Length > 0)
			{
				return;
			}

			_screenButtons = this.GetComponentsInChildren<ScreenOpenButton>(true);
		}

		private void bindScreenButtons()
		{
			for (int i = 0; i < _screenButtons.Length; i++)
			{
				if (_screenButtons[i] != null)
				{
					_screenButtons[i].OnClicked += onScreenButtonClicked;
				}
			}
		}

		private void unbindScreenButtons()
		{
			if (_screenButtons == null)
			{
				return;
			}

			for (int i = 0; i < _screenButtons.Length; i++)
			{
				if (_screenButtons[i] != null)
				{
					_screenButtons[i].OnClicked -= onScreenButtonClicked;
				}
			}
		}

		private void onScreenButtonClicked(UIScreenId id)
		{
			if (OnScreenRequested != null)
			{
				OnScreenRequested.Invoke(id);
			}
		}

		// 탭 인덱스 → 화면. 아직 연결하지 않은 탭은 눌리기만 하고 아무 일도 하지 않는다.
		private void onBottomTabChanged(int index)
		{
			if (_tabScreens == null || index < 0 || index >= _tabScreens.Length)
			{
				Debug.Log($"[MainHud] 탭 {index} 에 연결된 화면이 없습니다.");
				return;
			}

			UIScreenId screen = _tabScreens[index];
			if (screen == UIScreenId.None)
			{
				Debug.Log($"[MainHud] 탭 {index} 에 연결된 화면이 없습니다.");
				return;
			}

			if (OnScreenRequested != null)
			{
				OnScreenRequested.Invoke(screen);
			}
		}
	}
}
