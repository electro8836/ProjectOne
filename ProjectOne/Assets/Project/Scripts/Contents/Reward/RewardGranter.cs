using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Shared;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Reward
{
	// 보상 지급 맥락 (설계 3장).
	//
	// Reward 테이블은 "무엇을 얼마나" 만 갖고 **누가 왜 주는지는 모른다.** 그런데
	// Stat_GoldDropBonus 는 지급 맥락에 따라 적용 여부가 갈린다 — 같은 보상 그룹이
	// 몬스터 드랍과 퀘스트 보상 양쪽에 쓰일 수 있으므로 테이블만으로는 구분할 수 없다.
	//
	// 이 규칙은 데이터가 아니라 코드가 소유한다 (기반테이블 8.1).
	public enum RewardContext
	{
		None = 0,
		MonsterKill,
		QuestComplete,
		DungeonClear,
		DailyBonus,
		HeroPass
	}

	// 실제로 지급된 것 하나. 결과창 표시와 서버 배치 업로드(STEP 14)가 같은 목록을 쓴다.
	public struct GrantedReward
	{
		public RewardType type;
		public int itemId;				// Item / ItemPool
		public EDT.Currency currency;	// Currency
		public int count;

		// 장비면 만들어진 인스턴스. 등급·순도·품질이 여기 들어 있다.
		public EquipmentInstance equipment;
	}

	// Reward 그룹을 굴려 지급한다 (설계 6장).
	//
	// **추첨(Roll)과 지급(ApplyAll)이 분리되어 있다.** 바닥 드랍처럼 "굴리는 시점"과
	// "인벤에 들어가는 시점"이 다른 경로가 있기 때문이다 — 몬스터 처치는 즉시 굴리되
	// 실제 지급은 히어로가 획득 범위에 들어왔을 때 일어난다.
	//
	// **추첨 규칙은 공유 코어(ProjectOne.Shared.RewardRoller)가 소유한다.** 서버(뒤끝 함수)도 같은 코드로 굴리므로
	// 여기는 맥락 → 배율 결정, 결과 → 장비 인스턴스 변환, 인벤/지갑 반영만 맡는다.
	public static class RewardGranter
	{
		// 공유 코어 추첨 결과 재사용 버퍼 — 지급은 메인 스레드 단일 경로다.
		private static readonly List<RolledReward> _rolled = new List<RolledReward>(8);

		// 그룹 하나를 굴려 즉시 지급하고 결과를 buffer 에 채운다(호출자가 버퍼를 소유).
		// buffer 를 비우지 않고 **누적**한다 — 고유 드랍 + 지역 드랍처럼 두 그룹을 이어 굴릴 수 있다.
		public static void Grant(int groupId, RewardContext context, List<GrantedReward> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			// 이번 호출로 새로 추가된 몫만 지급한다 — 누적된 앞부분을 다시 지급하면 안 된다.
			int start = buffer.Count;
			Roll(groupId, context, buffer);
			ApplyRange(buffer, start);
		}

		// 굴리기만 한다 — 인벤/지갑에 손대지 않는다. 지급은 ApplyAll 이 맡는다.
		// 추첨 규칙은 서버와 공유하는 RewardRoller 가 소유한다. 여기서는 결과를 장비 인스턴스로 옮기기만 한다.
		public static void Roll(int groupId, RewardContext context, List<GrantedReward> buffer)
		{
			// 골드 보너스는 **적 처치분에만** 곱한다 — 퀘스트·던전 클리어 보상은 고정값이다 (기반테이블 8.1).
			int currencyBonusPermille = (context == RewardContext.MonsterKill) ? GetGoldBonusPermille() : 0;
			RollWith(groupId, currencyBonusPermille, UnityRandomSource.Instance, buffer);
		}

		// 난수원과 재화 보너스를 직접 지정해 굴린다 — 필드 처치 배치 정산은 서버가 재현할 수 있는 시드 난수를 넘긴다.
		// buffer 에 추가되는 순서가 곧 추첨 결과 순서(서버 재현 기준 인덱스)다.
		public static void RollWith(int groupId, int currencyBonusPermille, IRandomSource rng, List<GrantedReward> buffer)
		{
			if (groupId <= 0 || buffer == null)
			{
				return;
			}

			_rolled.Clear();
			RewardRoller.Roll(groupId, currencyBonusPermille, rng, _rolled, logRoll);

			for (int i = 0; i < _rolled.Count; i++)
			{
				RolledReward rolled = _rolled[i];

				GrantedReward granted = default(GrantedReward);
				granted.type = rolled.type;
				granted.itemId = rolled.itemId;
				granted.currency = rolled.currency;
				granted.count = rolled.count;

				// 등급·품질은 공유 코어가 이미 정했다 — 같은 값으로 인스턴스만 만든다.
				if (rolled.isEquipment == true)
				{
					granted.equipment = EquipmentFactory.CreateExact(rolled.itemId, rolled.grade, rolled.quality);
					if (granted.equipment == null)
					{
						continue;
					}
				}

				buffer.Add(granted);
			}
		}

		// 굴려 둔 목록을 실제로 인벤/지갑에 반영한다. 변경 이벤트는 여기서 발행된다.
		public static void ApplyAll(List<GrantedReward> rewards)
		{
			ApplyRange(rewards, 0);
		}

		// 목록의 startIndex 이후만 반영한다.
		public static void ApplyRange(List<GrantedReward> rewards, int startIndex)
		{
			if (rewards == null)
			{
				return;
			}

			for (int i = startIndex; i < rewards.Count; i++)
			{
				applyOne(rewards[i]);
			}
		}

		// 서버가 UID·등급·품질을 확정한 장비를 같은 값의 인스턴스로 만든다(인벤에는 넣지 않는다).
		// 서버 USER_INVENTORY 와 UID 가 일치해야 이후 장착 저장(SaveLoadout)의 보유 검증을 통과한다.
		public static EquipmentInstance CreateServerEquipment(EquipmentInstanceDto src)
		{
			if (src == null || src.uid <= 0)
			{
				return null;
			}

			EquipmentInstance instance = EquipmentFactory.CreateExact(src.itemId, (ItemGradeType)src.grade, src.quality);
			if (instance == null)
			{
				return null;
			}

			instance.uid = src.uid;
			return instance;
		}

		// 서버 응답(이미 서버에 저장된 지급분)을 공용 지급 목록으로 옮긴다 — 장비는 서버 UID 그대로 인스턴스를 만든다.
		// 반영은 호출자가 ApplyAll 로 한다(획득 로그·보상 팝업이 같은 목록을 쓴다).
		public static void FromServer(GrantedRewardDto[] rewards, EquipmentInstanceDto[] equipments, List<GrantedReward> buffer)
		{
			if (equipments != null)
			{
				for (int i = 0; i < equipments.Length; i++)
				{
					EquipmentInstance instance = CreateServerEquipment(equipments[i]);
					if (instance == null)
					{
						continue;
					}

					GrantedReward reward = default(GrantedReward);
					reward.type = RewardType.Item;
					reward.itemId = instance.itemId;
					reward.count = 1;
					reward.equipment = instance;
					buffer.Add(reward);
				}
			}

			if (rewards != null)
			{
				for (int i = 0; i < rewards.Length; i++)
				{
					GrantedRewardDto dto = rewards[i];
					GrantedReward reward = default(GrantedReward);
					reward.type = (RewardType)dto.rewardType;
					reward.count = dto.count;
					if (reward.type == RewardType.Currency)
					{
						reward.currency = (EDT.Currency)dto.itemId;
					}
					else
					{
						reward.itemId = dto.itemId;
					}

					buffer.Add(reward);
				}
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void applyOne(GrantedReward granted)
		{
			if (granted.count <= 0)
			{
				return;
			}

			if (granted.type == RewardType.Currency)
			{
				CurrencyManager.Instance.Add(granted.currency, granted.count);
				EventManager.Instance.Publish(new RewardAcquiredEvent(granted.type, 0, granted.currency, granted.count, ItemGradeType.None, 0, false));
				return;
			}

			// 장비는 인스턴스가 이미 만들어져 있다 — 넣기만 하면 된다.
			if (granted.equipment != null)
			{
				Account.Instance.Inventory.AddEquipment(granted.equipment);
				EventManager.Instance.Publish(new RewardAcquiredEvent(granted.type, granted.itemId, EDT.Currency.None, 1, granted.equipment.grade, granted.equipment.quality, true));
				return;
			}

			// 수집품(펫·코스튬)은 인벤토리가 아니라 보유로 간다. 이미 가졌으면 지급도 로그도 없다(서버와 같은 규칙).
			if (CollectionRules.IsCollection(granted.itemId) == true)
			{
				if (grantCollection(granted.itemId) == true)
				{
					EventManager.Instance.Publish(new RewardAcquiredEvent(granted.type, granted.itemId, EDT.Currency.None, 1, ItemGradeType.None, 0, false));
				}

				return;
			}

			Account.Instance.Inventory.Add(granted.itemId, granted.count);
			EventManager.Instance.Publish(new RewardAcquiredEvent(granted.type, granted.itemId, EDT.Currency.None, granted.count, ItemGradeType.None, 0, false));
		}

		private static bool grantCollection(int itemId)
		{
			if (CollectionRules.IsPet(itemId) == true)
			{
				return Account.Instance.Pet.Grant(itemId);
			}

			if (CollectionRules.IsCostume(itemId) == true && Account.Instance.Costume.IsOwned(itemId) == false)
			{
				Account.Instance.Costume.Add(itemId);
				return true;
			}

			return false;
		}

		private static void logRoll(bool isError, string message)
		{
			if (isError == true)
			{
				Debug.LogError(message);
			}
			else
			{
				Debug.LogWarning(message);
			}
		}

		// 살아있는 히어로의 골드 획득량 보너스(퍼밀). 없으면 0.
		// 서버 재현과 같은 값을 쓰도록 실수 스탯을 여기서 한 번만 정수로 바꾼다.
		public static int GetGoldBonusPermille()
		{
			return Mathf.RoundToInt(getGoldDropBonus() * 1000f);
		}

		// 살아있는 히어로의 골드 획득량 보너스. 없으면 0.
		private static float getGoldDropBonus()
		{
			if (UnitManager.HasInstance == false)
			{
				return 0f;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.Stats != null)
				{
					return hero.Stats.GetStat(Stat.Stat_GoldDropBonus);
				}
			}

			return 0f;
		}
	}
}
