using System.IO;
using UnityEngine;

namespace ProjectOne.ServerData
{
	// IServerDataRepository 의 로컬 구현 — persistentDataPath/{key}.json 에 JsonUtility 로 직렬화.
	// (서버 연동 전까지 로컬 캐시 역할)
	public sealed class LocalJsonRepository : IServerDataRepository
	{
		public bool TryLoad<T>(string key, out T data) where T : class
		{
			data = null;

			string path = MakePath(key);
			if (File.Exists(path) == false)
			{
				return false;
			}

			string json = File.ReadAllText(path);
			if (string.IsNullOrEmpty(json) == true)
			{
				return false;
			}

			data = JsonUtility.FromJson<T>(json);
			return data != null;
		}

		public void Save<T>(string key, T data) where T : class
		{
			if (data == null)
			{
				return;
			}

			string path = MakePath(key);
			string json = JsonUtility.ToJson(data);
			File.WriteAllText(path, json);
		}

		static string MakePath(string key)
		{
			return Path.Combine(Application.persistentDataPath, key + ".json");
		}
	}
}
