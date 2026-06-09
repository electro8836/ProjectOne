using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Map;
using ProjectOne.Utils;

namespace ProjectOne.Unit.AI
{
	// 몬스터 길찾기용 플로우필드 중앙 재베이크 전담 (MonoSingleton).
	// 살아있는 첫 히어로가 다른 그리드 셀로 이동할 때만 베이크 — 몬스터 개별 베이크 금지.
	public sealed class MonsterAiCoordinator : MonoSingleton<MonsterAiCoordinator>
	{
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		private void Update()
		{
			if (TilemapGrid.Instance == null)
			{
				return;
			}

			UnitBase hero = FindFirstAliveHero();
			if (hero == null)
			{
				return;
			}

			Vector3Int currentCell = TilemapGrid.Instance.WorldToCell(hero.transform.position);
			if (currentCell == _lastHeroCell)
			{
				return;
			}

			_lastHeroCell = currentCell;
			TilemapGrid.Instance.BakeFlowField(hero.transform.position);
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
