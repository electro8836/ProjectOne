using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Consumables
{
	// 소모품 쿨다운 (설계 4.3).
	//
	// 하급·상급 물약이 같은 CooldownGroup 을 쓰면 번갈아 연타할 수 없다.
	// 방치형이라 이 통제가 없으면 물약 스택이 곧 무한 생존이 된다.
	//
	// 저장하지 않는다 — 런타임 전용이다. 재접속하면 쿨다운이 풀린다.
	// 틱이 필요 없어 Update 도 코루틴도 두지 않는다. Time.time 과 비교하면 그만이다.
	public static class ConsumableCooldown
	{
		// 키 → 사용 가능해지는 시각(Time.time 기준)
		private static readonly Dictionary<int, float> _readyAt = new Dictionary<int, float>();

		public static bool IsReady(int itemId, int cooldownGroup)
		{
			return GetRemaining(itemId, cooldownGroup) <= 0f;
		}

		public static float GetRemaining(int itemId, int cooldownGroup)
		{
			float ready;
			if (_readyAt.TryGetValue(makeKey(itemId, cooldownGroup), out ready) == false)
			{
				return 0f;
			}

			float remaining = ready - Time.time;
			return (remaining > 0f) ? remaining : 0f;
		}

		public static void Begin(int itemId, int cooldownGroup, float cooldown)
		{
			if (cooldown <= 0f)
			{
				return;
			}

			_readyAt[makeKey(itemId, cooldownGroup)] = Time.time + cooldown;
		}

		// 씬 전환은 쿨다운을 풀지 않는다. 개발/테스트용 초기화 경로만 열어 둔다.
		public static void ClearAll()
		{
			_readyAt.Clear();
		}

		// CooldownGroup 이 None 이면 그 아이템 하나만 쿨다운을 갖는다 (설계 4.3).
		// 아이템 ID 는 양수이므로 음수로 뒤집어 그룹 키와 네임스페이스를 가른다.
		private static int makeKey(int itemId, int cooldownGroup)
		{
			return (cooldownGroup > 0) ? cooldownGroup : -itemId;
		}
	}
}
