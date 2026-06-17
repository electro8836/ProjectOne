using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.ServerData;

namespace ProjectOne.UserData
{
	// 보유 아이템(아이템정보 도메인) 모델 — 획득/합성/강화수치 보관.
	// InventoryData(직렬화 원본)와 _index(빠른 조회)를 동기 유지하며, 변경 시 자체 저장한다.
	public sealed class Inventory
	{
		// 강화 최대 수치 (강화 실행 로직은 범위 외 — 수치만 보관)
		public const int MaxEnhanceLevel = 15;

		private readonly InventoryData _data;
		private readonly Dictionary<int, OwnedItem> _index = new Dictionary<int, OwnedItem>();

		public Inventory(InventoryData data)
		{
			_data = (data != null) ? data : new InventoryData();
			buildIndex();
		}

		// ── 공개 API ──────────────────────────────────────────────────

		// 보유 여부 — count >= 1 이면 사용 가능
		public bool Has(int itemId)
		{
			OwnedItem item;
			if (_index.TryGetValue(itemId, out item) == true)
			{
				return item.count >= 1;
			}

			return false;
		}

		public int GetCount(int itemId)
		{
			OwnedItem item;
			if (_index.TryGetValue(itemId, out item) == true)
			{
				return item.count;
			}

			return 0;
		}

		public int GetEnhanceLevel(int itemId)
		{
			OwnedItem item;
			if (_index.TryGetValue(itemId, out item) == true)
			{
				return item.enhanceLevel;
			}

			return 0;
		}

		// 아이템 획득 — 없으면 생성, 있으면 count 증가
		public void Add(int itemId, int amount = 1)
		{
			if (itemId <= 0 || amount <= 0)
			{
				return;
			}

			OwnedItem item;
			if (_index.TryGetValue(itemId, out item) == false)
			{
				item = makeItem(itemId);
				_data.items.Add(item);
				_index.Add(itemId, item);
			}

			item.count += amount;
			save();
			publishChange(item);
		}

		// 합성 가능 여부.
		// TODO(테이블 컬럼 추가 후): Table_ItemInfo 의 NextGradeItemId / CombineCount 로 판정.
		public bool CanCombine(int itemId)
		{
			return false;
		}

		// 합성 실행 — CombineCount 만큼 소모하고 NextGradeItemId 를 1개 획득.
		// TODO(테이블 컬럼 추가 후): NextGradeItemId / CombineCount 컬럼이 생기면 본문을 구현한다.
		public bool TryCombine(int itemId)
		{
			if (CanCombine(itemId) == false)
			{
				return false;
			}

			return false;
		}

		// 강화 수치 설정 (0~15 보관만 — 강화 실행 로직은 범위 외)
		public void SetEnhanceLevel(int itemId, int level)
		{
			OwnedItem item;
			if (_index.TryGetValue(itemId, out item) == false)
			{
				return;
			}

			int clamped = Mathf.Clamp(level, 0, MaxEnhanceLevel);
			if (item.enhanceLevel == clamped)
			{
				return;
			}

			item.enhanceLevel = clamped;
			save();
			publishChange(item);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildIndex()
		{
			_index.Clear();
			for (int i = 0; i < _data.items.Count; i++)
			{
				OwnedItem item = _data.items[i];
				if (item == null || item.itemId <= 0)
				{
					continue;
				}

				_index[item.itemId] = item;
			}
		}

		private static OwnedItem makeItem(int itemId)
		{
			OwnedItem item = new OwnedItem();
			item.itemId = itemId;
			item.count = 0;
			item.enhanceLevel = 0;
			return item;
		}

		private void save()
		{
			ServerDataSystem.Repository.Save(ServerDataSystem.KeyInventory, _data);
		}

		private void publishChange(OwnedItem item)
		{
			EventManager.Instance.Publish(new InventoryChangeEvent(item.itemId, item.count, item.enhanceLevel));
		}
	}
}
