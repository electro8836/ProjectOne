using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 데미지 텍스트 전용 풀. World Space Canvas 하위에 배치되어 그 아래로 아이템을 생성한다.
	public class DamageTextPool : PoolBase<DamageTextItem>
	{
		[SerializeField] private DamageTextItem _prefab;
		[SerializeField] private int _hardCap = 30;  // 동시 활성 최대 개수

		private readonly List<DamageTextItem> _activeItems = new List<DamageTextItem>(32);

		protected override DamageTextItem CreateItem()
		{
			return Instantiate(_prefab, transform);
		}

		public DamageTextItem Spawn(Vector3 worldPos, string text, Color color)
		{
			if (_activeItems.Count >= _hardCap)
			{
				DamageTextItem oldest = _activeItems[0];
				_activeItems.RemoveAt(0);
				Release(oldest);
			}

			DamageTextItem item = GetFromPool();
			item.Initialize(worldPos, text, color, this);
			_activeItems.Add(item);
			item.OnActivate();
			return item;
		}

		// DamageTextManager.Update()에서 호출. 수명이 끝난 아이템을 풀에 반환한다.
		public void TickAll(float deltaTime)
		{
			for (int i = _activeItems.Count - 1; i >= 0; i--)
			{
				if (_activeItems[i].Tick(deltaTime))
				{
					DamageTextItem item = _activeItems[i];
					_activeItems.RemoveAt(i);
					Release(item);
				}
			}
		}
	}
}
