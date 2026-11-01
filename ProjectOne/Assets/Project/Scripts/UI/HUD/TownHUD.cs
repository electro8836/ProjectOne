using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 마을 씬(3.Town) HUD. 게임의 허브 — 하단 탭으로 장비 등 화면을 연다.
	//
	// 필드·던전 진입 UI(FieldSelect/DungeonSelect)는 폐기했다. 진입 방식을 새로 붙일 때
	// 여기에 버튼과 배선을 다시 단다.
	public class TownHUD : UIScreen
	{
		[Header("하단 탭")]
		[SerializeField] private TabGroup _tabGroup;

		[Header("캐릭터 정보")]
		[SerializeField] private Slider _expSlider;				// Exp
		[SerializeField] private TMP_Text _expText;				// Exp/Text (TMP)
		[SerializeField] private TMP_Text _levelText;			// Text_Level

		// 탭이 열 화면의 Addressable 주소 (탭→화면 매핑은 코드에서 관리)
		private const string EQUIPMENT_ADDRESS = "UIPrefab_Equipment";

		private void Awake()
		{
			// 배타 선택은 TabGroup이 담당하고, 여기선 선택된 탭의 화면 처리만 한다.
			_tabGroup.OnTabChanged += onTabChanged;
			EventManager.Instance.Subscribe<WindowClosedEvent>(onOverlayClosed);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Subscribe<MasteryExpChangeEvent>(onMasteryExpChanged);

			Debug.Log("마을 HUD 로드");
		}

		private void Start()
		{
			refreshSelectedCharacter();
		}

		private void OnDestroy()
		{
			_tabGroup.OnTabChanged -= onTabChanged;
			EventManager.Instance.Unsubscribe<WindowClosedEvent>(onOverlayClosed);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Unsubscribe<MasteryExpChangeEvent>(onMasteryExpChanged);
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

		// 무기 교체 등 영구 능력치 변경 — 대표 레벨의 기준 마스터리가 바뀔 수 있다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refreshSelectedCharacter();
		}

		// 경험치 적립·레벨업.
		private void onMasteryExpChanged(MasteryExpChangeEvent e)
		{
			refreshSelectedCharacter();
		}

		// 대표 레벨(장착 무기의 숙련도)과 그 경험치 비율을 갱신한다 (마스터리 설계 3.3).
		// 캐릭터가 하나뿐이라 이름·아이콘은 테이블에서 오지 않는다(프리팹 기본값 사용).
		private void refreshSelectedCharacter()
		{
			MasteryProgress progress = Account.Instance.Mastery.CurrentProgress;
			if (progress == null)
			{
				// 무기 미착용 — 경험치가 쌓일 마스터리가 없다.
				_levelText.text = string.Empty;
				_expText.text = string.Empty;
				_expSlider.value = 0f;
				return;
			}

			int level = progress.Level;
			int exp = progress.TotalExp;

			_levelText.text = level.ToString();

			// 테이블 값은 누적치다 — 현재 레벨 구간만 떼어내야 레벨업 직후 게이지가 0에서 시작한다.
			int nextTotal = totalExpFor(level + 1);
			if (nextTotal <= 0)
			{
				_expText.text = "MAX";
				_expSlider.value = 1f;
				return;
			}

			int currentTotal = totalExpFor(level);
			int gained = exp - currentTotal;
			int required = nextTotal - currentTotal;

			_expText.text = gained.ToString() + "/" + required.ToString();
			_expSlider.value = (required > 0) ? Mathf.Clamp01((float)gained / required) : 0f;
		}

		// 해당 레벨 도달에 필요한 누적 경험치. 없으면 0(최대 레벨 초과).
		private static int totalExpFor(int level)
		{
			return MasteryCatalog.GetMasteryTotalExp(level);
		}
	}
}
