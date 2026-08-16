using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 코드로 정의되는 커스텀 스킬의 동작 인터페이스.
	// - SkillBehaviorRegistry 에 등록된 스킬만 이 경로를 타고, 나머지는 테이블 효과 경로로 처리된다.
	// - "시작→진행→종료" 시퀀스라 하나의 인터페이스로 합친다.
	//   보스 멀티스텝(연타+차지 등)처럼 매 프레임 진행이 필요한 행동을 코드로 오케스트레이션한다.
	public interface ISkillBehavior
	{
		// 생성 직후 1회 — caster / skillId 주입 (테이블 행은 skillId 로 조회)
		void SetContext(UnitBase caster, EDT.Skill skillId);

		// 발동 시작 1회
		void OnStart();

		// 매 프레임 갱신 — true 반환 시 이번 프레임에 스킬 종료
		bool Tick(float dt);

		// 정상 종료/취소 1회 — 이동/스킬 차단·이동 override 정리
		void OnEnd();
	}
}
