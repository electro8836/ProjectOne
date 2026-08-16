using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Field;
using ProjectOne.Flow;

namespace ProjectOne.UI
{
	// 필드 씬(4.Field) HUD.
	//
	// 필드는 클리어 개념이 없는 사냥터다 (맵 설계 1.2). 나가는 방법은 두 가지뿐이다.
	// - 포탈: 다른 스테이지/액트로 이동 (씬 전환 없음)
	// - 마을 복귀: 씬 전환
	public class FieldHUD : UIScreen
	{
		[SerializeField] private UIButton _portalButton;		// 액트/스테이지 선택
		[SerializeField] private UIButton _townButton;			// 마을 복귀
		[SerializeField] private UIButton _dungeonButton;		// 필드에서 던전 직행
		[SerializeField] private TMP_Text _stageNameText;

		private const string STAGE_SELECT_ADDRESS = "UI_StageSelect";
		private const string DUNGEON_ADDRESS = "UI_DungeonSelect";

		private void Awake()
		{
			_portalButton.OnClickEvent += onPortalClicked;
			_townButton.OnClickEvent += onTownClicked;
			_dungeonButton.OnClickEvent += onDungeonClicked;
		}

		private void Start()
		{
			refreshStageName();
		}

		private void OnDestroy()
		{
			_portalButton.OnClickEvent -= onPortalClicked;
			_townButton.OnClickEvent -= onTownClicked;
			_dungeonButton.OnClickEvent -= onDungeonClicked;
		}

		// 현재 스테이지 이름 — 표시명은 Map 테이블이 소유한다.
		private void refreshStageName()
		{
			if (_stageNameText == null || FieldDirector.HasInstance == false)
			{
				return;
			}

			Table_Map.Row map = Table_Map.Get(FieldDirector.Instance.CurrentStageId);
			_stageNameText.text = (map != null) ? map.Name : string.Empty;
		}

		private void onPortalClicked()
		{
			UIManager.Instance.OpenOverlayAsync<StageSelectUI>(STAGE_SELECT_ADDRESS, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void onDungeonClicked()
		{
			UIManager.Instance.OpenOverlayAsync<DungeonSelectUI>(DUNGEON_ADDRESS, this.GetCancellationTokenOnDestroy()).Forget();
		}

		// 마을 귀환은 전체 회복 지점이다 — 회복 자체는 TownState 진입 시 처리한다.
		private void onTownClicked()
		{
			GameFlow.Instance.ChangeStateAsync(new TownState()).Forget();
		}
	}
}
