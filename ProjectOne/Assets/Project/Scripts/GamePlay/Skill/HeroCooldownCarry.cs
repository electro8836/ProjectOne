using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 씬 전환을 건너 히어로 쿨타임을 이어 준다.
	//
	// 마을·필드·던전은 씬을 새로 띄우며 히어로도 새로 만들기 때문에, 그냥 두면 이동할 때마다 쿨타임이 초기화된다.
	// 마을·필드를 떠날 때 남은 쿨타임을 보관하고, 다음 히어로가 만들어지면 그동안 흐른 시간만큼 빼고 이어받는다.
	// 던전 입장은 초기화가 맞으므로 입장 시 보관분을 버린다.
	//
	// 흐른 시간은 실제 시간(realtimeSinceStartup)으로 잰다 — 로딩 동안에도 쿨타임은 돈다.
	public static class HeroCooldownCarry
	{
		private static readonly Dictionary<EDT.Skill, float> _remaining = new Dictionary<EDT.Skill, float>();
		private static float _capturedAt;
		private static bool _hasData;

		// 지금 히어로의 남은 쿨타임을 보관한다. 히어로가 없으면 보관분을 비운다.
		public static void Capture()
		{
			Clear();
			if (UnitManager.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			if (heroes.Count == 0 || heroes[0] == null || heroes[0].SkillContainer == null)
			{
				return;
			}

			heroes[0].SkillContainer.ExportCooldowns(_remaining);
			_capturedAt = Time.realtimeSinceStartup;
			_hasData = _remaining.Count > 0;
		}

		// 새로 만든 히어로에 보관한 쿨타임을 이어 준다. 한 번 쓰면 비운다.
		public static void Restore(SkillContainer container)
		{
			if (_hasData == false || container == null)
			{
				return;
			}

			container.ImportCooldowns(_remaining, Time.realtimeSinceStartup - _capturedAt);
			Clear();
		}

		public static void Clear()
		{
			_remaining.Clear();
			_hasData = false;
		}
	}
}
