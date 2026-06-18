using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.ServerData
{
	// IServerDataRepository 의 로컬 구현 — persistentDataPath/{key}.json 에 JsonUtility 로 직렬화.
	// (서버 연동 전까지 로컬 캐시 역할) 디스크 접근은 동기지만 인터페이스 계약상 완료된 UniTask 로 반환한다.
	public sealed class LocalJsonRepository : IServerDataRepository
	{
		public UniTask<(bool found, T data)> LoadAsync<T>(string key, CancellationToken ct) where T : class
		{
			string path = MakePath(key);
			if (File.Exists(path) == false)
			{
				return UniTask.FromResult<(bool, T)>((false, null));
			}

			string json = File.ReadAllText(path);
			if (string.IsNullOrEmpty(json) == true)
			{
				return UniTask.FromResult<(bool, T)>((false, null));
			}

			T data = JsonUtility.FromJson<T>(json);
			return UniTask.FromResult((data != null, data));
		}

		public UniTask SaveAsync<T>(string key, T data) where T : class
		{
			if (data == null)
			{
				return UniTask.CompletedTask;
			}

			string path = MakePath(key);
			string json = JsonUtility.ToJson(data);
			File.WriteAllText(path, json);
			return UniTask.CompletedTask;
		}

		static string MakePath(string key)
		{
			return Path.Combine(Application.persistentDataPath, key + ".json");
		}
	}
}
