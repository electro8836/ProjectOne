using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 브레이크 이미지 전용 풀. World Space Canvas 하위에 배치되어 그 아래로 아이템을 생성한다.
	public class BreakImgPool : PoolBase<BreakImgItem>
	{
		[SerializeField] private BreakImgItem _prefab;
		[SerializeField] private int _hardCap = 20;  // 동시 활성 최대 개수

		private readonly List<BreakImgItem> _activeItems = new List<BreakImgItem>(32);

		protected override BreakImgItem CreateItem()
		{
			return Instantiate(_prefab, transform);
		}

		public BreakImgItem Spawn(Vector3 worldPos)
		{
			if (_activeItems.Count >= _hardCap)
			{
				BreakImgItem oldest = _activeItems[0];
				_activeItems.RemoveAt(0);
				Release(oldest);
			}

			BreakImgItem item = GetFromPool();
			item.Initialize(worldPos);
			_activeItems.Add(item);
			item.OnActivate();
			return item;
		}

		// BreakImgManager.Update()에서 호출. 수명이 끝난 아이템을 풀에 반환한다.
		public void TickAll(float deltaTime)
		{
			for (int i = _activeItems.Count - 1; i >= 0; i--)
			{
				if (_activeItems[i].Tick(deltaTime))
				{
					BreakImgItem item = _activeItems[i];
					_activeItems.RemoveAt(i);
					Release(item);
				}
			}
		}
	}
}
