using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 히어로 자식 "MagnetSensor" 오브젝트에 부착하는 자석 센서.
	// 넓은 트리거 콜라이더 안에 들어온 보상 드랍만 OnTriggerStay2D 로 히어로 쪽으로 끌어당긴다.
	// - 감지는 엔진 브로드페이즈에 위임 → 전체 드랍 거리 스캔 없음
	// - 이동/가속 상태는 각 RewardDrop 이 소유(MagnetTick), 센서는 대상만 넘긴다
	// 요구사항: 자체 Kinematic Rigidbody2D(콜백 라우팅) + isTrigger CircleCollider2D.
	[RequireComponent(typeof(CircleCollider2D))]
	public class HeroMagnet : MonoBehaviour
	{
		// 센서 오브젝트 이름 — 히어로 프리팹이 아니라 코드가 만든다.
		private const string SensorName = "MagnetSensor";

		// 센서 전용 레이어 — Physics2D 매트릭스에서 Drop 레이어하고만 충돌하도록 설정돼 있다.
		private const string SensorLayerName = "MagnetSensor";

		// Stat_PickupRange 가 비어 있을 때 쓰는 기본 흡입 범위(반경).
		// CharacterStat 에 StatDetail_PickupRange_Base 행이 생기면 그 값이 이 기본값을 대체한다.
		[SerializeField] private float _range = 2f;

		private CircleCollider2D _sensor;
		private UnitBase _owner;
		// 마지막으로 반경에 반영한 스탯 버전 — 장비 교체 등으로 범위가 바뀌면 갱신한다.
		private int _appliedStatVersion = -1;

		// 히어로에 자석 센서를 붙인다. 프리팹을 건드리지 않고 생성 시점에 코드가 구성한다.
		public static HeroMagnet AttachTo(UnitBase hero)
		{
			if (hero == null)
			{
				return null;
			}

			HeroMagnet existing = hero.GetComponentInChildren<HeroMagnet>(true);
			if (existing != null)
			{
				return existing;		// 풀에서 재사용된 히어로 — 이미 달려 있다
			}

			GameObject go = new GameObject(SensorName);
			go.transform.SetParent(hero.transform, false);
			go.transform.localPosition = Vector3.zero;

			// 전용 레이어를 반드시 씌운다. Default 로 남으면 투사체가 이 넓은 센서에 걸려
			// GetComponentInParent 로 히어로를 찾아내 사거리 밖에서 명중 판정이 나 버린다.
			int sensorLayer = LayerMask.NameToLayer(SensorLayerName);
			if (sensorLayer < 0)
			{
				Debug.LogError($"[HeroMagnet] 레이어 '{SensorLayerName}' 가 없습니다 — Project Settings > Tags and Layers 를 확인하세요.");
			}
			else
			{
				go.layer = sensorLayer;
			}

			// 트리거 콜백을 받으려면 자체 Rigidbody2D 가 필요하다. Kinematic 이라 물리에 관여하지 않는다.
			Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
			rb.bodyType = RigidbodyType2D.Kinematic;
			rb.simulated = true;

			CircleCollider2D sensor = go.AddComponent<CircleCollider2D>();
			sensor.isTrigger = true;

			return go.AddComponent<HeroMagnet>();
		}

		private void Awake()
		{
			_sensor = this.GetComponent<CircleCollider2D>();
			_owner = this.GetComponentInParent<UnitBase>();

			// 감지 원 중심을 판정 기준점(HitCenter)에 맞춘다 — 흡입 목표점과 동일 기준
			if (_owner != null)
			{
				_sensor.offset = _owner.ColliderOffset;
			}

			applyRange();
		}

		private void Update()
		{
			applyRange();
		}

		// 흡입 반경은 Stat_PickupRange 를 따른다. 값이 없으면 인스펙터 기본값으로 떨어진다.
		private void applyRange()
		{
			if (_owner == null || _owner.Stats == null)
			{
				return;
			}

			if (_appliedStatVersion == _owner.Stats.Version)
			{
				return;
			}

			_appliedStatVersion = _owner.Stats.Version;

			float range = _owner.Stats.GetStat(Stat.Stat_PickupRange);
			_sensor.radius = (range > 0f) ? range : _range;
		}

		private void OnTriggerStay2D(Collider2D other)
		{
			// 죽은 히어로는 흡입하지 않음
			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			// 보상 드랍만 대상 — 회복 오브·버프룬은 제자리에 머물고, 몬스터/투사체는 null 로 자연 제외
			RewardDrop drop = other.GetComponent<RewardDrop>();
			if (drop == null)
			{
				return;
			}

			drop.MagnetTick(_owner.HitCenter);
		}
	}
}
