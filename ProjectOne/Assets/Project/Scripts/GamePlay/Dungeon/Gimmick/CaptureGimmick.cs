using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 점령 기믹 — 미구현. 베이스 프레임(트리거 진입/이탈 감지) 검증용 스텁.
	// 추후: 히어로가 범위에 있는 동안 점령 수치를 올리고, 가득 차면 스테이지 클리어 신호를 보낸다.
	public sealed class CaptureGimmick : DungeonGimmick
	{
		protected override void OnHeroEnter(UnitBase hero)
		{
			// TODO: 점령 게이지 증가 시작
		}

		protected override void OnHeroExit(UnitBase hero)
		{
			// TODO: 점령 게이지 증가 중단
		}
	}
}
