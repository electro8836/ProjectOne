using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 히어로 자식 "MagnetSensor" 오브젝트에 부착하는 자석 센서.
	// 넓은 트리거 콜라이더 안에 들어온 드랍만 OnTriggerStay2D 로 히어로 쪽으로 끌어당긴다.
	// - 감지는 엔진 브로드페이즈에 위임 → 전체 드랍 거리 스캔 없음
	// - 이동/가속 상태는 각 DropObject 가 소유(MagnetTick), 센서는 대상만 넘긴다
	// 요구사항: 자체 Kinematic Rigidbody2D(콜백 라우팅) + isTrigger CircleCollider2D.
	[RequireComponent(typeof(CircleCollider2D))]
	public class HeroMagnet : MonoBehaviour
	{
		// 자석 흡입 범위(반경). Awake 에서 센서 콜라이더에 반영.
		[SerializeField] private float _range = 0.5f;

		private CircleCollider2D _sensor;
		private UnitBase _owner;

		private void Awake()
		{
			_sensor = this.GetComponent<CircleCollider2D>();
			_sensor.radius = _range;
			_owner = this.GetComponentInParent<UnitBase>();
		}

		private void OnTriggerStay2D(Collider2D other)
		{
			// 죽은 히어로는 흡입하지 않음
			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			// 드랍만 대상 — 몬스터/투사체 콜라이더는 null 로 자연 제외 (전용 레이어로 대부분 사전 차단)
			DropObject drop = other.GetComponent<DropObject>();
			if (drop == null)
			{
				return;
			}

			drop.MagnetTick(_owner.HitCenter);
		}
	}
}
