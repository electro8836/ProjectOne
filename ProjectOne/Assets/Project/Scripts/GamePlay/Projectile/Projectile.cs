using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Unit;
using ProjectOne.Skill;
using ProjectOne.Audio;

namespace ProjectOne.Projectile
{
	// 발사체 — 직선 이동(속도/수명/사거리는 프리팹 설정)하다 적에 적중하면 발동 스킬의 hitEffect 를 적용한다.
	// 유도/포물선 등 변형 궤적은 후속(Trajectory 컴포넌트)에서 direction 을 조정하는 방식으로 확장.
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

		// 적중 효과 적용용 재사용 버퍼 (할당 방지)
		private readonly List<UnitBase> _hitBuffer = new List<UnitBase>(1);

		private void Update()
		{
			float step = _speed * Time.deltaTime;
			transform.position += _data.direction * step;
			_traveledDistance += step;

			if (_maxDistance > 0f && _traveledDistance >= _maxDistance)
			{
				returnToPool("maxdist");
			}
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			UnitBase unit = other.GetComponentInParent<UnitBase>();
			if (unit != null)
			{
				// 적이 아니면(아군/캐스터 자신/사망) 무시하고 통과
				if (_data.caster == null || unit.IsDead == true || TargetResolver.IsEnemy(_data.caster.Faction, unit.Faction) == false)
				{
					return;
				}

				// 적중 — 발동 스킬 효과를 적중 대상에 적용 후 반환 (단일 hit, 관통은 후속)
				_hitBuffer.Clear();
				_hitBuffer.Add(unit);
				SkillEffectApplier.Apply(_data.hitEffect, _data.caster, _data.skillId, _hitBuffer);
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
			// 이동 방향으로 머리를 향하게 — 스프라이트 기준축(_spriteForwardAngle)만큼 보정.
			// 직선 비행이라 발사 순간 1회로 충분(유도/포물선은 후속에서 Update 회전).
			float angle = Mathf.Atan2(data.direction.y, data.direction.x) * Mathf.Rad2Deg - _spriteForwardAngle;
			transform.rotation = Quaternion.Euler(0f, 0f, angle);
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
