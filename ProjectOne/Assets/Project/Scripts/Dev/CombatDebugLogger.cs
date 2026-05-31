using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.Dev
{
	// 개발용 전투 로거 — 빈 GameObject 에 붙여 사용
	// Hero 의 스킬 사용 / Monster 의 피격을 콘솔에 출력 (글로벌 이벤트 구독)
	public class CombatDebugLogger : MonoBehaviour
	{
		void OnEnable()
		{
			EventManager.Instance.Subscribe<SkillCastEvent>(onSkillCast);
			EventManager.Instance.Subscribe<DamageTakenEvent>(onDamageTaken);
		}

		void OnDisable()
		{
			EventManager.Instance.Unsubscribe<SkillCastEvent>(onSkillCast);
			EventManager.Instance.Unsubscribe<DamageTakenEvent>(onDamageTaken);
		}

		void onSkillCast(SkillCastEvent e)
		{
			if (e.Caster == null || e.Caster.GetUnitType() != UnitType.Hero)
			{
				return;
			}

			Debug.Log($"[전투] {e.Caster.name} 스킬 사용 → {e.SkillId}");
		}

		void onDamageTaken(DamageTakenEvent e)
		{
			if (e.Target == null || e.Target.GetUnitType() != UnitType.Monster)
			{
				return;
			}

			string atk = e.Attacker != null ? e.Attacker.name : "?";
			Debug.Log($"[전투] {e.Target.name} 피격 {e.Damage} (공격자:{atk}, 스킬:{(SkillInfo)e.SkillId})");
		}
	}
}
