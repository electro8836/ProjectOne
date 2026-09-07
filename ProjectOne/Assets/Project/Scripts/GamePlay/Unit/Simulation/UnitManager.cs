using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	// 활성 유닛 관리자 (MonoSingleton) — 레지스트리 + 유닛 일괄 구동.
	//
	// **부모 Transform 은 씬의 UnitContainer 가 소유한다.** 매니저는 상태(레지스트리·공간해시)만
	// 들고 영속하고, 실제 인스턴스는 씬과 함께 나고 죽는다 — 씬이 바뀌면 컨테이너가 파괴되며
	// 하위 유닛이 자동으로 정리되므로 명시적 청소를 빠뜨릴 여지가 없다.
	//
	// - 유닛은 OnEnable/OnDisable 에서 자기 자신을 등록/해제
	// - GetRoot(type) 은 등록된 컨테이너에 위임 (없으면 매니저가 하나 만들어 쓴다)
	// - GetByType(type) 으로 Type별 활성 목록 캐시 노출 (적 탐지 등)
	// - Fixed/LateUpdate 에서 UnitSimulator 를 구동 (캐시 갱신 / 분리 계산 / 전체 ManualTick)
	// ExecutionOrder 를 앞당겨 캐시 갱신이 UnitMover.FixedUpdate 보다 먼저 일어나게 한다.
	[DefaultExecutionOrder(-100)]
	public class UnitManager : MonoSingleton<UnitManager>
	{
		// 상시 매니저 — 마을·필드·던전이 모두 쓴다. 반복 생성/파괴하지 않는다.
		protected override bool Persistent => true;

		readonly List<UnitBase> _units = new List<UnitBase>(256);

		// 유닛 일괄 구동 로직 (순수 클래스) — 컨테이너가 소유하고 직접 호출
		readonly UnitSimulator _simulator = new UnitSimulator();

		// 충돌 후보 조회용 공간 해시 — FixedUpdate 에서 프레임당 1회 Rebuild
		readonly UnitSpatialHash _spatialHash = new UnitSpatialHash();

		// Type별 활성 유닛 캐시 — enum 키, List 값. 둘 다 동적 컬렉션.
		readonly Dictionary<UnitType, List<UnitBase>> _byType = new Dictionary<UnitType, List<UnitBase>>();
		static readonly List<UnitBase> _empty = new List<UnitBase>(0);

		// 씬에 배치된 컨테이너. 없으면 ensureContainer 가 **활성 씬에** 만든다(매니저 자식이 아니다).
		UnitContainer _container;

		// 외부 순회용 — 인덱스 for 사용 권장
		public IReadOnlyList<UnitBase> All
		{
			get { return _units; }
		}

		// 충돌 후보 조회용 공간 해시 — UnitMover 가 인접 유닛만 순회하는 데 사용
		public UnitSpatialHash SpatialHash
		{
			get { return _spatialHash; }
		}

		// 씬의 UnitContainer 가 Awake 에서 자기를 등록한다.
		//
		// 폴백은 디렉터가 요청할 때(=씬의 Awake 가 전부 끝난 뒤) 만들어지므로
		// "폴백이 먼저 있고 나중에 씬 것이 등록되는" 경우는 생기지 않는다. 핸도버 처리가 필요 없다.
		public void RegisterContainer(UnitContainer container)
		{
			if (container == null || _container == container)
			{
				return;
			}

			if (_container != null)
			{
				Debug.LogWarning($"[UnitManager] UnitContainer 가 둘 이상입니다 — 뒤에 등록된 {container.name} 을 씁니다.");
			}

			_container = container;
		}

		public void UnregisterContainer(UnitContainer container)
		{
			if (_container == container)
			{
				_container = null;
			}
		}

		// 컨테이너가 없으면 **활성 씬에** 하나 만든다.
		//
		// 매니저 자식으로 달면 안 된다 — 매니저는 영속(DontDestroyOnLoad)이라 컨테이너까지 씬 전환을
		// 넘어 살아남고, "컨테이너가 씬에 있으면 씬 전환이 곧 정리다" 라는 분리의 목적이 무너진다.
		// new GameObject 는 활성 씬에 생성되므로 부모를 지정하지 않는 것만으로 수명이 씬과 같아진다.
		UnitContainer ensureContainer()
		{
			if (_container != null)
			{
				return _container;
			}

			GameObject go = new GameObject("UnitContainer (auto)");
			_container = go.AddComponent<UnitContainer>();
			return _container;
		}

		// 캐시 갱신 → 공간 해시 Rebuild → 모든 유닛 이동 일괄 구동
		// (무버를 여기서 직접 구동 — per-MonoBehaviour FixedUpdate 콜백 오버헤드 제거)
		void FixedUpdate()
		{
			_simulator.RefreshCache(_units);
			_spatialHash.Rebuild(_units);
			_simulator.TickMovers(_units, Time.fixedDeltaTime);
		}

		// 캐시 재갱신 → 공간 해시 Rebuild → 분리 배치 계산 → 전체 유닛 ManualTick
		void LateUpdate()
		{
			_simulator.RefreshCache(_units);
			_spatialHash.Rebuild(_units);
			_simulator.ComputeSeparations(GetByType(UnitType.Monster), _spatialHash);
			_simulator.TickAll(_units, Time.deltaTime);
		}

		// UnitFactory 가 Instantiate 시 부모로 사용 — 실제 Transform 은 컨테이너가 소유한다.
		public Transform GetRoot(UnitType type)
		{
			return ensureContainer().GetRoot(type);
		}

		public void Register(UnitBase unit)
		{
			if (unit == null)
			{
				return;
			}

			// 중복 방지 — OnEnable 이 재호출되는 경로(씬 토글 등) 대비
			for (int i = 0; i < _units.Count; i++)
			{
				if (_units[i] == unit)
				{
					return;
				}
			}

			_units.Add(unit);

			// Type별 캐시 동기화
			UnitType type = unit.GetUnitType();
			List<UnitBase> list;
			if (_byType.TryGetValue(type, out list) == false)
			{
				list = new List<UnitBase>(64);
				_byType[type] = list;
			}

			list.Add(unit);
		}

		public void Unregister(UnitBase unit)
		{
			if (unit == null)
			{
				return;
			}

			_units.Remove(unit);

			// Type별 캐시 동기화
			UnitType type = unit.GetUnitType();
			List<UnitBase> list;
			if (_byType.TryGetValue(type, out list) == true)
			{
				list.Remove(unit);
			}
		}

		// Type별 활성 유닛 조회 — 캐시된 리스트 직접 반환 (호출자는 즉시 소비, 수정 금지)
		public IReadOnlyList<UnitBase> GetByType(UnitType type)
		{
			List<UnitBase> list;
			if (_byType.TryGetValue(type, out list) == false)
			{
				return _empty;
			}

			return list;
		}

		// 활성 유닛 전체 destroy. 파괴되면 OnDisable 에서 자동 Unregister 됨.
		readonly List<UnitBase> _clearBuffer = new List<UnitBase>(256);
		public void ClearAll()
		{
			_clearBuffer.Clear();
			for (int i = 0; i < _units.Count; i++)
			{
				_clearBuffer.Add(_units[i]);
			}

			for (int i = 0; i < _clearBuffer.Count; i++)
			{
				UnitBase u = _clearBuffer[i];
				if (u != null)
				{
					Object.Destroy(u.gameObject);
				}
			}

			_clearBuffer.Clear();
		}

		// 특정 Type 만 destroy
		public void ClearByType(UnitType type)
		{
			List<UnitBase> list;
			if (_byType.TryGetValue(type, out list) == false)
			{
				return;
			}

			_clearBuffer.Clear();
			for (int i = 0; i < list.Count; i++)
			{
				_clearBuffer.Add(list[i]);
			}

			for (int i = 0; i < _clearBuffer.Count; i++)
			{
				UnitBase u = _clearBuffer[i];
				if (u != null)
				{
					Object.Destroy(u.gameObject);
				}
			}

			_clearBuffer.Clear();
		}
	}
}
