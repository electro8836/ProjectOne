using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 코드 스킬의 단위 동작 — SkillSequence 가 순서대로 실행한다.
	// 대시/대시공격/차지/대기 같은 빌딩블록을 조합해 멀티스텝 스킬(보스 콤보 등)을 구성한다.
	public interface ISkillAction
	{
		// 액션 시작 1회 — caster/skillId 주입 (테이블 행은 skillId 로 조회)
		void OnStart(UnitBase caster, SkillInfo skillId);

		// 매 프레임 갱신 — true 반환 시 이 액션 완료 (다음 액션으로 진행)
		bool Tick(float dt);

		// 정리 1회 — 이동/스킬 차단·이동 override 해제
		void OnEnd();
	}
}
