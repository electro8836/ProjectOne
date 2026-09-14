using UnityEngine;
using ProjectOne.Skill;
using ProjectOne.Summons;

namespace ProjectOne.Unit.AI
{
	// 궤도형 소환물 — 주인에게 매달려 일정 반경으로 공전하며, 사거리에 든 적을 그 자리에서 공격한다 (설계 2.11).
	//
	// 주인을 쫓아가지 않는다. SummonUnit 이 스폰 때 주인 루트의 자식으로 붙여 두고, 여기서는 localPosition 만
	// 굴린다. 월드 추격으로 만들면 주인이 움직일 때마다 뒤처져 반경이 흔들리고, 이속 스탯·벽 충돌에도 끌려다닌다.
	//
	// 무버는 계속 멈춰 둔다. UnitMover.ApplyMovement 는 현재 transform.position 이 아니라 프레임 시작에 캐시된
	// CachedPos 를 기준으로 위치를 다시 쓰기 때문에, 속도가 조금이라도 남으면 부모를 따라 움직인 위치가
	// 캐시 시점으로 되감겨 심하게 떨린다. 속도가 0이면 ApplyMovement 자체가 호출되지 않는다.
	public sealed class SummonOrbitBehavior : IAiBehavior
	{
		// 공전 각속도(라디안/초)
		private const float AngularSpeed = 2.0f;

		private const float TwoPi = Mathf.PI * 2f;

		// 각도를 누적해 들고 있지 않고 경과 시간에서 매번 구한다.
		//
		// 개체마다 각도를 누적하면 360/N 간격을 유지할 수 없다 — 소환 시각이 다르고 dt 누적 오차도
		// 제각각이라 처음에 맞춰 놓아도 위상이 서서히 어긋난다. Time.time 은 모두가 같은 값을 보므로
		// 공통 위상이 공짜로 얻어지고, 여기에 자기 번호만큼 오프셋을 주면 간격이 항상 정확하다.

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			UnitBase owner = bb.SummonOwner;
			if (owner == null)
			{
				self.Mover.Stop();
				return;
			}

			// 이전 스폰에서 남은 속도까지 확실히 묶는다 — 잔여 속도는 곧 위치 되감기다.
			self.Mover.Stop();

			// 앵커는 고정 좌표가 아니라 주인이다 — 매 틱 갱신해야 리쉬가 의미를 갖는다.
			SummonAiCommon.SyncAnchorToOwner(bb);

			// 타겟을 따로 잡아 두지 않는다. AcquireTarget 은 한 번 문 타겟을 죽을 때까지 거리와 무관하게
			// 유지하는데, 탐지 범위(8)가 빔 사거리보다 넓어서 멀리 있는 원거리 몬스터에 묶이면
			// 사거리 안의 다른 적을 두고도 영영 쏘지 못한다.
			//
			// Select 는 bb.Target 을 보지 않고 HasEnemyInRange 로 매번 새로 스캔하므로 그냥 맡기면 된다.
			// 사거리 안에 적이 없으면 시전도 쿨타임 소모도 일어나지 않는다.
			//
			// 판단 주기를 두지 않고 매 프레임 부르는 이유 — 주기를 두면 그 주기가 쿨타임과 간섭해
			// 발사 간격이 쿨타임과 그 두 배 사이를 오간다(쿨이 판단 직후에 풀리면 한 주기를 더 기다린다).
			// 비용은 문제되지 않는다. 쿨다운 중에는 CanCastNow 가 Normal 에서, TryBasicAttack 이
			// IsOnCooldown 에서 각각 조기 반환하므로 O(N) 탐색은 쿨이 풀린 프레임에만 돈다.
			SkillSelector.Select(self, false);

			// 형제 중 내 번호와 총수 — 360/총수 간격으로 자리를 잡는다.
			// 매 틱 물어야 한다. 하나가 사라지거나 늘면 그 즉시 남은 개체들이 새 간격으로 다시 벌어져야 한다.
			int index = 0;
			int total = 1;
			SummonUnit summon = self as SummonUnit;
			if (summon != null && SummonManager.HasInstance == true)
			{
				int found = SummonManager.Instance.GetIndexOf(summon, out total);
				if (found < 0 || total <= 0)
				{
					index = 0;
					total = 1;
				}
				else
				{
					index = found;
				}
			}

			float angle = Time.time * AngularSpeed + index * (TwoPi / total);

			// 주인의 콜라이더 오프셋을 더해야 궤도가 몸통 높이에 걸린다 — 빼면 발밑으로 내려앉는다.
			float radius = bb.FollowDistance;
			Vector3 orbit = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
			self.transform.localPosition = (Vector3)owner.ColliderOffset + orbit;
		}
	}
}
