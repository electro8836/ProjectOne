using EDT;
using ProjectOne.Event;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.HeroPasses
{
	// 히어로패스 경험치 추적 — 게임 이벤트를 HeroPassBook 의 활동 카운터로 옮긴다.
	//
	// QuestTracker 와 같은 골격의 이벤트 구독형 MonoSingleton 이다. 마을·필드·던전을 가로질러 살아 있어야 한다.
	public sealed class HeroPassTracker : MonoSingleton<HeroPassTracker>
	{
		protected override bool Persistent => true;

		protected override void Awake()
		{
			base.Awake();

			EventManager.Instance.Subscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(onDungeonStageCleared);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(onDungeonStageCleared);

			base.OnDestroy();
		}

		// 인스턴스 생성만을 목적으로 하는 호출 지점 — Instance 접근이 곧 생성이라 본문이 필요 없다.
		public void Touch()
		{
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 이벤트에는 등급이 실리지 않는다 — 몬스터 테이블로 역조회한다.
		private void onMonsterKill(MonsterKillEvent e)
		{
			Table_Monster.Row monster = Table_Monster.Get(e.MonsterID);
			if (monster == null)
			{
				return;
			}

			HeroPassExpType type = toExpType(monster.MonsterType);
			if (type == HeroPassExpType.None)
			{
				return;
			}

			Account.Instance.HeroPass.AddCount(type);
		}

		private void onDungeonStageCleared(DungeonStageClearedEvent e)
		{
			Account.Instance.HeroPass.AddCount(HeroPassExpType.DungeonClear);
		}

		private static HeroPassExpType toExpType(MonsterType monsterType)
		{
			switch (monsterType)
			{
				case MonsterType.Normal:
					return HeroPassExpType.MonsterKill_Normal;

				case MonsterType.Elite:
					return HeroPassExpType.MonsterKill_Elite;

				case MonsterType.Boss:
					return HeroPassExpType.MonsterKill_Boss;

				default:
					return HeroPassExpType.None;
			}
		}
	}
}
