using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Unit;
using ProjectOne.Audio;
using ProjectOne.Event;

namespace ProjectOne.Dungeon
{
	// 던전 휘발성 드랍 아이템. 몬스터 사망 시 DropManager 가 풀에서 스폰한다.
	// 히어로가 접촉(트리거)하면 타입별 효과를 적용하고 풀로 반환된다.
	// 프리팹 요구사항: isTrigger CircleCollider2D + Kinematic Rigidbody2D.
	public class DropObject : MonoBehaviour, IPoolable
	{
		// 체력/스태미너 회복 비율 (최대치 대비)
		private const float RestoreRatio = 0.25f;

		[Header("픽업 연출")]
		// 픽업 FX 프리팹 (직접 링크) — VFXManager 가 풀링 재생/회수
		[SerializeField] private GameObject _pickupFx;
		// 픽업 SFX (직접 링크) — AudioManager 풀에서 2D 재생
		[SerializeField] private AudioClip _pickupSfx;

		[Header("자석 흡입")]
		// 흡입 시작 속도(유닛/초)
		[SerializeField] private float _homingStartSpeed = 2f;
		// 흡입 가속도(유닛/초²) — 히어로에 가까워질수록 빨라지는 느낌
		[SerializeField] private float _homingAccel = 40f;
		// 흡입 최대 속도(유닛/초)
		[SerializeField] private float _homingMaxSpeed = 20f;

		private DropObjectType _type;
		private DropObjectPool _ownerPool;
		// MagicEssence 1회 획득량 (스폰 시 주입)
		private int _essenceAmount;
		// 같은 프레임 다중 트리거로 이중 반환되는 것 방지
		private bool _isReleased;
		// 이동/트리거 감지용 Kinematic Rigidbody2D (static 콜라이더 이동 시 충돌 트리 재빌드 회피)
		private Rigidbody2D _rb;
		// 현재 흡입 속도 (스폰마다 _homingStartSpeed 로 리셋)
		private float _homingSpeed;

		private void Awake()
		{
			_rb = this.GetComponent<Rigidbody2D>();
		}

		// DropObjectPool.Spawn() 이 위치 설정 → Initialize() → OnActivate() 순서로 호출
		public void Initialize(DropObjectType type, DropObjectPool pool, int essenceAmount)
		{
			_type = type;
			_ownerPool = pool;
			_essenceAmount = essenceAmount;
			_isReleased = false;
			_homingSpeed = _homingStartSpeed;
		}

		// HeroMagnet 센서가 프레임(물리)마다 호출 — 히어로 중심으로 가속 이동. 최종 획득은 기존 OnTriggerEnter2D 가 처리.
		public void MagnetTick(Vector2 targetCenter)
		{
			if (_isReleased == true)
			{
				return;
			}

			_homingSpeed = Mathf.Min(_homingSpeed + _homingAccel * Time.fixedDeltaTime, _homingMaxSpeed);
			Vector2 next = Vector2.MoveTowards(_rb.position, targetCenter, _homingSpeed * Time.fixedDeltaTime);
			_rb.MovePosition(next);
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			if (_isReleased == true)
			{
				return;
			}

			// 자석 센서(히어로 자식)는 획득 대상 아님 — 넓은 범위에서 즉시 획득되는 것 방지
			if (other.GetComponent<HeroMagnet>() != null)
			{
				return;
			}

			// 히어로만 획득 — 몬스터/투사체 콜라이더는 GetComponentInParent 결과로 자연 제외
			UnitBase unit = other.GetComponentInParent<UnitBase>();
			if (unit == null || unit.GetUnitType() != UnitType.Hero || unit.IsDead == true)
			{
				return;
			}

			applyPickupEffect(unit);
			playPickupFeedback();

			_isReleased = true;
			_ownerPool.Release(this);
		}

		// 픽업 연출(FX/SFX) — 풀 반환 전 호출. FX/SFX 모두 전역 매니저에 위임해 본체 비활성화와 분리.
		private void playPickupFeedback()
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

		private void applyPickupEffect(UnitBase hero)
		{
			switch (_type)
			{
			case DropObjectType.HealOrb:
				hero.Vitals.ModifyHp(hero.Stats.GetStat(StatInfo.MaxHP) * RestoreRatio);
				break;

			case DropObjectType.StaminaOrb:
				hero.Vitals.ModifyStamina(hero.Stats.GetStat(StatInfo.MaxStamina) * RestoreRatio);
				break;

			case DropObjectType.MagicEssence:
				DungeonRunState.Instance.AddEssence(_essenceAmount);
				break;
			}
		}
	}
}
