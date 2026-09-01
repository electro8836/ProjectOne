using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Unit;
using ProjectOne.Audio;

namespace ProjectOne.Dungeon
{
	// 전투씬 휘발성 월드 오브젝트의 공통 베이스. DropManager 가 풀에서 스폰한다.
	// 히어로가 접촉(트리거)하면 파생의 효과를 적용하고 풀로 반환된다.
	// 프리팹 요구사항: isTrigger CircleCollider2D + Kinematic Rigidbody2D.
	//
	// 종류별 차이는 전부 파생이 갖는다 — HealOrb(회복) / BuffRune(버프·수명) / RewardDrop(보상·흡입).
	public abstract class DropObject : MonoBehaviour, IPoolable
	{
		[Header("수명")]
		// 획득되지 않았을 때 스스로 사라지기까지의 시간(초). 0 이면 무한.
		[SerializeField] private float _lifetime = 0f;

		[Header("픽업 연출")]
		// 픽업 FX 프리팹 (직접 링크) — VFXManager 가 풀링 재생/회수
		[SerializeField] private GameObject _pickupFx;
		// 픽업 SFX (직접 링크) — AudioManager 풀에서 2D 재생
		[SerializeField] private AudioClip _pickupSfx;

		private DropObjectPool _ownerPool;
		// 같은 프레임 다중 트리거로 이중 반환되는 것 방지
		private bool _isReleased;
		// 남은 수명 (_lifetime 이 0 이면 쓰지 않는다)
		private float _remainingLife;

		// 파생이 흡입 로직에서 이중 반환을 피하려고 읽는다.
		protected bool IsReleased => _isReleased;

		// DropObjectPool.Spawn() 이 위치 설정 → Initialize() → OnActivate() 순서로 호출.
		// 파생은 자기 상태를 리셋하기 전에 base.Initialize(pool) 을 먼저 부른다.
		public virtual void Initialize(DropObjectPool pool)
		{
			_ownerPool = pool;
			_isReleased = false;
			_remainingLife = _lifetime;
		}

		private void Update()
		{
			// _ownerPool 이 없으면 아직 Initialize 를 거치지 않은 예열 인스턴스다.
			if (_isReleased == true || _ownerPool == null)
			{
				return;
			}

			// 수명과 무관하게 매 프레임 돈다 — 유지 시간 판정처럼 수명이 없는 파생도 써야 한다.
			Tick(Time.deltaTime);

			// _lifetime 이 0 이면 스스로 사라지지 않는다 — 스테이지 정리가 회수한다.
			if (_lifetime <= 0f)
			{
				return;
			}

			_remainingLife -= Time.deltaTime;
			if (_remainingLife > 0f)
			{
				return;
			}

			ReleaseSelf();
		}

		// 매 프레임 갱신이 필요한 파생용. 파생이 Update 를 직접 선언하면 베이스의 Update 와
		// 어느 쪽이 불릴지 불분명해지므로 이 훅을 거친다.
		protected virtual void Tick(float dt)
		{
		}

		// 접촉 즉시 획득할지. 유지 시간이 필요한 파생은 false 로 덮고 Enter/Exit 로 직접 판정한다.
		protected virtual bool PickupOnTouch
		{
			get { return true; }
		}

		// 히어로가 범위에 들어왔을 때 — 즉시 획득이 아닌 파생이 쓴다.
		protected virtual void OnHeroEnter(UnitBase hero)
		{
		}

		// 히어로가 실제로 획득했을 때 — 파생이 자기 효과를 적용한다.
		protected abstract void OnPickup(UnitBase hero);

		// 이중 반환을 막으면서 풀로 돌려보낸다 (픽업·수명만료 공용).
		protected void ReleaseSelf()
		{
			if (_isReleased == true)
			{
				return;
			}

			_isReleased = true;
			_ownerPool.Release(this);
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
		}

		// 히어로 판별 — 파생이 OnTriggerExit2D 에서 같은 규칙을 쓰도록 공개한다.
		//
		// 자석 센서(히어로 자식)는 획득 대상이 아니다 — 넓은 범위에서 즉시 획득되는 것을 막는다.
		// 몬스터/투사체 콜라이더는 GetComponentInParent 결과로 자연 제외된다.
		protected static bool TryGetHero(Collider2D other, out UnitBase hero)
		{
			hero = null;
			if (other.GetComponent<HeroMagnet>() != null)
			{
				return false;
			}

			UnitBase unit = other.GetComponentInParent<UnitBase>();
			if (unit == null || unit.GetUnitType() != UnitType.Hero || unit.IsDead == true)
			{
				return false;
			}

			hero = unit;
			return true;
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			if (_isReleased == true)
			{
				return;
			}

			UnitBase hero;
			if (TryGetHero(other, out hero) == false)
			{
				return;
			}

			OnHeroEnter(hero);

			// 유지 시간이 필요한 파생은 여기서 끝낸다 — 완료 판정은 파생이 직접 한다.
			if (PickupOnTouch == false)
			{
				return;
			}

			OnPickup(hero);
			PlayPickupFeedback();

			ReleaseSelf();
		}

		// 픽업 연출(FX/SFX) — 풀 반환 전 호출. FX/SFX 모두 전역 매니저에 위임해 본체 비활성화와 분리.
		protected void PlayPickupFeedback()
		{
			if (_pickupFx != null)
			{
				VFXManager.Instance.PlayOneShot(_pickupFx, transform.position);
			}

			if (_pickupSfx != null)
			{
				AudioManager.Instance.PlaySFX(_pickupSfx);
			}
		}
	}
}
