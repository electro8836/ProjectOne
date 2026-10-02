using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Monsters;
using ProjectOne.Network;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.UI;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Field
{
	// 필드보스 처치 보상 — 처치 즉시 서버(FieldBossKill)가 하루 1회 제한을 확인하고 경험치·보상을 지급한다.
	//
	// 필드보스는 DailyReset 스폰 개체다(FieldMonsterSpawner 가 표시). 잡몹과 달리 바닥 드랍·배치 정산이 없고,
	// 서버 응답을 받아 반영한 뒤 보상 팝업을 띄운다(사용자 결정). MonsterKillReward 는 필드보스를 건너뛴다.
	//
	// 순수 C# 싱글톤 — 응답이 오기 전에 필드를 떠나도(씬 전환) 반영이 끊기지 않는다.
	public sealed class FieldBossReward : Singleton<FieldBossReward>
	{
		// 처치 1회의 지급 결과 버퍼 — 응답 콜백은 메인 스레드라 재사용해도 안전하다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>(8);

		private FieldBossReward()
		{
			EventManager.Instance.Subscribe<MonsterKillEvent>(onMonsterKill);
		}

		// 인스턴스 생성만을 목적으로 하는 호출 지점 — Instance 접근이 곧 생성이라 본문이 필요 없다.
		public void Touch()
		{
		}

		private void onMonsterKill(MonsterKillEvent e)
		{
			if (e.FieldBossSpawnID == 0)
			{
				return;
			}

			int expBonusPermille = MonsterKillReward.GetExpBonusPermille();
			int goldBonusPermille = RewardGranter.GetGoldBonusPermille();

			// 미로그인(오프라인 테스트) — 예전처럼 로컬에서 지급한다.
			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				grantLocal(e, expBonusPermille);
				return;
			}

			FieldBossKillRequest request = new FieldBossKillRequest();
			request.fieldId = e.FieldBossFieldID;
			request.spawnId = e.FieldBossSpawnID;
			request.monsterId = e.MonsterID;
			request.level = e.Level;
			request.expBonusPermille = expBonusPermille;
			request.goldBonusPermille = goldBonusPermille;
			request.masteryId = MonsterKillReward.CurrentMasteryId();
			NetworkManager.Instance.RequestFieldBossKill(request, onBossKillSettled);
		}

		private void onBossKillSettled(bool success, FieldBossKillResponse data, string error)
		{
			if (success == false || data == null)
			{
				Debug.LogWarning($"[FieldBossReward] 필드보스 정산 실패: {error}");
				return;
			}

			if (data.kill != null)
			{
				MonsterRespawnClock.Instance.SetBossKilled(data.kill.fieldId, data.kill.spawnId, data.kill.resetDay);
			}

			// 캐릭터는 서버 권위값, 마스터리는 증가분만 적립된다. 아직 서버에 올라가지 않은 필드 처치 경험치를 더한다.
			Account.Instance.SetExpAuthoritative(data.exp + FieldKillLedger.Instance.UnsettledExp);

			_granted.Clear();
			RewardGranter.FromServer(data.rewards, data.equipments, _granted);
			showRewards();
		}

		private void grantLocal(MonsterKillEvent e, int expBonusPermille)
		{
			int exp = MonsterExp.Calc(e.MonsterID, e.Level, expBonusPermille);
			if (exp > 0)
			{
				Account.Instance.AddExp(exp);
			}

			_granted.Clear();
			Table_Monster.Row monster = MonsterCatalog.GetMonster(e.MonsterID);
			if (monster != null)
			{
				RewardGranter.Roll(monster.RewardGroupID, RewardContext.MonsterKill, _granted);
			}

			RewardGranter.Roll(e.SpawnRewardGroupID, RewardContext.MonsterKill, _granted);
			showRewards();
		}

		// 지급(획득 로그) 후 팝업 — 팝업은 목록을 복사해 넘긴다(다음 처치가 버퍼를 다시 쓴다).
		private void showRewards()
		{
			RewardGranter.ApplyAll(_granted);

			if (_granted.Count == 0 || UIManager.HasInstance == false)
			{
				return;
			}

			List<GrantedReward> shown = new List<GrantedReward>(_granted);
			UIManager.Instance.ShowRewardPopupAsync(shown, CancellationToken.None).Forget();
		}
	}
}
