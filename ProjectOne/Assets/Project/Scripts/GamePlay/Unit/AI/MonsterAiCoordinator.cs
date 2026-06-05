using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Map;
using ProjectOne.Utils;

namespace ProjectOne.Unit.AI
{
	// 몬스터 길찾기용 플로우필드 중앙 재베이크 전담 (MonoSingleton).
	// 살아있는 첫 히어로 위치로 일정 주기 1회만 베이크 — 몬스터 개별 베이크 금지.
	public sealed class MonsterAiCoordinator : MonoSingleton<MonsterAiCoordinator>
	{
		private const float Interval = 0.3f;

		private float _accum;

		private void Update()
		{
			if (TilemapGrid.Instance == null)
			{
				return;
			}

			_accum += Time.deltaTime;
			if (_accum < Interval)
			{
				return;
			}

			_accum = 0f;

			UnitBase hero = FindFirstAliveHero();
			if (hero != null)
			{
				TilemapGrid.Instance.BakeFlowField(hero.transform.position);
			}
		}

		private static UnitBase FindFirstAliveHero()
		{
			if (UnitContainer.Instance == null)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h != null && h.IsDead == false)
				{
					return h;
				}
			}

			return null;
		}
	}
}
