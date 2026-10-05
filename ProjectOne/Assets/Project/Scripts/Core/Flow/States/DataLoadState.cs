using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Loading;
using ProjectOne.Mail;
using ProjectOne.Network;
using ProjectOne.Field;
using ProjectOne.Quests;
using ProjectOne.Ranking;
using ProjectOne.Shared;
using ProjectOne.Shop;
using ProjectOne.UI;
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
		private bool _loadSucceeded;

		// 계정 데이터 요청을 연속으로 이만큼 실패하면 타이틀로 돌려보낸다.
		private const int MaxLoadAttempts = 3;
		private const float RetryDelaySeconds = 2f;

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 서버 데이터 구간 0.15~0.70 — fetch 마다 균등 분할.
			// 향후 우편/출석/랭킹 등이 추가되면 totalSteps 만 늘리면 % 가 자동으로 나뉜다(현재 1개).
			const int totalSteps = 1;
			int step = 0;

			await ensureLoadingAsync(ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.ServerData, 0f);
			bool loaded = await loadUserDataAsync(ct);
			if (loaded == false)
			{
				await GoToTitleAsync(NetworkMessages.ConnectionFailed);
				return;
			}

			step++;
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.ServerData, (float)step / totalSteps);

			// 로드 완료 — DevTester 등 후처리가 이 시점에 Account 를 오버라이드할 수 있다.
			EventManager.Instance.Publish(new DataLoadedEvent());

			// 저장된 진행도로 시작하고 자동 수락 퀘스트를 훑는다. DataLoadedEvent 이후여야
			// DevTester 의 오버라이드(레벨 등)가 활성화 조건에 반영된다.
			QuestTracker.Instance.OnDataLoaded();

			GameFlow.Instance.ChangeStateAsync(new TownState()).Forget();
		}

		// 로딩 화면이 없으면 타이틀→마을 흐름으로 띄운다. 패치 단계는 이미 지났으므로 채워 둔다.
		private static async UniTask ensureLoadingAsync(CancellationToken ct)
		{
			if (LoadingManager.Instance.IsShowing == true)
			{
				return;
			}

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToTown, ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.Patch, 1f);
		}

		// 계정 데이터를 받는다. 실패하면 잠시 뒤 스스로 다시 요청하고, MaxLoadAttempts 번 연속 실패하면 false.
		// 미로그인(Dev·오프라인)은 요청 자체를 건너뛰므로 빈 계정으로 통과한다.
		private async UniTask<bool> loadUserDataAsync(CancellationToken ct)
		{
			for (int attempt = 1; attempt <= MaxLoadAttempts; attempt++)
			{
				_loadSucceeded = false;
				await requestGetUserDataAsync();

				if (_loadSucceeded == true || NetworkManager.Instance.IsLoggedIn == false)
				{
					return true;
				}

				if (attempt < MaxLoadAttempts)
				{
					await UniTask.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), DelayType.Realtime, cancellationToken: ct);
				}
			}

			return false;
		}

		// 서버에 닿지 않는다 — 로딩 화면을 띄운 채 타이틀로 돌려보낸다(계정 데이터 로드 실패·게임 중 재전송 포기 공용).
		// notice 는 타이틀에 도착한 뒤 팝업으로 띄울 문구다.
		//
		// 타이틀에 처음 들어온 것처럼 만든다 — 영속 UI 를 전부 걷고 로그인 상태를 지운다.
		// 로딩은 타이틀 씬이 뜨면 TitleState 가 걷는다. 게임 중에는 타이틀 전이 이벤트가 로그인 뒤에야 나가므로,
		// 여기서 먼저 발행해 UI 밖의 정리(맵 해제 등)를 기존 처리에 맡긴다.
		public static async UniTask GoToTitleAsync(string notice)
		{
			await ensureLoadingAsync(CancellationToken.None);

			UIManager.Instance.ResetForTitle();
			NetworkManager.Instance.ResetLogin();
			TitleState.PendingNotice = notice;
			EventManager.Instance.Publish(new GameStateChangedEvent(typeof(TitleState)));

			changeToTitleNextFrameAsync().Forget();
		}

		// 타이틀 전이는 다음 프레임에 건다.
		//
		// 여기서 바로 걸면 타이틀 씬 로드가 시작된 같은 프레임에 GameStateChangedEvent 가 발행되고,
		// MapManager 가 그 이벤트로 맵 인스턴스를 해제한다. 비동기 씬 로드 도중의 Addressables 해제는
		// 메인 스레드를 영영 멈춘다(에디터 정지로 확인).
		private static async UniTaskVoid changeToTitleNextFrameAsync()
		{
			await UniTask.NextFrame();
			GameFlow.Instance.ChangeStateAsync(new TitleState()).Forget();
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
				_loadSucceeded = true;

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

				// 던전 진행도(최고 단계·입장 횟수) — 서버가 소유한다.
				DungeonProgress.Apply(data.dungeonProgress);

				if (data.costume != null)
				{
					Account.Instance.SetCostume(data.costume);
				}

				if (data.mastery != null)
				{
					Account.Instance.SetMastery(data.mastery);
				}

				if (data.quest != null)
				{
					Account.Instance.SetQuests(data.quest);
				}

				if (data.pet != null)
				{
					Account.Instance.SetPet(data.pet);
				}

				if (data.dailyBonus != null)
				{
					Account.Instance.SetDailyBonus(data.dailyBonus);
				}

				if (data.heroPass != null)
				{
					Account.Instance.SetHeroPass(data.heroPass);
				}

				if (data.shop != null)
				{
					ShopPurchaseCounter.Set(data.shop);
				}

				// 닉네임 — 없으면 서버가 Player + 8자리 숫자로 부여한다. 실패하면 다음 로그인에 다시 시도된다.
				MyPlayerProfile.SetNickname(data.nickname);
				MyPlayerProfile.SetNicknameChangeCount(data.nicknameChangeCount);
				if (string.IsNullOrEmpty(data.nickname) == true)
				{
					Debug.LogWarning($"[DataLoadState] 닉네임 없음 — 다음 로그인에 다시 부여한다: {data.error}");
				}

				// 새 우편 실시간 알림 — 메뉴 버튼 배지를 바로 갱신한다.
				MailNotifier.Connect();

				// 필드 처치 배치 정산 세션 — 서버가 로그인마다 새 시드를 발급한다. 없으면 원장이 비활성(로컬 지급).
				FieldKillLedger.Instance.Begin(data.field);

				// 필드보스 처치 기록 — 하루 1회 리젠 판정
				MonsterRespawnClock.Instance.ApplyBossKills((data.field != null) ? data.field.bossKills : null);
			}
			else
			{
				Debug.LogError($"[DataLoadState] GetUserData 실패 — 다시 요청한다: {errorMsg}");
			}

			// 성공/실패 무관 — fetch 완료를 통지(실패 처리는 loadUserDataAsync 가 한다).
			_getUserDataTcs?.TrySetResult();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
