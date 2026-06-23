using UnityEngine;

namespace ProjectOne.Network
{
	// 앱 일시정지/종료 시 미저장 장착 변경(dirty)을 서버에 flush 하는 전역 컴포넌트.
	// 게임 부트에서 Ensure() 로 1회 생성되어 DontDestroyOnLoad 로 유지된다.
	// (화면 닫기 flush 가 1차 경로이고, 이건 변경 후 화면을 안 닫고 앱을 백그라운드/종료할 때의 안전망.)
	public sealed class LoadoutSyncFlusher : MonoBehaviour
	{
		private static LoadoutSyncFlusher _instance;

		// 부트에서 1회 생성 — 중복 생성 방지.
		public static void Ensure()
		{
			if (_instance != null)
			{
				return;
			}

			GameObject go = new GameObject(nameof(LoadoutSyncFlusher));
			_instance = go.AddComponent<LoadoutSyncFlusher>();
			DontDestroyOnLoad(go);
		}

		// 백그라운드 전환(pause=true) — 모바일에서 앱이 종료될 수 있는 시점이라 여기서 보낸다.
		private void OnApplicationPause(bool pause)
		{
			if (pause == true)
			{
				NetworkManager.Instance.FlushLoadoutIfDirty();
			}
		}

		private void OnApplicationQuit()
		{
			NetworkManager.Instance.FlushLoadoutIfDirty();
		}
	}
}
