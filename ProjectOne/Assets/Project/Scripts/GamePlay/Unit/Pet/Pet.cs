using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Dungeon;

namespace ProjectOne.Unit
{
	// 캐릭터를 따라다니며 바닥의 보상 드랍을 대신 주워 주는 자석 펫.
	//
	// **UnitBase 가 아니다.** UnitManager 에 등록되지 않고 콜라이더도 없다 —
	// 그래서 타겟팅·피격·유닛 충돌에서 자연히 제외된다(무적). 애니/정렬은 UnitAnimator 를 붙여 재사용한다.
	//
	// 갱신은 히어로와 같은 리듬으로 나눈다 — 이동/흡입은 FixedUpdate(UnitMover.ManualFixedTick,
	// HeroMagnet.OnTriggerStay2D 와 같은 물리 스텝), 표현(애니·정렬)은 Update.
	public class Pet : MonoBehaviour
	{
		// 드랍 오브젝트가 올라가 있는 레이어 이름
		private const string DropLayerName = "Drop";

		// 도착 판정 여유값. 부동소수 오차만큼 남은 거리를 "아직 안 왔다"고 보면
		// 매 틱 눈에 보이지 않을 만큼씩 움직이며 이동 애니가 영원히 재생된다.
		private const float ArriveEpsilon = 0.01f;

		[Header("추종")]
		// 순간이동·최초 배치 때의 착지 지점(캐릭터 기준 오프셋). 평상시 추종에는 쓰지 않는다.
		[SerializeField] private Vector2 _teleportOffset = new Vector2(-0.6f, -0.2f);
		// 캐릭터와 이 거리까지 좁혀지면 멈춘다
		[SerializeField] private float _followStopDistance = 1f;
		// 이보다 벌어져야 추격을 시작한다 — 둘 사이가 히스테리시스 구간이라 캐릭터를 졸졸 붙어다니지 않는다
		[SerializeField] private float _followStartDistance = 2f;
		// 이 거리까지 벌어지면 2배속 — 1배(_followStartDistance)와의 사이는 선형 보간.
		// _followStartDistance 보다 커야 한다(작으면 추격 내내 2배로 고정돼 버린다 — 아래에서 강제 보정).
		[SerializeField] private float _speedMaxDistance = 4f;
		// 캐릭터와 이보다 멀어지면 따라잡기를 포기하고 옆으로 순간이동한다
		[SerializeField] private float _teleportDistance = 8f;

		[Header("드랍 획득")]
		// **캐릭터 기준** 드랍 탐지 반경 — 캐릭터 스탯과 무관한 펫 고유 고정값
		[SerializeField] private float _detectRange = 3.5f;
		// **펫 기준** 획득 반경 — 여기 들어온 드랍은 그 순간 펫이 가져간다
		[SerializeField] private float _pickupRange = 1.5f;
		// 드랍을 주우러 갈 때의 속도 배율
		[SerializeField] private float _chaseSpeedMul = 2f;
		// 끌려온 드랍이 이 거리까지 오면 회수한다
		[SerializeField] private float _consumeDistance = 0.12f;
		// 드랍이 빨려드는 지점 — 발밑(루트)이 아니라 몸통이어야 자연스럽다
		[SerializeField] private Vector2 _bodyCenter = new Vector2(0f, 0.3f);

		private UnitAnimator _animator;
		private UnitBase _owner;

		// 드랍 탐지용 재사용 버퍼
		private readonly Collider2D[] _hits = new Collider2D[16];
		private ContactFilter2D _dropFilter;

		// 펫이 물어 끌고 오는 중인 드랍.
		// 캐릭터가 멀어져 탐지 원을 벗어나도 끝까지 빨려들어와야 하므로 탐지 결과와 별개로 들고 있는다.
		private readonly List<RewardDrop> _pulling = new List<RewardDrop>(8);

		// 이번 물리 스텝에 실제로 움직였는지 / 그때의 방향 — Update 의 애니 갱신이 읽는다
		private bool _isMoving;
		private Vector2 _moveDir;

		// 지금 캐릭터를 쫓는 중인지. _followStartDistance 를 넘어야 켜지고 _followStopDistance 에서 꺼진다.
		private bool _isFollowing;

		private void Awake()
		{
			_animator = this.GetComponent<UnitAnimator>();

			_dropFilter = new ContactFilter2D();
			_dropFilter.useTriggers = true;
			_dropFilter.SetLayerMask(LayerMask.GetMask(DropLayerName));
		}

		// 주인 설정 — 스폰 직후 1회. 펫은 주인을 따라다닐 뿐 주인에게 아무 영향도 주지 않는다.
		public void SetOwner(UnitBase owner)
		{
			_owner = owner;
			_isFollowing = false;
			if (_owner != null)
			{
				transform.position = anchorPosition();
			}
		}

		private void FixedUpdate()
		{
			_isMoving = false;

			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			// 너무 벌어지면 따라잡기를 포기하고 옆으로 붙는다
			if (Vector2.Distance(transform.position, _owner.transform.position) > _teleportDistance)
			{
				transform.position = anchorPosition();
			}

			Vector2 petCenter = (Vector2)transform.position + _bodyCenter;

			RewardDrop target = scanDrops(petCenter);
			tickPulling(petCenter);
			move(target);
		}

