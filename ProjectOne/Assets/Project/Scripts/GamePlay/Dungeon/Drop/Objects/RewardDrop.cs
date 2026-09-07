using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Reward;

namespace ProjectOne.Dungeon
{
	// 몬스터 처치 보상을 실어 바닥에 떨어지는 드랍. 세 종류 중 유일하게 자석 흡입 대상이다.
	//
	// **지급은 접촉이 아니라 획득 범위 진입 시점**이다 — 끌려오는 구간은 이미 획득이 확정된
	// 뒤의 연출이다 (사용자 결정).
	public class RewardDrop : DropObject
	{
		[Header("자석 흡입")]
		// 흡입 시작 속도(유닛/초)
		[SerializeField] private float _homingStartSpeed = 2f;
		// 흡입 가속도(유닛/초²) — 히어로에 가까워질수록 빨라지는 느낌
		[SerializeField] private float _homingAccel = 40f;
		// 흡입 최대 속도(유닛/초)
		[SerializeField] private float _homingMaxSpeed = 20f;

		// 이동용 Kinematic Rigidbody2D (static 콜라이더 이동 시 충돌 트리 재빌드 회피)
		private Rigidbody2D _rb;
		// 현재 흡입 속도 (스폰마다 _homingStartSpeed 로 리셋)
		private float _homingSpeed;
		// 이 오브젝트가 운반 중인 보상
		private readonly List<GrantedReward> _payload = new List<GrantedReward>(2);
		// 보상 지급 완료 여부 — 범위 진입과 접촉 양쪽에서 불려도 한 번만 지급한다
		private bool _isClaimed;

		private void Awake()
		{
			_rb = this.GetComponent<Rigidbody2D>();
		}

		public override void Initialize(DropObjectPool pool)
		{
			base.Initialize(pool);
			_homingSpeed = _homingStartSpeed;
			_isClaimed = false;
			_payload.Clear();
		}

		// 이 드랍이 운반할 보상을 설정한다. Initialize 직후 DropManager 가 호출한다.
		public void SetPayload(List<GrantedReward> rewards)
		{
			_payload.Clear();
			if (rewards == null)
			{
				return;
			}

			for (int i = 0; i < rewards.Count; i++)
			{
				_payload.Add(rewards[i]);
			}
		}

		// HeroMagnet 센서가 물리 프레임마다 호출 — 히어로 중심으로 가속 이동.
		// 최종 회수는 베이스의 OnTriggerEnter2D 가 처리한다.
		public void MagnetTick(Vector2 targetCenter)
		{
			if (IsReleased == true)
			{
				return;
			}

			// 획득 범위에 들어온 순간이 지급 시점이다 — 끌려오는 동안 죽거나 씬이 바뀌어도 이미 받은 것이다.
			claim();

			_homingSpeed = Mathf.Min(_homingSpeed + _homingAccel * Time.fixedDeltaTime, _homingMaxSpeed);
			Vector2 next = Vector2.MoveTowards(_rb.position, targetCenter, _homingSpeed * Time.fixedDeltaTime);
			_rb.MovePosition(next);
		}

		protected override void OnPickup(UnitBase hero)
		{
			// 자석 범위를 거치지 않고 곧바로 부딪힌 경우를 위한 보루 — claim 은 멱등이다.
			claim();
		}

		// 운반 중인 보상을 실제로 인벤/지갑에 반영한다. 두 번 불려도 한 번만 지급된다.
		private void claim()
		{
			if (_isClaimed == true)
			{
				return;
			}

			_isClaimed = true;
			if (_payload.Count == 0)
			{
				return;
			}

			RewardGranter.ApplyAll(_payload);
			_payload.Clear();
		}
	}
}
