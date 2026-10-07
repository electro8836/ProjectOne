using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UI;
using ProjectOne.UserData;

namespace ProjectOne.Dungeon
{
	// 던전 입장 결과. ok 면 들어가고, run 은 서버가 발급한 런(미로그인이면 null — 로컬 런).
	public struct DungeonEnterResult
	{
		public bool ok;
		public DungeonRunDto run;
	}

	// 던전 입장 요청 — 서버(DungeonEnter)가 해금·남은 횟수를 확인해 차감하고 런 시드를 발급한다.
	// 던전 팝업 4종과 결과창의 재도전·다음 단계가 같은 경로를 쓴다.
	// 미로그인(오프라인 테스트)이면 예전처럼 로컬에서 횟수만 센다.
	public static class DungeonEntry
	{
		private const string INVENTORY_FULL_MESSAGE = "인벤토리가 가득 차서 던전에 입장할 수 없습니다.";

		private static UniTaskCompletionSource<DungeonEnterResult> _pending;

		public static async UniTask<DungeonEnterResult> RequestAsync(EDT.Dungeon type, int stage, CancellationToken ct)
		{
			DungeonEnterResult result = default(DungeonEnterResult);

			// 던전에서 얻는 장비를 받을 칸이 없으면 들여보내지 않는다 — 횟수 차감 전이라 입장 횟수는 그대로다.
			if (Account.Instance.Inventory.IsInventoryFull == true)
			{
				UIManager.Instance.ShowAlertMessage(INVENTORY_FULL_MESSAGE);
				return result;
			}

			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				result.ok = DungeonProgress.TryConsumeEnter(type);
				return result;
			}

			// 이미 요청 중이면(연타) 무시한다 — 횟수가 두 번 빠지지 않게.
			if (_pending != null)
			{
				return result;
			}

			DungeonEnterRequest request = new DungeonEnterRequest();
			request.dungeonType = (int)type;
			request.stage = stage;

			_pending = new UniTaskCompletionSource<DungeonEnterResult>();
			UniTask<DungeonEnterResult> task = _pending.Task;
			NetworkManager.Instance.RequestDungeonEnter(request, onEnterResponse);

			return await task.AttachExternalCancellation(ct);
		}

		private static void onEnterResponse(bool success, DungeonEnterResponse data, string error)
		{
			DungeonEnterResult result = default(DungeonEnterResult);
			if (success == true && data != null && data.run != null)
			{
				DungeonProgress.ApplyEntry(data.entry);
				result.ok = true;
				result.run = data.run;
			}
			else
			{
				Debug.LogWarning($"[DungeonEntry] 입장 거절: {error}");
			}

			UniTaskCompletionSource<DungeonEnterResult> pending = _pending;
			_pending = null;
			pending?.TrySetResult(result);
		}
	}
}
