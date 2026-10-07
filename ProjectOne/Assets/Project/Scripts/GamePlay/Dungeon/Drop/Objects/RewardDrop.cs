using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.Unit;
using ProjectOne.Reward;
using ProjectOne.UserData;

namespace ProjectOne.Dungeon
{
	// 몬스터 처치 보상을 실어 바닥에 떨어지는 드랍. 세 종류 중 유일하게 자석 흡입 대상이다.
	//
	// **지급은 접촉이 아니라 획득 범위 진입 시점**이다 — 끌려오는 구간은 이미 획득이 확정된
	// 뒤의 연출이다 (사용자 결정).
	//
	// 흡입 주체는 둘이다 — 히어로 자석(HeroMagnet)과 자석 펫(Pet). **먼저 범위에 넣은 쪽이
	// 끝까지 가져간다** — 연출 목표가 도중에 바뀌지 않도록 펫이 잡은 드랍은 히어로 자석이 건너뛴다.
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
		// 펫이 끌고 가는 중 — 히어로 자석이 같은 드랍을 다시 당기는 것을 막는다
		private bool _isPetOwned;
		// 필드 배치 정산 좌표 — 이 드랍이 몇 번째 처치의 몇 번째 보상인가. -1 이면 정산 대상 아님(오프라인)
		private int _killIndex = -1;
		private int _rewardIndex;
		// 던전 상자 정산 좌표 — 미궁 상자에서 떨어진 드랍이면 상자 인덱스. -1 이면 상자 드랍이 아니다
		private int _chestIndex = -1;

		// 펫이 이미 누군가 가져간 드랍을 타겟으로 잡지 않도록 공개한다.
		public bool IsClaimed { get { return _isClaimed; } }

		// 펫이 같은 드랍을 흡입 목록에 두 번 넣지 않도록 공개한다.
		public bool IsPetOwned { get { return _isPetOwned; } }

		// 인벤토리가 가득 차 지금은 주울 수 없는가 — 장비를 실은 드랍만 해당한다(골드·재료는 칸을 쓰지 않는다).
		// 이미 획득이 확정돼 끌려오는 중이면 막지 않는다. 칸이 비면 다시 주울 수 있다.
		public bool IsPickupBlocked
		{
			get
			{
				if (_isClaimed == true || Account.Instance.Inventory.IsInventoryFull == false)
				{
					return false;
				}

				for (int i = 0; i < _payload.Count; i++)
				{
					if (_payload[i].equipment != null)
					{
						return true;
					}
				}

				return false;
			}
		}

		private void Awake()
		{
			_rb = this.GetComponent<Rigidbody2D>();
		}

		public override void Initialize(DropObjectPool pool)
		{
			base.Initialize(pool);
			_homingSpeed = _homingStartSpeed;
			_isClaimed = false;
			_isPetOwned = false;
			_killIndex = -1;
			_chestIndex = -1;
			_rewardIndex = 0;
			_payload.Clear();
		}

		// 원장 좌표를 단다. SetPayload 직후 DropManager 가 호출한다.
		public void SetLedgerTag(int killIndex, int rewardIndex)
		{
			_killIndex = killIndex;
			_rewardIndex = rewardIndex;
		}

		// 상자 원장 좌표를 단다(미궁 상자 드랍). 주우면 DungeonRunLedger 에 주운 비트가 남는다.
		public void SetChestTag(int chestIndex, int rewardIndex)
		{
			_chestIndex = chestIndex;
			_rewardIndex = rewardIndex;
		}

		// 이미 주웠거나 수명이 다해 풀로 돌아갔는가 — DropManager 가 추적 목록을 정리할 때 본다.
		public bool IsGone
		{
			get { return IsReleased; }
		}

		// 던전 종료 시 바닥에 남은 드랍을 줍지 않은 채 지운다 — 반환 콜백이 처치 보상을 미획득으로 확정한다.
		public void Discard()
		{
			if (IsReleased == false)
			{
				ReleaseSelf();
			}
		}

