using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.ServerData
{
	// 개발용 메모리 저장소 — 인스펙터로 구성한 DTO 를 보관하고 디스크에는 쓰지 않는다(비영속).
	// 부트 시점 ServerDataSystem.SetRepository 로 끼워넣으면 DataLoadState 가 이 데이터를 읽는다.
	public sealed class DevDataRepository : IServerDataRepository
	{
		private readonly Dictionary<string, object> _store = new Dictionary<string, object>();

		public DevDataRepository(CharacterData character, InventoryData inventory, SkillData skill)
		{
			_store[ServerDataSystem.KeyCharacter] = character;
			_store[ServerDataSystem.KeyInventory] = inventory;
			_store[ServerDataSystem.KeySkill] = skill;
		}

		public UniTask<(bool found, T data)> LoadAsync<T>(string key, CancellationToken ct) where T : class
		{
			object value;
			if (_store.TryGetValue(key, out value) == true)
			{
				T data = value as T;
				return UniTask.FromResult((data != null, data));
			}

			return UniTask.FromResult<(bool, T)>((false, null));
		}

		// 플레이 중 변경은 메모리에만 반영(디스크 미기록) → 세션 내 유지, 재실행 시 초기화
		public UniTask SaveAsync<T>(string key, T data) where T : class
		{
			_store[key] = data;
			return UniTask.CompletedTask;
		}
	}
}
