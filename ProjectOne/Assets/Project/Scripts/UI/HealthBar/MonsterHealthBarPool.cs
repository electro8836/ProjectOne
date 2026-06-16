using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 몬스터 체력바 전용 풀. World Space Canvas 하위에 배치되어 그 아래로 아이템을 생성한다.
	// 회수와 hardCap 관리는 HealthBarManager 가 주도하고(InstanceID 매핑 동기화),
	// 풀은 순수 Get/Release 와 활성 목록 노출만 담당한다.
	public class MonsterHealthBarPool : PoolBase<MonsterHealthBarItem>
	{
		[SerializeField] private MonsterHealthBarItem _prefab;

		private readonly List<MonsterHealthBarItem> _activeItems = new List<MonsterHealthBarItem>(64);

		// 활성 바 목록 — 추가 순서 유지(인덱스 0 = 가장 오래된 바). 호출자는 순회만, 수정 금지.
		public IReadOnlyList<MonsterHealthBarItem> ActiveItems
		{
			get { return _activeItems; }
		}

		protected override MonsterHealthBarItem CreateItem()
		{
			return Instantiate(_prefab, transform);
		}

		public MonsterHealthBarItem Spawn(UnitBase unit)
		{
			MonsterHealthBarItem item = GetFromPool();
			item.Bind(unit);
			_activeItems.Add(item);
			item.OnActivate();
			return item;
		}

		// HealthBarManager 가 사망/hardCap 초과 시 호출 — 활성 목록에서 제거하고 풀에 반납
		public void RemoveActive(MonsterHealthBarItem item)
		{
			_activeItems.Remove(item);
			Release(item);
		}
	}
}
