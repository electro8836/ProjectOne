using UnityEngine;
using ProjectOne.Field;

namespace ProjectOne.Network
{
	// 필드 처치 배치 정산 주기 트리거 — 게임 부트에서 Ensure() 로 1회 생성되어 DontDestroyOnLoad 로 유지된다.
	//
	// 다른 펑션 직전 flush(NetworkManager)가 순서를 보장하고, 이건 주기·일시정지·종료 시점의 전송을 맡는다.
	public sealed class FieldSettleTicker : MonoBehaviour
	{
		// 배치 전송 주기(초) — 시간 배율과 무관하게 실제 시간으로 잰다.
		private const float IntervalSeconds = 20f;

		private static FieldSettleTicker _instance;

		private float _elapsed;

		// 부트에서 1회 생성 — 중복 생성 방지.
		public static void Ensure()
		{
			if (_instance != null)
			{
				return;
			}

			GameObject go = new GameObject(nameof(FieldSettleTicker));
			_instance = go.AddComponent<FieldSettleTicker>();
			DontDestroyOnLoad(go);
		}

		private void Update()
		{
			_elapsed += Time.unscaledDeltaTime;
			if (_elapsed < IntervalSeconds)
			{
				return;
			}

			_elapsed = 0f;
			NetworkManager.Instance.FlushFieldBatch();
		}

		// 백그라운드 전환 — 정리가 끝난 처치까지만 보낸다. 바닥 드랍은 복귀 후에도 주울 수 있으므로 확정하지 않는다.
		private void OnApplicationPause(bool pause)
		{
			if (pause == true)
			{
				NetworkManager.Instance.FlushFieldBatch();
			}
		}

		// 종료 — 남은 드랍은 더 이상 주울 수 없으니 미획득으로 확정하고 전부 보낸다.
		private void OnApplicationQuit()
		{
			FieldKillLedger.Instance.ResolveAll();
			NetworkManager.Instance.FlushFieldBatch();
		}
	}
}
