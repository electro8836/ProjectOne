using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Unit;
using ProjectOne.Skill;
using ProjectOne.Audio;
using ProjectOne.Map;

namespace ProjectOne.Projectile
{
	// 발사체 — 이동 방식은 같은 GameObject 의 궤적 컴포넌트(ITrajectory)에 위임한다(직선/유도/포물선).
	// 적에 적중하면 발동 스킬의 hitEffect 를 적용한다. 속도/수명/사거리는 프리팹(이 컴포넌트)이 소유한다.
	public class Projectile : MonoBehaviour, IPoolable
	{
		// 이동 파라미터는 프리팹이 소유 (발사 측이 아니라 발사체 종류가 결정)
		[SerializeField] private float _speed = 10f;
		[SerializeField] private float _lifeTime = 3f;
		[SerializeField] private float _maxDistance = 0f;   // 0이면 거리 제한 없음
		// 스프라이트가 향하는 기준축(도): +X=0, +Y(위쪽)=90. 이동 방향으로 회전할 때 이 값만큼 보정한다.
		[SerializeField] private float _spriteForwardAngle = 90f;

		private PoolBase<Projectile> _ownerPool;
		private ProjectileData _data;
		private Coroutine _lifeCoroutine;
		// 수명 만료와 충돌이 같은 프레임에 발생할 때 이중 반환 방지
		private bool _isReleased;
		private float _traveledDistance;

		// 이동 방식 위임 — Awake 에서 GetComponent 로 캐시. 궤적별 전용 분기를 위해 Homing/Parabolic 캐스팅도 캐시.
		private ITrajectory _trajectory;
		private Trajectory_Homing _homing;
		private Trajectory_Parabolic _parabolic;

		// 적중 효과 적용용 재사용 버퍼 (할당 방지)
		private readonly List<UnitBase> _hitBuffer = new List<UnitBase>(1);

		private void Awake()
		{
			_trajectory = GetComponent<ITrajectory>();
			if (_trajectory == null)
			{
				// 궤적 미부착 프리팹은 직진으로 폴백 — 프리팹에 명시적으로 궤적 컴포넌트를 부착해야 한다.
				Debug.LogError($"[Projectile] 궤적 컴포넌트(ITrajectory) 없음 — Direct 로 폴백: {name}");
				_trajectory = gameObject.AddComponent<Trajectory_Direct>();
			}

			_homing = _trajectory as Trajectory_Homing;
			_parabolic = _trajectory as Trajectory_Parabolic;
		}

		private void Update()
		{
			if (_trajectory == null)
			{
				return;
			}

			Vector3 pos = transform.position;
			Vector2 facing;
			float step = _trajectory.Tick(Time.deltaTime, ref pos, out facing);
			transform.position = pos;
			_traveledDistance += step;

			// 발사체 차단 타일(벽)에 진입하면 소멸 — 직선/유도/포물선 모두 동일. 소멸 연출은 returnToPool 에서 출력.
			if (MapManager.HasInstance == true && MapManager.Instance.IsProjectileBlocked(pos) == true)
			{
				returnToPool("blocked");
				return;
			}

			// 이동 방향으로 머리를 향하게 — 스프라이트 기준축(_spriteForwardAngle)만큼 보정
			if (facing.sqrMagnitude > 1E-06f)
			{
				float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - _spriteForwardAngle;
				transform.rotation = Quaternion.Euler(0f, 0f, angle);
			}

			// 궤적 종료(유도=타겟 소실 후 마지막 위치 / 포물선=타겟 지점 도달) — 그 지점에서 피격판정 후 소멸
			if (_trajectory.IsFinished == true)
			{
				applyHitOnFinish();
				returnToPool("finish");
				return;
			}

			if (_maxDistance > 0f && _traveledDistance >= _maxDistance)
			{
				returnToPool("maxdist");
			}
		}

