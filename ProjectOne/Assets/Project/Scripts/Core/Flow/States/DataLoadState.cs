using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Flow
{
	// 유저 데이터 로드 상태 — Login 씬 위 오버레이(별도 씬 없음).
	// 서버 권위: GetUserData 함수로 계정 전체를 받아 Account 에 반영한다(클라가 테이블 직접 읽지 않음).
	// 신규 계정의 기본 데이터 생성은 서버(Backnd 함수)가 담당. 서버 없을 땐 DevTester 가 Account 를 설정한다.
	public class DataLoadState : IGameState
	{
		public UniTask EnterAsync(CancellationToken ct)
		{
			NetworkManager.Instance.RequestGetUserData(onUserDataLoaded);
			return UniTask.CompletedTask;
		}

		// GetUserData 응답 — Account 반영 후 로드 완료 이벤트 발행 + 로비 전이.
		private void onUserDataLoaded(bool isSuccess, GetUserDataResponse data, string errorMsg)
		{
			if (isSuccess == true && data != null)
			{
				// 로그인 스냅샷 반영 — 서버 권위 데이터를 Account 도메인에 주입(DTO → 도메인 변환은 Set* 내부에서).
				if (data.currency != null)
				{
					Account.Instance.SetCurrency(data.currency);
				}

				if (data.inventory != null)
				{
					Account.Instance.SetInventory(data.inventory);
				}

				Account.Instance.Loadout.AddExp(101, data.exp);

				// (테스트) 서버 저장 exp 수신 확인 — 재로그인 시 유지되는지 로그로 검증.
				Debug.Log($"[테스트] GetUserData 수신 → exp={data.exp}");
			}
			else
			{
				Debug.LogError($"[DataLoadState] GetUserData 실패 — 빈 계정으로 진행: {errorMsg}");
			}

			// 로드 완료 — DevTester 등 후처리가 이 시점에 Account 를 오버라이드할 수 있다.
			EventManager.Instance.Publish(new DataLoadedEvent());

			GameFlow.Instance.ChangeStateAsync(new LobbyState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
