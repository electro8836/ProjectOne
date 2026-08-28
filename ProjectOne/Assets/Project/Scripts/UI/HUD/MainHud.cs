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

	// 게임 상태 → 화면 맥락.
	//
	// MainHud 와 NavigationBar 가 같은 기준으로 보이고 숨어야 한다.
	// 각자 갖고 있으면 상태를 하나 추가할 때 한쪽만 고쳐져 조용히 어긋난다.
	public static class HudContexts
	{
		public static HudContext FromState(System.Type stateType)
		{
			if (stateType == typeof(Flow.TownState))
			{
				return HudContext.Town;
			}

			if (stateType == typeof(Flow.FieldState))
			{
				return HudContext.Field;
			}

			if (stateType == typeof(Flow.DungeonState))
			{
				return HudContext.Dungeon;
			}

			// 타이틀·패치·로딩 등 — HUD 를 보일 자리가 아니다.
			return HudContext.None;
		}
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

		// 개발용 이동 버튼 — 비워두면 자식에서 자동 수집한다.
		[Header("개발용 이동 버튼 (비우면 자식에서 자동 수집)")]
		[SerializeField] private DevWarpButton[] _warpButtons;

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────
		public event Action<UIScreenId> OnScreenRequested;

		// 이동 요청 — 목적지는 Table_Map.ID. 어디로 갈지는 Presenter 가 판단한다.
		public event Action<int> OnWarpRequested;

		private readonly MainHudPresenter _presenter = new MainHudPresenter();

		private void Awake()
		{
			collectScreenButtons();
			bindScreenButtons();

			collectWarpButtons();
			bindWarpButtons();

			_presenter.Initialize(this);
		}

		// 체력은 이벤트가 없어 값 비교로 갱신한다 — 판단은 Presenter 가 하고 View 는 위임만 한다.
		private void OnDestroy()
		{
			_presenter.Dispose();
			unbindScreenButtons();
			unbindWarpButtons();
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


		// ── 이동 ──────────────────────────────────────────────────────

		// 화면 열기 버튼과 같은 방식 — 비활성 자식까지 훑는다.
		private void collectWarpButtons()
		{
			if (_warpButtons != null && _warpButtons.Length > 0)
			{
				return;
			}

			_warpButtons = this.GetComponentsInChildren<DevWarpButton>(true);
		}

		private void bindWarpButtons()
		{
			for (int i = 0; i < _warpButtons.Length; i++)
			{
				if (_warpButtons[i] != null)
				{
					_warpButtons[i].OnClicked += onWarpButtonClicked;
				}
			}
		}

		private void unbindWarpButtons()
		{
			if (_warpButtons == null)
			{
				return;
			}

			for (int i = 0; i < _warpButtons.Length; i++)
			{
				if (_warpButtons[i] != null)
				{
					_warpButtons[i].OnClicked -= onWarpButtonClicked;
				}
			}
		}

		private void onWarpButtonClicked(int mapId)
		{
			if (OnWarpRequested != null)
			{
				OnWarpRequested.Invoke(mapId);
			}
		}
	}
}
