using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Flow;
using ProjectOne.Battle;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 로비 씬(3.Lobby) HUD.
	// 캐릭터 화면 중앙 배치 + 내비게이션 버튼들 (캐릭터/인벤토리/상점/던전 입장 등)
	public class LobbyHUD : UIScreen
	{
		[SerializeField] private UIButton _testButton;

		[Header("하단 탭")]
		[SerializeField] private TabGroup _tabGroup;

		[Header("선택 캐릭터 정보")]
		[SerializeField] private TMP_Text _characterNameText;	// Text_CharacterName
		[SerializeField] private Image _characterIcon;			// Character
		[SerializeField] private Slider _expSlider;				// Exp
		[SerializeField] private TMP_Text _expText;				// Exp/Text (TMP)
		[SerializeField] private TMP_Text _levelText;			// Text_Level

		[Header("임시 전투 진입 파라미터 (던전 선택 UI 구현 전까지)")]
		[SerializeField] private int _testMapId = 1;

		// 탭이 열 화면의 Addressable 주소 (탭→화면 매핑은 코드에서 관리)
		private const string CHARACTER_ADDRESS = "UI_Character";
		private const string EQUIPMENT_ADDRESS = "UI_Equipment";

		// 현재 로드한 캐릭터 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_testButton.OnClickEvent += onBattleEnterClicked;

			// 배타 선택은 TabGroup이 담당하고, 여기선 선택된 탭의 화면 처리만 한다.
			_tabGroup.OnTabChanged += onTabChanged;
			EventManager.Instance.Subscribe<OverlayClosedEvent>(onOverlayClosed);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);

			Debug.Log("로비씬UI 로드!");
		}

		private void Start()
		{
			refreshSelectedCharacter();
		}

		private void OnDestroy()
		{
			_testButton.OnClickEvent -= onBattleEnterClicked;

			_tabGroup.OnTabChanged -= onTabChanged;
			EventManager.Instance.Unsubscribe<OverlayClosedEvent>(onOverlayClosed);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);

			releaseIcon();
		}

		private void onTabChanged(int index)
		{
			tabFlowAsync((LobbyMenuTab)index).Forget();
		}

		// 열린 오버레이를 조용히 닫고(선택 유지), 선택된 탭에 연결된 화면을 연다.
		private async UniTask tabFlowAsync(LobbyMenuTab tab)
		{
			await UIManager.Instance.CloseAllOverlaysAsync();

			switch (tab)
			{
				case LobbyMenuTab.Character:
					await UIManager.Instance.OpenOverlayAsync<CharacterUI>(CHARACTER_ADDRESS, this.GetCancellationTokenOnDestroy());
					break;

				case LobbyMenuTab.Equipment:
					await UIManager.Instance.OpenOverlayAsync<EquipmentUI>(EQUIPMENT_ADDRESS, this.GetCancellationTokenOnDestroy());
					break;
			}
		}

		// 사용자가 화면을 닫아 오버레이 스택이 비면 탭 선택을 해제한다.
		private void onOverlayClosed(OverlayClosedEvent e)
		{
			_tabGroup.ClearSelection();
		}

		// 선택 변경/레벨업/경험치 변동 시 중앙 캐릭터 정보를 다시 그린다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refreshSelectedCharacter();
		}

		// 현재 선택된 캐릭터의 이름/아이콘/레벨/경험치 비율을 갱신한다.
		private void refreshSelectedCharacter()
		{
			Loadout loadout = Account.Instance.Loadout;
			int selectedId = loadout.Selected;

			Table_Character.Row row = Table_Character.Get(selectedId);
			if (row == null)
			{
				_characterNameText.text = string.Empty;
				_levelText.text = string.Empty;
				_expText.text = string.Empty;
				_expSlider.value = 0f;
				setIcon(null).Forget();
				return;
			}

			OwnedCharacter oc = loadout.GetOwned(selectedId);
			int level = oc != null ? oc.level : 1;
			int exp = oc != null ? oc.exp : 0;

			_characterNameText.text = row.Name;
			_levelText.text = level.ToString();

			// 슬라이더/텍스트 — 디테일 팝업 ExpText 와 동일 표기(현재/필요, 최대 레벨은 MAX).
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

			setIcon(row.Icon).Forget();
		}

		// 다음 레벨 도달에 필요한 누적 경험치(Table_LevelExp). 없으면 0(최대 레벨).
		private static int requiredExpForNext(int level)
		{
			Table_LevelExp.Row next = Table_LevelExp.Get(level + 1);
			return next != null ? next.TotalExperience : 0;
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		private async UniTaskVoid setIcon(string address)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				_characterIcon.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = IconAtlasCache.Instance.Get(address);
			if (atlasSprite != null)
			{
				_characterIcon.sprite = atlasSprite;
				_iconAddress = null;
				return;
			}

			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 요청됐으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_characterIcon.sprite = icon;
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

		private void onBattleEnterClicked()
		{
			BattleContext ctx = new BattleContext();
			ctx.MapId = _testMapId;
			ctx.CharacterId = Account.Instance.Loadout.Selected;

			GameFlow.Instance.ChangeStateAsync(new BattleState(ctx)).Forget();
		}
	}
}
