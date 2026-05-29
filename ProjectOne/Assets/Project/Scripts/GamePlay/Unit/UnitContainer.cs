using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	// 활성 유닛 컨테이너 (MonoSingleton) — 레지스트리 + Hierarchy 부모 통합
	// - 유닛은 OnEnable/OnDisable 에서 자기 자신을 등록/해제
	// - GetRoot(type) 으로 Type별 부모 Transform 제공 → UnitFactory 가 스폰 시 부모로 사용
	// - GetByType(type) 으로 Type별 활성 목록 캐시 노출 (적 탐지 등)
	// - ClearAll/ClearByType 으로 씬 전환·전투 종료 시 일괄 정리
	public class UnitContainer : MonoSingleton<UnitContainer>
	{
		readonly List<UnitBase> _units = new List<UnitBase>(256);

		// Type별 활성 유닛 캐시 — enum 키, List 값. 둘 다 동적 컬렉션.
		readonly Dictionary<UnitType, List<UnitBase>> _byType = new Dictionary<UnitType, List<UnitBase>>();
		static readonly List<UnitBase> _empty = new List<UnitBase>(0);

		Transform _heroesRoot;
		Transform _monstersRoot;

		// 외부 순회용 — 인덱스 for 사용 권장
		public IReadOnlyList<UnitBase> All
		{
			get { return _units; }
		}

		protected override void Awake()
		{
			base.Awake();
			_heroesRoot   = CreateChild("Heroes");
			_monstersRoot = CreateChild("Monsters");
		}

		Transform CreateChild(string name)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(transform, false);
			return go.transform;
		}

		// UnitFactory 가 Instantiate 시 부모로 사용
		public Transform GetRoot(UnitType type)
		{
			switch (type)
			{
				case UnitType.Hero:    return _heroesRoot;
				case UnitType.Monster: return _monstersRoot;
				default:               return transform;  // None 등 미분류 — 컨테이너 루트 직속
			}
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
