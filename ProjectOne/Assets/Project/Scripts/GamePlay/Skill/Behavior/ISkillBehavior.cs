using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 코드로 정의되는 커스텀 스킬의 동작 인터페이스
	// - 스킬 ID(enum) 와 동일한 이름의 클래스가 이 인터페이스를 구현하면
	//   SkillExecutor 가 리플렉션으로 찾아 SkillContainer.BeginBehavior 로 위임한다.
	// - 구현 클래스는 (UnitBase caster) 생성자를 가져야 한다.
	// - 버프의 IBuffBehavior + ITickableBuff 와 대칭. 스킬은 "시작→진행→종료" 시퀀스라 하나로 합친다.
	//   보스 멀티스텝(연타+차지 등)처럼 매 프레임 진행이 필요한 행동을 코드로 오케스트레이션한다.
	public interface ISkillBehavior
	{
		// 생성 직후 1회 — caster / skillId 주입 (테이블 행은 skillId 로 조회)
		void SetContext(UnitBase caster, SkillInfo skillId);

		// 발동 시작 1회
		void OnStart();

		// 매 프레임 갱신 — true 반환 시 이번 프레임에 스킬 종료
		bool Tick(float dt);

		// 정상 종료/취소 1회 — 이동/스킬 차단·이동 override 정리
		void OnEnd();
	}
}
