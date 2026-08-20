using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 마을 씬(3.Town) HUD. 게임의 허브 — 포탈로 필드에, 버튼으로 던전에 들어간다.
	public class TownHUD : UIScreen
	{
		[SerializeField] private UIButton _portalButton;		// 포탈 — 액트/스테이지 선택 UI 열기
		[SerializeField] private UIButton _dungeonButton;	// 던전 선택 UI 열기

		[Header("하단 탭")]
		[SerializeField] private TabGroup _tabGroup;

		[Header("캐릭터 정보")]
		[SerializeField] private Slider _expSlider;				// Exp
		[SerializeField] private TMP_Text _expText;				// Exp/Text (TMP)
		[SerializeField] private TMP_Text _levelText;			// Text_Level

		// 탭이 열 화면의 Addressable 주소 (탭→화면 매핑은 코드에서 관리)
		private const string EQUIPMENT_ADDRESS = "UI_Equipment";
		private const string DUNGEON_ADDRESS = "UI_DungeonSelect";
		private const string FIELD_SELECT_ADDRESS = "UI_FieldSelect";

		private void Awake()
		{
			_portalButton.OnClickEvent += onPortalClicked;
			_dungeonButton.OnClickEvent += onDungeonClicked;

			// 배타 선택은 TabGroup이 담당하고, 여기선 선택된 탭의 화면 처리만 한다.
			_tabGroup.OnTabChanged += onTabChanged;
			EventManager.Instance.Subscribe<WindowClosedEvent>(onOverlayClosed);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);

			Debug.Log("마을 HUD 로드");
		}

		private void Start()
		{
			refreshSelectedCharacter();
		}

		private void OnDestroy()
		{
			_portalButton.OnClickEvent -= onPortalClicked;
			_dungeonButton.OnClickEvent -= onDungeonClicked;

			_tabGroup.OnTabChanged -= onTabChanged;
			EventManager.Instance.Unsubscribe<WindowClosedEvent>(onOverlayClosed);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
		}

		private void onTabChanged(int index)
		{
			tabFlowAsync((TownMenuTab)index).Forget();
		}

		// 열린 오버레이를 조용히 닫고(선택 유지), 선택된 탭에 연결된 화면을 연다.
		private async UniTask tabFlowAsync(TownMenuTab tab)
		{
			await UIManager.Instance.CloseAllWindowsAsync();

			switch (tab)
			{
				case TownMenuTab.Equipment:
					await UIManager.Instance.OpenWindowAsync<EquipmentUI>(EQUIPMENT_ADDRESS, this.GetCancellationTokenOnDestroy());
					break;
			}
		}

		// 사용자가 화면을 닫아 오버레이 스택이 비면 탭 선택을 해제한다.
		private void onOverlayClosed(WindowClosedEvent e)
		{
			_tabGroup.ClearSelection();
		}

		// 레벨업/경험치 변동 시 중앙 캐릭터 정보를 다시 그린다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refreshSelectedCharacter();
		}

		// 캐릭터의 레벨/경험치 비율을 갱신한다.
		// 캐릭터가 하나뿐이라 이름·아이콘은 테이블에서 오지 않는다(프리팹 기본값 사용).
		private void refreshSelectedCharacter()
		{
			Loadout loadout = Account.Instance.Loadout;
			int level = loadout.Level;
			int exp = loadout.Exp;

			_levelText.text = level.ToString();

			// 슬라이더/텍스트 — 현재/필요 표기, 최대 레벨은 MAX.
			int reqExp = requiredExpForNext(level);
			if (reqExp <= 0)
			{
				_expText.text = "MAX";
				_expSlider.value = 1f;
			}
			else
			{
				_expText.text = exp.ToString() + "/" + reqExp.ToString();
				// 수동 레벨업이라 현재가 필요를 초과할 수 있어 1로 클램프.
				_expSlider.value = Mathf.Clamp01((float)exp / reqExp);
			}
		}

		// 다음 레벨 도달에 필요한 누적 경험치(Table_CharacterLevelExp). 없으면 0(최대 레벨).
		private static int requiredExpForNext(int level)
		{
			Table_CharacterLevelExp.Row next = Table_CharacterLevelExp.Get(level + 1);
			return next != null ? next.TotalExperience : 0;
		}

		// 포탈 — 액트를 고르고 스테이지를 골라 필드로 나간다.
		private void onPortalClicked()
		{
			UIManager.Instance.OpenWindowAsync<FieldSelectUI>(FIELD_SELECT_ADDRESS, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void onDungeonClicked()
		{
			UIManager.Instance.OpenWindowAsync<DungeonSelectUI>(DUNGEON_ADDRESS, this.GetCancellationTokenOnDestroy()).Forget();
		}
	}
}
