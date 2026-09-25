using System.Collections.Generic;
using BackEnd;
using EDT;
using ProjectOne.Costumes;
using ProjectOne.Network;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.UserData;

namespace ProjectOne.Ranking
{
	// 내 Account 를 플레이어 정보 스냅샷으로 옮긴다. 랭킹 출처가 무엇이든 "나" 는 여기서 만든다.
	public static class MyPlayerProfile
	{
		// 비로그인 상태의 내 식별자. 로그인 중이면 뒤끝 inDate 를 쓴다.
		private const string LocalPlayerId = "local";

		// 닉네임 기능이 없어 임시 이름을 쓴다 — 닉네임이 생기면 교체한다.
		private const string TempNamePrefix = "Player";
		private const int TempNameSuffixLength = 4;

		public static string PlayerId
		{
			get
			{
				if (isLoggedIn() == false)
				{
					return LocalPlayerId;
				}

				return Backend.UserInDate;
			}
		}

		// "Player" + inDate 끝 4자리. 비로그인이면 "Player".
		public static string PlayerName
		{
			get
			{
				if (isLoggedIn() == false)
				{
					return TempNamePrefix;
				}

				string inDate = Backend.UserInDate;
				if (string.IsNullOrEmpty(inDate) == true || inDate.Length < TempNameSuffixLength)
				{
					return TempNamePrefix;
				}

				return TempNamePrefix + inDate.Substring(inDate.Length - TempNameSuffixLength);
			}
		}

		// 최고 전투력은 서버 저장이 생길 때까지 현재 전투력으로 대신한다.
		// 살아 있는 히어로가 없으면(씬 전환 중) 0.
		public static int BattlePower
		{
			get
			{
				UnitBase hero = findAliveHero();
				if (hero == null || hero.Stats == null)
				{
					return 0;
				}

				return BattlePowerCalculator.Calculate(hero.Stats, hero.SkillContainer);
			}
		}

		public static PlayerProfile Build()
		{
			PlayerProfile profile = new PlayerProfile();
			profile.playerId = PlayerId;
			profile.playerName = PlayerName;
			profile.battlePower = BattlePower;

			Loadout loadout = Account.Instance.Loadout;
			if (loadout != null)
			{
				profile.level = loadout.Level;
				for (int i = 1; i < profile.equipped.Length; i++)
				{
					profile.equipped[i] = loadout.GetEquipped((EquipSlotTypes)i);
				}
			}

			CostumeBook costume = Account.Instance.Costume;
			if (costume != null)
			{
				profile.weaponCostumeId = costume.EquippedWeaponId;
				profile.bodyCostumeId = costume.EquippedBodyId;
			}

			return profile;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static bool isLoggedIn()
		{
			return NetworkManager.Instance.IsLoggedIn;
		}

		// EquipmentPresenter.findAliveHero 와 같은 방식.
		private static UnitBase findAliveHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.IsDead == false)
				{
					return hero;
				}
			}

			return null;
		}
	}
}
