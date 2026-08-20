using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Loading;
using ProjectOne.Network;
using ProjectOne.Quests;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Flow
{
	// 유저 데이터 로드 상태 — Login 씬 위 오버레이(별도 씬 없음).
	// 서버 권위: GetUserData 함수로 계정 전체를 받아 Account 에 반영한다(클라가 테이블 직접 읽지 않음).
	// 신규 계정의 기본 데이터 생성은 서버(Backnd 함수)가 담당. 서버 없을 땐 DevTester 가 Account 를 설정한다.
	public class DataLoadState : IGameState
	{
		// 서버 fetch 완료를 await 로 받기 위한 완료 소스(콜백 → UniTask 변환).
		private UniTaskCompletionSource _getUserDataTcs;

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 서버 데이터 구간 0.15~0.70 — fetch 마다 균등 분할.
			// 향후 우편/출석/랭킹 등이 추가되면 totalSteps 만 늘리면 % 가 자동으로 나뉜다(현재 1개).
			const int totalSteps = 1;
			int step = 0;

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.ServerData, 0f);
			await requestGetUserDataAsync();
			step++;
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.ServerData, (float)step / totalSteps);

			// 로드 완료 — DevTester 등 후처리가 이 시점에 Account 를 오버라이드할 수 있다.
			EventManager.Instance.Publish(new DataLoadedEvent());

			// 저장된 진행도로 시작하고 자동 수락 퀘스트를 훑는다. DataLoadedEvent 이후여야
			// DevTester 의 오버라이드(레벨 등)가 활성화 조건에 반영된다.
			QuestTracker.Instance.OnDataLoaded();

			GameFlow.Instance.ChangeStateAsync(new TownState()).Forget();
		}

		// GetUserData 호출을 콜백 → UniTask 로 래핑(NetworkManager 변경 없이 State 측에서 변환).
		//
		// 미로그인이면 요청 자체를 건너뛴다. 로그인 없이 InvokeFunction 을 넣으면 SendQueue 워커에서
		// 실패해 콜백이 영영 오지 않을 수 있고, 그러면 아래 TCS 가 풀리지 않아 로딩이 영구 고착된다.
		private UniTask requestGetUserDataAsync()
		{
			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				Debug.LogWarning("[DataLoadState] 미로그인 상태 — 서버 데이터 요청을 건너뜁니다. 빈 계정으로 진행합니다.");
				return UniTask.CompletedTask;
			}

			_getUserDataTcs = new UniTaskCompletionSource();
			NetworkManager.Instance.RequestGetUserData(onUserDataLoaded);
			return _getUserDataTcs.Task;
		}

		// GetUserData 응답 — Account 반영 후 완료 통지.
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

				if (data.loadout != null)
				{
					Account.Instance.SetLoadout(data.loadout);
				}

				if (data.clearedDungeons != null)
				{
					Account.Instance.SetClearedDungeons(data.clearedDungeons);
				}
			}
			else
			{
				Debug.LogError($"[DataLoadState] GetUserData 실패 — 빈 계정으로 진행: {errorMsg}");
			}

			// 성공/실패 무관 — fetch 완료를 통지(실패해도 빈 계정으로 진행).
			_getUserDataTcs?.TrySetResult();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
