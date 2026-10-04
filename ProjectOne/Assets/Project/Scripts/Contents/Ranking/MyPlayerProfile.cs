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

		// 뒤끝 계정 닉네임 — 로그인 데이터(GetUserData)로 받는다. 클라 SDK 의 UserNickName 은 로그인 시점 캐시라
		// 서버가 방금 부여한 닉네임을 모르므로 응답값을 쓴다.
		private static string _nickname = string.Empty;

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

		// 비로그인이거나 아직 닉네임이 없으면 빈 문자열.
		public static string PlayerName
		{
			get { return _nickname; }
		}

		public static void SetNickname(string nickname)
		{
			_nickname = (nickname != null) ? nickname : string.Empty;
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
