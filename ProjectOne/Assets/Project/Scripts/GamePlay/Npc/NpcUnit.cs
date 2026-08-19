using EDT;
using UnityEngine;

namespace ProjectOne.Npcs
{
	// 배치된 NPC 하나.
	//
	// **UnitBase 파생이 아니다.** NPC 는 스탯도 전투도 AI 도 없다. UnitBase 를 물려받으면
	// UnitContainer 의 시뮬레이션 루프(캐시 갱신·공간 해시·분리 계산)에 매 프레임 실려
	// 아무것도 하지 않는 유닛을 계속 계산하게 된다.
	//
	// 상호작용 판정은 NpcInteraction 이 소유한다 — 여기는 "누구인가"만 안다.
	public class NpcUnit : MonoBehaviour
	{
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
		}
	}
}
