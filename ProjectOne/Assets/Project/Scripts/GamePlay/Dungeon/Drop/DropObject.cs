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

		private DropObjectType _type;
		private DropObjectPool _ownerPool;
		// MagicEssence 1회 획득량 (스폰 시 주입)
		private int _essenceAmount;
		// 같은 프레임 다중 트리거로 이중 반환되는 것 방지
		private bool _isReleased;

		// DropObjectPool.Spawn() 이 위치 설정 → Initialize() → OnActivate() 순서로 호출
		public void Initialize(DropObjectType type, DropObjectPool pool, int essenceAmount)
		{
			_type = type;
			_ownerPool = pool;
			_essenceAmount = essenceAmount;
			_isReleased = false;
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