		private void Update()
		{
			if (_animator == null)
			{
				return;
			}

			_animator.SetMoving(_isMoving);
			if (_isMoving == true)
			{
				// UnitAnimator 의 플립 규칙은 "원본 스프라이트가 좌향"(+1 = 왼쪽)을 전제한다.
				// 고양이 스프라이트는 우향이라 그대로 넘기면 좌우가 뒤집힌다 — x 부호를 뒤집어 넘긴다.
				_animator.SetFacing(new Vector2(-_moveDir.x, _moveDir.y));
			}

			_animator.UpdateSorting();
		}

		// 캐릭터 주변 _detectRange 안의 보상 드랍을 훑는다.
		// 펫 획득 반경 안이면 그 자리에서 물고(_pulling), 밖이면 가장 가까운 하나를 이동 목표로 돌려준다.
		private RewardDrop scanDrops(Vector2 petCenter)
		{
			int count = Physics2D.OverlapCircle(_owner.HitCenter, _detectRange, _dropFilter, _hits);

			RewardDrop nearest = null;
			float nearestSqr = float.MaxValue;

			for (int i = 0; i < count; i++)
			{
				RewardDrop drop = _hits[i].GetComponent<RewardDrop>();

				// 회복 오브·버프룬은 흡입 대상이 아니다. 이미 누군가 가져간 드랍도 건너뛴다 —
				// 히어로 자석이 먼저 물었거나(IsClaimed), 펫이 이미 물어 끌고 오는 중이다.
				if (drop == null || drop.IsClaimed == true)
				{
					continue;
				}

				float sqr = ((Vector2)drop.transform.position - petCenter).sqrMagnitude;
				if (sqr <= _pickupRange * _pickupRange)
				{
					_pulling.Add(drop);		// 획득 확정 — 실제 지급은 이어지는 PetMagnetTick 이 한다
					continue;
				}

				if (sqr < nearestSqr)
				{
					nearestSqr = sqr;
					nearest = drop;
				}
			}

			return nearest;
		}

		// 물어 둔 드랍을 펫 쪽으로 끌어당기고, 다 온 것은 회수한다.
		private void tickPulling(Vector2 petCenter)
		{
			for (int i = _pulling.Count - 1; i >= 0; i--)
			{
				RewardDrop drop = _pulling[i];

				// 스테이지 정리·수명 만료로 풀에 돌아갔으면 목록에서 뺀다
				if (drop == null || drop.gameObject.activeInHierarchy == false)
				{
					_pulling.RemoveAt(i);
					continue;
				}

				drop.PetMagnetTick(petCenter);

				if (((Vector2)drop.transform.position - petCenter).sqrMagnitude > _consumeDistance * _consumeDistance)
				{
					continue;
				}

				drop.PickupByPet();
				_pulling.RemoveAt(i);
			}
		}

		// 드랍이 있으면 그쪽으로 2배속, 없으면 캐릭터를 느슨하게 뒤따른다.
		private void move(RewardDrop target)
		{
			if (target != null)
			{
				// 드랍을 주우러 가는 동안은 추종 상태를 접어 둔다 — 다 줍고 나면 다시 벌어진 뒤부터 따라간다
				_isFollowing = false;
				step(target.transform.position, _owner.MoveSpeed * _chaseSpeedMul, 0f);
				return;
			}

			Vector2 ownerPos = _owner.transform.position;
			float distance = Vector2.Distance(transform.position, ownerPos);

			// 히스테리시스 — 충분히 벌어져야 출발하고, 주변까지 좁히면 선다.
			// 덕분에 캐릭터가 움직이기 시작해도 곧바로 같이 움직이지 않고 한 박자 뒤에서 따라온다.
			if (_isFollowing == false && distance > _followStartDistance)
			{
				_isFollowing = true;
			}
			else if (_isFollowing == true && distance <= _followStopDistance + ArriveEpsilon)
			{
				_isFollowing = false;
			}

			if (_isFollowing == false)
			{
				return;
			}

			// 추격을 시작하는 거리에서는 캐릭터와 정확히 같은 속도(1배)로 따라붙는다 —
			// 그래야 간격이 줄지 않아 뒤에서 따라오는 모양이 유지된다. 더 벌어질수록 최대 2배까지 올린다.
			float maxDistance = Mathf.Max(_speedMaxDistance, _followStartDistance + 0.01f);
			float mul = Mathf.Lerp(1f, 2f, Mathf.InverseLerp(_followStartDistance, maxDistance, distance));

			// 목적지는 캐릭터 본체다 — 정해진 옆자리가 없으므로 접근해 온 방향 그대로 멈춘다
			step(ownerPos, _owner.MoveSpeed * mul, _followStopDistance);
		}

		// 목적지 쪽으로 한 스텝 이동한다. keepDistance 만큼은 남겨 두고 멈춘다.
		private void step(Vector2 destination, float speed, float keepDistance)
		{
			Vector2 toDestination = destination - (Vector2)transform.position;
			float remain = toDestination.magnitude - keepDistance;
			if (remain <= ArriveEpsilon)
			{
				return;
			}

			_moveDir = toDestination.normalized;
			_isMoving = true;

			// 벽·유닛 충돌은 계산하지 않는다 — 펫은 전부 통과한다
			float delta = Mathf.Min(speed * Time.fixedDeltaTime, remain);
			transform.position = (Vector2)transform.position + _moveDir * delta;
		}

		// 순간이동·최초 배치 때의 착지 지점(월드). 루트끼리 맞추므로 발밑 기준이다.
		private Vector2 anchorPosition()
		{
			return (Vector2)_owner.transform.position + _teleportOffset;
		}
	}
}
