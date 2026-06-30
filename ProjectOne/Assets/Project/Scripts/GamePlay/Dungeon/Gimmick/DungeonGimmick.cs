using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 던전 기믹 베이스 — 히어로가 트리거 범위에 들고 날 때를 감지해 파생 클래스에 알린다.
	// 등장/회수 타이밍은 각 StageMode 가, 인스턴스 수명은 GimmickService 가 관리한다.
	// 프리팹 요구사항: isTrigger Collider2D + Kinematic Rigidbody2D.
	public abstract class DungeonGimmick : MonoBehaviour
	{
		private void OnTriggerEnter2D(Collider2D other)
		{
			UnitBase hero = resolveHero(other);
			if (hero == null)
			{
				return;
			}

			OnHeroEnter(hero);
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			UnitBase hero = resolveHero(other);
			if (hero == null)
			{
				return;
			}

			OnHeroExit(hero);
		}

		// 히어로가 트리거에 진입했을 때 — 파생 구현
		protected abstract void OnHeroEnter(UnitBase hero);

		// 히어로가 트리거에서 이탈했을 때 — 파생 구현
		protected abstract void OnHeroExit(UnitBase hero);

		// GimmickService 가 회수(파괴) 직전에 호출한다. 히어로가 트리거 위에 있는 채로
		// 제거될 때는 OnTriggerExit2D 가 불리지 않으므로, 파생이 여기서 상태를 정리한다.
		public virtual void OnDespawn()
		{
		}

		// 히어로만 통과 — 몬스터/투사체 콜라이더는 GetComponentInParent 결과로 자연 제외 (DropObject 와 동일 패턴)
		private static UnitBase resolveHero(Collider2D other)
		{
			UnitBase unit = other.GetComponentInParent<UnitBase>();
			if (unit == null || unit.GetUnitType() != UnitType.Hero || unit.IsDead == true)
			{
				return null;
			}

			return unit;
		}
	}
}
