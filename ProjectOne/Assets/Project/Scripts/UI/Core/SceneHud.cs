using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 씬별 HUD 를 Addressable 로 띄우고 내리는 헬퍼.
	//
	// HUD 를 씬에 박아두면 씬을 늘릴 때마다 같은 계층을 복제하게 되고,
	// 프리팹 하나를 고쳐도 씬마다 따로 반영해야 한다. 씬은 비워두고 코드가 띄운다.
	//
	// 씬 하나당 HUD 하나이므로 단일 슬롯으로 충분하다.
	public static class SceneHud
	{
		private static GameObject _instance;
		private static string _address;

		public static bool IsLoaded
		{
			get { return _instance != null; }
		}

		// 이전 HUD 가 남아 있으면 먼저 내린다. 주소가 없거나 로드에 실패하면 경고 후 넘어간다.
		public static async UniTask LoadAsync(string address, CancellationToken ct)
		{
			Unload();

			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				// HUD 프리팹이 아직 없어도 씬 진입 자체는 막지 않는다.
				Debug.LogWarning($"[SceneHud] HUD 프리팹을 찾지 못했습니다: {address}");
				return;
			}

			_instance = Object.Instantiate(prefab);
			_address = address;
		}

		public static void Unload()
		{
			if (_instance != null)
			{
				Object.Destroy(_instance);
				_instance = null;
			}

			// 종료/취소 흐름에서 ResourceManager 가 먼저 파괴됐을 수 있어 가드한다.
			if (string.IsNullOrEmpty(_address) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_address);
			}

			_address = null;
		}
	}
}
