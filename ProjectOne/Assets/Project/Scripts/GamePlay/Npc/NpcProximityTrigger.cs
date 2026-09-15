using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.UI;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Npcs
{
	// 히어로가 가까이 오면 액트·필드 목록을 여는 포탈 NPC 전용 트리거.
	//
	// 포탈은 포탈 기능만 한다 — NpcInteractor(퀘스트 대화 우선순위)를 타지 않고 곧장 목록을 연다.
	// 퀘스트 NPC 는 지금처럼 클릭으로 NpcInteractor 를 탄다.
	//
	// Physics2D 트리거를 쓰지 않는다. 대상이 히어로 하나뿐이라 거리 비교 한 번이 더 싸고,
	// 콜라이더·Rigidbody2D·전용 레이어를 프리팹에 얹지 않아도 된다.
	public class NpcProximityTrigger : MonoBehaviour
	{
		private const string ACT_LIST_POPUP_ADDRESS = "UIPrefab_ActListPopup";

		[Tooltip("히어로가 이 반경 안에 들어오면 팝업이 열린다")]
		[SerializeField] private float _radius = 1f;

		// 범위 안에 있는 동안 켜져 있는 빗장. 팝업을 닫아도 내려가지 않으므로
		// 한 번 범위를 벗어났다 다시 들어와야 재발동한다.
		private bool _inside;

		// 팝업이 열려 있는 동안은 재진입해도 또 열지 않는다.
		private bool _opening;

		private void Update()
		{
			UnitBase hero = findAliveHero();
			if (hero == null)
			{
				_inside = false;
				return;
			}

			bool inRange = Scanner.InCircle(this.transform.position, _radius, hero.CachedPos);
			if (inRange == false)
			{
				_inside = false;
				return;
			}

			if (_inside == true)
			{
				return;
			}

			_inside = true;

			if (_opening == true)
			{
				return;
			}

			openAsync().Forget();
		}

		// 팝업이 닫힐 때까지 돌아오지 않는다. 이동으로 닫혔으면 MapNavigator 가 상태 전환을
		// 이미 시작했으므로 여기서 할 일은 없다.
		private async UniTaskVoid openAsync()
		{
			_opening = true;

			await UIManager.Instance
				.ShowActListPopupAsync(ACT_LIST_POPUP_ADDRESS, this.GetCancellationTokenOnDestroy())
				.SuppressCancellationThrow();

			_opening = false;
		}

		// 살아 있는 히어로 하나. DropManager.findAliveHero 와 같은 조회 방식이다.
		private static UnitBase findAliveHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				if (heroes[i] != null && heroes[i].IsDead == false)
				{
					return heroes[i];
				}
			}

			return null;
		}

		// 인스펙터에서 반경을 조절할 때 씬 뷰에서 바로 보이도록 그린다.
		private void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(this.transform.position, _radius);
		}
	}
}
