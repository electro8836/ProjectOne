using UnityEngine;

namespace ProjectOne.Unit.AI
{
	// AI 의사결정 공유 상태 (POCO). 이동형 AI(몬스터/PVP)가 타겟/앵커를 저장하는 용도.
	// 히어로 자동스킬 AI 는 사용하지 않는다.
	public sealed class Blackboard
	{
		public UnitBase Target;
		public Vector2 Anchor;
	}
}
