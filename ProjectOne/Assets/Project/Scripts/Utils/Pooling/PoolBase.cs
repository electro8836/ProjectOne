using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ProjectOne.Utils
{
	// 어떤 MonoBehaviour든 IPoolable을 구현하면 이 베이스를 상속해 풀을 만들 수 있음
	// 사용법: public class MyPool : PoolBase<MyItem> { override CreateItem() }
	public abstract class PoolBase<T> : MonoBehaviour where T : MonoBehaviour, IPoolable
	{
		[SerializeField] protected int capacity = 10;     // 씬 시작 시 미리 생성할 개수 = 기본 풀 크기
		[SerializeField] protected int maxCapacity = 100; // 초과 반환 시 파괴 기준

		private ObjectPool<T> _pool;

		protected virtual void Awake()
		{
			_pool = new ObjectPool<T>(
				createFunc: CreateItem,
				actionOnGet: onGet,
				actionOnRelease: onRelease,
				actionOnDestroy: onItemDestroy,
				collectionCheck: true,
				defaultCapacity: capacity,
				maxSize: maxCapacity
			);

			prewarm();
		}

		// ── 서브클래스 구현 영역 ───────────────────────────────────────

		// 새 아이템 생성 방법을 서브클래스가 정의 (보통 Instantiate)
		protected abstract T CreateItem();

		// ── 풀 API ────────────────────────────────────────────────────

		// 풀에서 아이템을 꺼낸 뒤 초기화 → 활성화 순서는 서브클래스의 Spawn()이 담당
		protected T GetFromPool()
		{
			return _pool.Get();
		}

		// 외부(또는 아이템 자신)에서 반환 시 호출
		public void Release(T item)
		{
			_pool.Release(item);
		}

		// 비활성 인스턴스를 전부 파괴하고 capacity 만큼 다시 예열 — 런타임 풀 크기 초기화
		// 활성 아이템은 _pool.Clear() 대상이 아니므로, 호출자가 활성분을 먼저 회수해야 한다
		public void ResetPool()
		{
			_pool.Clear();
			prewarm();
		}

		// ── 내부 메서드 ───────────────────────────────────────────────

		// capacity만큼 미리 생성해 비활성 상태로 풀에 채움
		private void prewarm()
		{
			var preWarmed = new List<T>(capacity);
			for (int i = 0; i < capacity; i++)
			{
				preWarmed.Add(_pool.Get());
			}

			for (int i = 0; i < preWarmed.Count; i++)
			{
				_pool.Release(preWarmed[i]);
			}
		}

		// ── 내부 콜백 (람다 지양 - 메서드 그룹으로 연결) ────────────────

		private void onGet(T item)
		{
			item.gameObject.SetActive(true);
		}

		private void onRelease(T item)
		{
			// OnDeactivate → SetActive(false) 순서 고정
			// 코루틴·로직 정리를 활성 상태에서 먼저 처리
			item.OnDeactivate();
			item.gameObject.SetActive(false);
		}

		private void onItemDestroy(T item)
		{
			Destroy(item.gameObject);
		}
	}
}
