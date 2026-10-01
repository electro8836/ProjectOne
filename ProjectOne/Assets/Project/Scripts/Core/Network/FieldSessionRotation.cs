using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Field;
using ProjectOne.Shared;
using UnityEngine;

namespace ProjectOne.Network
{
	// 로딩(맵 이동) 시점의 필드 세션 교체 — 남은 처치를 정산하고 새 시드를 받는다(FieldSessionRotate).
	//
	// 시드는 클라가 알아야 드랍을 바로 보여줄 수 있어 숨길 수 없다. 대신 맵마다 바꿔 예측할 수 있는 범위를 줄인다.
	// 로딩 중에는 처치가 없고 바닥 드랍도 모두 정리되므로, 여기서 끊으면 옛 시드와 새 시드가 섞이지 않는다.
	public static class FieldSessionRotation
	{
		// 로딩이 응답을 기다리는 최대 시간. 넘기면 옛 시드로 진행하고, 늦은 응답이 와도 콜백이 교체를 마친다.
		private const float TimeoutSeconds = 3f;

		private static UniTaskCompletionSource _pending;

		// 로딩을 띄운 직후, 씬·맵을 바꾸기 전에 호출한다.
		public static async UniTask RotateAsync(CancellationToken ct)
		{
			if (NetworkManager.Instance.IsLoggedIn == false || _pending != null)
			{
				return;
			}

			FieldKillLedger ledger = FieldKillLedger.Instance;

			// 이번 세션에 처치가 없었으면 시드가 쓰인 적이 없다 — 바꿀 이유가 없다(로그인 직후 첫 마을 진입 등).
			if (ledger.IsActive == false || ledger.NextKillIndex == 0)
			{
				return;
			}

			// 이 맵의 바닥 드랍은 더는 주울 수 없다 — 미획득으로 확정하고 전부 보낸다.
			ledger.ResolveAll();
			FieldKillDto[] kills = ledger.TakeRemaining();

			_pending = new UniTaskCompletionSource();
			UniTask task = _pending.Task;
			NetworkManager.Instance.RequestFieldRotate(ledger.Seed, kills, onRotated);

			await UniTask.WhenAny(task, UniTask.Delay(TimeSpan.FromSeconds(TimeoutSeconds), DelayType.Realtime, PlayerLoopTiming.Update, ct));
		}

		// 성공하면 새 세션으로 원장을 다시 시작한다(보낸 처치는 서버에 반영됐거나 폐기됐다).
		// 실패하면 옛 세션을 유지하고 처치를 다시 대기열에 둔다 — 다음 주기 배치나 다음 로딩에서 재전송된다.
		private static void onRotated(bool success, FieldRotateResponse data, string error)
		{
			FieldKillLedger ledger = FieldKillLedger.Instance;
			if (success == true && data != null && data.field != null && data.field.seed != 0)
			{
				if (string.IsNullOrEmpty(data.error) == false)
				{
					Debug.LogError($"[FieldSessionRotation] 남은 처치 정산이 거절됐다 — 세션은 교체: {data.error}");
				}

				ledger.Begin(data.field);
			}
			else
			{
				Debug.LogWarning($"[FieldSessionRotation] 세션 교체 실패 — 옛 시드로 계속: {error}");
				ledger.OnSendFailed();
			}

			UniTaskCompletionSource pending = _pending;
			_pending = null;
			pending?.TrySetResult();
		}
	}
}
