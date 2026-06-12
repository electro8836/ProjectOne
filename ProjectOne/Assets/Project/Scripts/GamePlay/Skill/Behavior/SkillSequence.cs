using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 여러 ISkillAction 을 순서대로 실행하는 코드 스킬 — ISkillBehavior 구현.
	// SkillContainer 가 매 프레임 Tick 하며, 현재 액션이 끝나면 다음 액션으로 넘어가고 마지막까지 끝나면 종료한다.
	// 단일 동작 스킬도 new SkillSequence(new DashAction()) 한 줄, 멀티스텝(보스 콤보)은 액션을 나열한다.
	public sealed class SkillSequence : ISkillBehavior
	{
		readonly ISkillAction[] _actions;
		UnitBase _caster;
		SkillInfo _skillId;
		int _index;
		bool _finished;

		public SkillSequence(params ISkillAction[] actions)
		{
			_actions = actions;
		}

		public void SetContext(UnitBase caster, SkillInfo skillId)
		{
			_caster = caster;
			_skillId = skillId;
		}

		public void OnStart()
		{
			_index = 0;
			startCurrent();
		}

		public bool Tick(float dt)
		{
			if (_finished == true)
			{
				return true;
			}

			if (_actions == null || _index >= _actions.Length)
			{
				_finished = true;
				return true;
			}

			// 현재 액션 완료 시 정리 후 다음 액션으로 진행 — 마지막까지 끝나면 시퀀스 종료
			if (_actions[_index].Tick(dt) == true)
			{
				_actions[_index].OnEnd();
				_index++;
				if (_index >= _actions.Length)
				{
					_finished = true;
					return true;
				}

				startCurrent();
			}

			return false;
		}

		// 정상 종료/취소(넉백·스턴) 공통 — 진행 중이던 액션을 정리 (중복 OnEnd 가드)
		public void OnEnd()
		{
			if (_finished == true)
			{
				return;
			}

			_finished = true;
			if (_actions != null && _index < _actions.Length)
			{
				_actions[_index].OnEnd();
			}
		}

		void startCurrent()
		{
			if (_actions == null || _index >= _actions.Length)
			{
				return;
			}

			_actions[_index].OnStart(_caster, _skillId);
		}
	}
}