		// 궤적 종료 지점 피격판정. hitRadius>0 이면 그 위치 원형 범위에, 아니면 잠금된 타겟에 단일 적용.
		// - 포물선 착지: 타겟 생존 → 단일(또는 AoE)
		// - 유도 종료(타겟 소실): 타겟 사망/없음 → 단일 폴백 무효 → AoE(hitRadius>0)일 때만 적용
		private void applyHitOnFinish()
		{
			if (_data.caster == null)
			{
				return;
			}

			if (_data.hitRadius > 0f)
			{
				SkillEffectApplier.ApplyAtPosition(_data.hitEffect, _data.caster, _data.skillId, transform.position, _data.hitRadius);
				return;
			}

			if (_data.target != null && _data.target.IsDead == false)
			{
				_hitBuffer.Clear();
				_hitBuffer.Add(_data.target);
				SkillEffectApplier.Apply(_data.hitEffect, _data.caster, _data.skillId, _hitBuffer);
			}
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			// 포물선은 비행 중 접촉 판정을 하지 않는다 — 타겟 지점 도달(IsFinished) 시에만 판정한다.
			if (_parabolic != null)
			{
				return;
			}

			UnitBase unit = other.GetComponentInParent<UnitBase>();
			if (unit != null)
			{
				// 유도 발사체는 비행 중 타겟만 판정 — 타겟 외 유닛은 통과, 타겟 소실 비행 중엔 모두 통과(AoE는 종료 시 처리)
				if (_homing != null)
				{
					if (_homing.IsTargetLost == true || unit != _homing.Target)
					{
						return;
					}
				}

				// 적이 아니면(아군/캐스터 자신/사망) 무시하고 통과
				if (_data.caster == null || unit.IsDead == true || TargetResolver.IsEnemy(_data.caster.Faction, unit.Faction) == false)
				{
					return;
				}

				// 적중 — 범위공격(hitRadius>0)이면 적중 위치 원형 범위에, 아니면 적중 대상 단일에 효과 적용 후 반환
				if (_data.hitRadius > 0f)
				{
					SkillEffectApplier.ApplyAtPosition(_data.hitEffect, _data.caster, _data.skillId, transform.position, _data.hitRadius);
				}
				else
				{
					_hitBuffer.Clear();
					_hitBuffer.Add(unit);
					SkillEffectApplier.Apply(_data.hitEffect, _data.caster, _data.skillId, _hitBuffer);
				}

				returnToPool("hit");
				return;
			}

			// 비유닛(다른 발사체/벽 등)은 통과 — 발사체끼리 충돌로 사라지지 않게 한다.
			// 발사체는 _lifeTime/_maxDistance 로 정리. (벽 차단이 필요하면 발사체 전용 레이어로 후속 처리)
		}

		// PoolBase 의 Spawn()이 GetFromPool() → Initialize() → OnActivate() 순서로 호출
		public void Initialize(ProjectileData data, PoolBase<Projectile> pool)
		{
			_data = data;
			_ownerPool = pool;
			_isReleased = false;
			_traveledDistance = 0f;
			transform.position = data.startPos;

			// 이동 방식 초기화 — 회전은 Update 에서 궤적이 제공하는 facing 으로 매 프레임 갱신
			if (_trajectory != null)
			{
				_trajectory.OnLaunch(data, _speed, _maxDistance, _lifeTime);
			}
		}

		public void OnActivate()
		{
			// _lifeTime 0 이하 = 수명 무한 (코루틴 미시작). maxDistance 0 = 사거리 무한.
			if (_lifeTime > 0f)
			{
				_lifeCoroutine = StartCoroutine(lifetimeRoutine());
			}
		}

		public void OnDeactivate()
		{
			if (_lifeCoroutine != null)
			{
				StopCoroutine(_lifeCoroutine);
				_lifeCoroutine = null;
			}

			_data = default;
		}

		private void returnToPool(string reason)
		{
			if (_isReleased)
			{
				return;
			}

			_isReleased = true;

			// 적중 외(수명/사거리 만료)로 사라질 때 소멸 연출 — 효과 행에서 받은 VFX/SFX 출력.
			// 적중은 hitEffect 의 EffectVFX 가 대상에 출력하므로 여기선 내지 않는다.
			if (reason != "hit")
			{
				if (string.IsNullOrEmpty(_data.expireVFX) == false)
				{
					VFXManager.Instance.PlayOneShot(_data.expireVFX, transform.position);
				}

				if (string.IsNullOrEmpty(_data.expireSFX) == false)
				{
					AudioManager.Instance.PlaySFX(_data.expireSFX);
				}
			}

			_ownerPool.Release(this);
		}

		private IEnumerator lifetimeRoutine()
		{
			yield return new WaitForSeconds(_lifeTime);
			returnToPool("lifetime");
		}
	}
}
