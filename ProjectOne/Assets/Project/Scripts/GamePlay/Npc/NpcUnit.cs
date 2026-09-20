using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectOne.UI;

namespace ProjectOne.Npcs
{
	// 배치된 NPC 하나.
	//
	// **UnitBase 파생이 아니다.** NPC 는 스탯도 전투도 AI 도 없다. UnitBase 를 물려받으면
	// UnitManager 의 시뮬레이션 루프(캐시 갱신·공간 해시·분리 계산)에 매 프레임 실려
	// 아무것도 하지 않는 유닛을 계속 계산하게 된다.
	//
	// 클릭하면 NpcType 에 대응하는 기능 화면(상점·대장간)을 연다. 대응 화면이 없으면 아무 일도 없다.
	//
	// 클릭을 받으려면 Collider2D 가 필요하다 — 프리팹에 없으면 경고를 남기고 상호작용이 죽는다.
	public class NpcUnit : MonoBehaviour
	{
		// 유닛과 같은 레이어여야 Y 정렬이 서로 비교된다 (MapBlocker.SORTING_LAYER 와 같다).
		private const string SORTING_LAYER = "GamePlay";

		// worldY → sortingOrder 변환 정밀도. UnitAnimator._precision / MapBlocker._sortPrecision 과
		// 반드시 같은 값이어야 한다 — 하나만 바꾸면 그 순간 정렬 축이 갈라진다.
		private const float SORT_PRECISION = 1000f;

		private int _npcId;

		private Table_Npc.Row _row;

		public int NpcId
		{
			get { return _npcId; }
		}

		public Table_Npc.Row Row
		{
			get { return _row; }
		}

		public void Setup(int npcId, Table_Npc.Row row)
		{
			_npcId = npcId;
			_row = row;

			// 방향은 테이블에 두지 않는다 — 모든 NPC 가 동일 처리라 코드가 정한다 (설계 5.3).
			// 지금은 기본 방향 그대로 둔다. 좌우 반전이 필요해지면 여기서 처리한다.

			applySorting();
			warnIfNotClickable();
		}

		// 발밑 Y 로 sortingOrder 를 정해 히어로와 같은 축에서 앞뒤가 결정되게 한다.
		// 프리팹에는 SortingGroup 이 없으므로 여기서 보장한다 — 그룹을 루트에 얹으면
		// 자식 렌더러(Model 0 / Shadow -50)의 상대 순서는 그대로 남는다.
		// NPC 는 제자리에서 움직이지 않으므로 스폰 시 1회로 끝난다.
		private void applySorting()
		{
			SortingGroup group = this.GetComponent<SortingGroup>();
			if (group == null)
			{
				group = this.gameObject.AddComponent<SortingGroup>();
			}

			// Y 가 클수록(위) 뒤로 → 음수
			group.sortingOrder = -Mathf.RoundToInt(this.transform.position.y * SORT_PRECISION);
			group.sortingLayerID = SortingLayer.NameToID(SORTING_LAYER);
		}

		// 클릭 상호작용. 2D 콜라이더가 있으면 유니티가 불러 준다.
		private void OnMouseUpAsButton()
		{
			if (_row == null)
			{
				return;
			}

			// 화면은 UIManager.OpenAsync(UIScreenId) 하나로만 연다 — HUD 버튼이 여는 상점과
			// NPC 가 여는 상점이 같은 코드여야 "굳이 찾아가지 않아도 같은 창"이 성립한다.
			UIScreenId screen = UIScreenCatalog.FromNpcType(_row.NpcType);
			if (screen == UIScreenId.None)
			{
				return;
			}

			openAsync(screen).Forget();
		}

		private async UniTaskVoid openAsync(UIScreenId screen)
		{
			await UIManager.Instance.OpenAsync(screen, this.GetCancellationTokenOnDestroy());
		}

		// 상호작용 대상으로 눌리려면 콜라이더가 있어야 한다.
		// 없으면 조용히 아무 일도 안 일어나므로 배치 시점에 드러낸다.
		// 근접으로 발동하는 NPC(포탈)는 클릭을 쓰지 않으므로 검사에서 뺀다.
		private void warnIfNotClickable()
		{
			if (this.GetComponent<NpcProximityTrigger>() != null)
			{
				return;
			}

			if (this.GetComponentInChildren<Collider2D>(true) == null)
			{
				Debug.LogWarning($"[NpcUnit] Npc {_npcId} 프리팹에 Collider2D 가 없습니다 — 클릭 상호작용이 동작하지 않습니다.");
			}
		}
	}
}
