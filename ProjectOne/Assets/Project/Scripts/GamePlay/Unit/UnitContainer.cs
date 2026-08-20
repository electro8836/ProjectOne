using UnityEngine;

namespace ProjectOne.Unit
{
	// 씬에 배치하는 유닛 담을 그릇. 타입별 부모 Transform 만 소유한다.
	//
	// 매니저(UnitManager)와 나누는 이유 — 매니저는 레지스트리·시뮬레이션 상태를 들고 영속해야 하고,
	// 실제 인스턴스는 씬과 함께 나고 죽어야 한다. 컨테이너가 씬에 있으면 씬 전환이 곧 정리라
	// 명시적 청소를 빠뜨릴 여지가 없다.
	//
	// 씬에 배치하지 않아도 동작한다 — UnitManager 가 자기 자식으로 하나 만들어 쓴다.
	public class UnitContainer : MonoBehaviour
	{
		private Transform _heroesRoot;
		private Transform _monstersRoot;
		private Transform _summonsRoot;

		private void Awake()
		{
			_heroesRoot   = ensureChild("Heroes");
			_monstersRoot = ensureChild("Monsters");
			_summonsRoot  = ensureChild("Summons");

			UnitManager.Instance.RegisterContainer(this);
		}

		private void OnDestroy()
		{
			// 종료 중이면 매니저를 새로 만들지 않는다.
			if (UnitManager.HasInstance == true)
			{
				UnitManager.Instance.UnregisterContainer(this);
			}
		}

		public Transform GetRoot(UnitType type)
		{
			switch (type)
			{
				case UnitType.Hero:    return _heroesRoot;
				case UnitType.Monster: return _monstersRoot;
				case UnitType.Summon:  return _summonsRoot;
				default:               return transform;	// None 등 미분류 — 컨테이너 직속
			}
		}

		// 씬에서 미리 만들어 둔 자식이 있으면 그것을 쓴다(계층을 눈으로 구성할 수 있게).
		private Transform ensureChild(string childName)
		{
			Transform found = transform.Find(childName);
			if (found != null)
			{
				return found;
			}

			GameObject go = new GameObject(childName);
			go.transform.SetParent(transform, false);
			return go.transform;
		}
	}
}
