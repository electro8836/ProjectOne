using ProjectOne.Event;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 1회성 던전 런(로그라이트) 상태 홀더. 던전 진입 시 Reset, 종료 시 소멸(서버 미저장).
	// 현재는 던전 임시재화(MagicEssence)만 보유한다. 런 중 구매한 스킬카드 레벨 등
	// 던전 종료 시 사라지는 상태가 늘어나면 이 클래스에 추가한다.
	public sealed class DungeonRunState : Singleton<DungeonRunState>
	{
		// 던전 임시재화 보유량
		private int _essence;

		private DungeonRunState()
		{
		}

		public int EssenceAmount => _essence;

		// 임시재화 획득
		public void AddEssence(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			_essence += amount;
			publishChanged();
		}

		// 임시재화 소비 — 부족하면 false (이번 작업에선 미사용, 스킬카드 구매용 사전 제공)
		public bool TrySpendEssence(int cost)
		{
			if (cost <= 0 || _essence < cost)
			{
				return false;
			}

			_essence -= cost;
			publishChanged();
			return true;
		}

		// 던전 진입 시 0으로 초기화
		public void Reset()
		{
			_essence = 0;
			publishChanged();
		}

		private void publishChanged()
		{
			EventManager.Instance.Publish(new DungeonEssenceChangedEvent(_essence));
		}
	}
}
