using System.Collections.Generic;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 1회 입장(= 단계 하나)의 런 상태. 진입 시 Reset, 종료 시 소멸(서버 미저장).
	//
	// 로그라이트 요소(임시재화·스테이지 선택지)를 걷어내고 부활 횟수와 누적 보상만 남긴다.
	public sealed class DungeonRunState : Singleton<DungeonRunState>
	{
		// 이번 입장에서 사용한 부활 횟수. 입장 단위로 초기화된다(일일 카운터가 아니다).
		private int _reviveUsedCount;

		// 이번 입장에서 확정된 보상 (결과창 표시용). 실제 지급은 서버가 한다.
		private readonly List<GrantedRewardDto> _rewards = new List<GrantedRewardDto>();

		private DungeonRunState()
		{
		}

		public int ReviveUsedCount => _reviveUsedCount;
		public IReadOnlyList<GrantedRewardDto> Rewards => _rewards;

		public void IncrementReviveUsed()
		{
			_reviveUsedCount++;
		}

		public void AddRewards(IReadOnlyList<GrantedRewardDto> rewards)
		{
			if (rewards == null)
			{
				return;
			}

			for (int i = 0; i < rewards.Count; i++)
			{
				_rewards.Add(rewards[i]);
			}
		}

		public void Reset()
		{
			_reviveUsedCount = 0;
			_rewards.Clear();
		}
	}
}