		// 던전 종료 시 바닥에 남은 드랍을 한 번에 획득한다 — 지급·연출·반환을 여기서 끝낸다.
		public void CollectNow()
		{
			if (IsReleased == true)
			{
				return;
			}

			// 인벤토리가 가득 차도 받는다 — 던전 결과는 칸을 넘겨도 지급한다.
			claim();
			PlayPickupFeedback();
			ReleaseSelf();
		}

		// 풀 반환 — 줍지 않고 사라지면(수명 만료) 원장에 미획득으로 남긴다.
		public override void OnDeactivate()
		{
			if (_isClaimed == false && _killIndex >= 0)
			{
				FieldKillLedger.Instance.TryResolve(_killIndex, _rewardIndex, false);
			}
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

			// 펫이 먼저 물어간 드랍은 펫에게 끝까지 맡긴다
			if (_isPetOwned == true)
			{
				return;
			}

			// 인벤토리가 가득 찼으면 끌어오지 않고 알리기만 한다 — 안내 주기는 획득 로그가 조절한다.
			if (IsPickupBlocked == true)
			{
				EventManager.Instance.Publish(new InventoryFullEvent());
				return;
			}

			// 획득 범위에 들어온 순간이 지급 시점이다 — 끌려오는 동안 죽거나 씬이 바뀌어도 이미 받은 것이다.
			claim();
			pullTowards(targetCenter);
		}

		// 펫이 물리 프레임마다 호출 — 펫 중심으로 가속 이동.
		// 히어로 콜라이더에 닿는 일이 없으므로 회수는 펫이 PickupByPet 으로 직접 끝낸다.
		public void PetMagnetTick(Vector2 petCenter)
		{
			if (IsReleased == true)
			{
				return;
			}

			_isPetOwned = true;
			claim();
			pullTowards(petCenter);
		}

		// 펫이 끌어당긴 드랍을 회수한다 — 히어로 접촉 경로(OnTriggerEnter2D)를 거치지 않으므로
		// 지급 보루·픽업 연출·풀 반환을 여기서 직접 끝낸다.
		public void PickupByPet()
		{
			if (IsReleased == true)
			{
				return;
			}

			claim();
			PlayPickupFeedback();
			ReleaseSelf();
		}

		// 펫에게 끌려가는 동안 히어로 몸에 스쳐도 히어로가 가로채지 않는다 —
		// 회수는 펫이 PickupByPet 으로 끝낸다(연출 목표가 도중에 바뀌지 않게).
		protected override bool PickupOnTouch
		{
			get { return _isPetOwned == false && IsPickupBlocked == false; }
		}

		// 자석 범위 없이 곧바로 부딪혔는데 주울 수 없으면 알린다.
		protected override void OnHeroEnter(UnitBase hero)
		{
			if (IsPickupBlocked == true)
			{
				EventManager.Instance.Publish(new InventoryFullEvent());
			}
		}

		protected override void OnPickup(UnitBase hero)
		{
			// 자석 범위를 거치지 않고 곧바로 부딪힌 경우를 위한 보루 — claim 은 멱등이다.
			claim();
		}

		// 목표 좌표로 가속하며 다가간다. 호출자는 전부 물리 프레임(FixedUpdate) 타이밍이다.
		private void pullTowards(Vector2 targetCenter)
		{
			_homingSpeed = Mathf.Min(_homingSpeed + _homingAccel * Time.fixedDeltaTime, _homingMaxSpeed);
			Vector2 next = Vector2.MoveTowards(_rb.position, targetCenter, _homingSpeed * Time.fixedDeltaTime);
			_rb.MovePosition(next);
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

			// 원장이 이미 이 보상을 미획득으로 확정했다면(정산 완료·강제 확정) 로컬만 받으면 서버와 어긋난다 — 지급하지 않는다.
			if (_killIndex >= 0 && FieldKillLedger.Instance.TryResolve(_killIndex, _rewardIndex, true) == false)
			{
				_payload.Clear();
				return;
			}

			if (_chestIndex >= 0)
			{
				DungeonRunLedger.Instance.MarkChestPicked(_chestIndex, _rewardIndex);
			}

			RewardGranter.ApplyAll(_payload, true);

			// 던전 결과창 합산 — 던전 밖(필드)에서는 원장이 무시한다.
			DungeonRunLedger.Instance.RecordPicked(_payload);
			_payload.Clear();
		}
	}
}
