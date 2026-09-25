using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;

namespace ProjectOne.Ranking
{
	// [임시] 서버 랭킹이 생기기 전까지 쓰는 가짜 데이터.
	//
	// 시드를 고정해 열 때마다 같은 목록·같은 장비가 나온다. 내 줄은 실제 Account 값으로 끼워 넣는다 —
	// 가짜 목록 안에서 내 전투력이 들어갈 자리를 찾고, 100위 밖이면 집계 밖 순위를 줘서 "---" 표시까지 확인할 수 있게 한다.
	public sealed class DummyRankingProvider : IRankingProvider
	{
		private const int DummyCount = 100;
		private const int TopCount = 100;
		private const int Seed = 20260925;

		// 100위 밖일 때 내게 줄 가짜 순위 — 집계 한도(10000위)를 넘겨 "---" 로 보이게 한다.
		private const int OutOfTopRank = 12345;

		private const int TopBattlePower = 500000;
		private const int MinPowerStep = 500;
		private const int MaxPowerStep = 3000;

		private const int MinLevel = 10;
		private const int MaxLevel = 80;
		private const int MaxEquipLevel = 20;
		private const int MaxQuality = 100;

		private const string DummyIdPrefix = "dummy_";
		private const string DummyNamePrefix = "모험가";

		private readonly List<RankEntry> _dummies = new List<RankEntry>(DummyCount);
		private readonly Dictionary<string, PlayerProfile> _profiles = new Dictionary<string, PlayerProfile>();

		// 부위별 장비 후보(아이템 ID). 가짜 프로필의 장비를 여기서 뽑는다.
		private readonly Dictionary<EquipSlotTypes, List<int>> _equipCandidates = new Dictionary<EquipSlotTypes, List<int>>();

		public UniTask<RankingResult> GetRankingAsync(CancellationToken ct)
		{
			buildDummies();

			string myId = MyPlayerProfile.PlayerId;
			string myName = MyPlayerProfile.PlayerName;
			int myPower = MyPlayerProfile.BattlePower;

			RankingResult result = new RankingResult();
			bool inserted = false;
			int rank = 1;

			for (int i = 0; i < _dummies.Count && result.top.Count < TopCount; i++)
			{
				// 전투력이 같으면 먼저 등록된 쪽이 위다 — 나는 같은 값 뒤에 선다.
				if (inserted == false && myPower > _dummies[i].battlePower)
				{
					result.mine = new RankEntry(myId, rank, myName, myPower);
					result.top.Add(result.mine);
					inserted = true;
					rank++;

					if (result.top.Count >= TopCount)
					{
						break;
					}
				}

				RankEntry dummy = _dummies[i];
				result.top.Add(new RankEntry(dummy.playerId, rank, dummy.playerName, dummy.battlePower));
				rank++;
			}

			if (inserted == false)
			{
				result.mine = new RankEntry(myId, OutOfTopRank, myName, myPower);
			}

			return UniTask.FromResult(result);
		}

		public UniTask<PlayerProfile> GetProfileAsync(string playerId, CancellationToken ct)
		{
			if (playerId == MyPlayerProfile.PlayerId)
			{
				return UniTask.FromResult(MyPlayerProfile.Build());
			}

			PlayerProfile profile;
			if (_profiles.TryGetValue(playerId, out profile) == false)
			{
				profile = buildDummyProfile(playerId);
				_profiles.Add(playerId, profile);
			}

			return UniTask.FromResult(profile);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildDummies()
		{
			if (_dummies.Count > 0)
			{
				return;
			}

			System.Random random = new System.Random(Seed);
			int power = TopBattlePower;

			for (int i = 0; i < DummyCount; i++)
			{
				string id = DummyIdPrefix + i.ToString();
				string name = DummyNamePrefix + (i + 1).ToString("000");
				_dummies.Add(new RankEntry(id, 0, name, power));

				power -= random.Next(MinPowerStep, MaxPowerStep + 1);
			}
		}

		// 가짜 유저마다 시드를 달리해, 같은 유저는 몇 번을 열어도 같은 장비가 나온다.
		private PlayerProfile buildDummyProfile(string playerId)
		{
			buildEquipCandidates();

			int index = findDummyIndex(playerId);
			RankEntry entry = (index >= 0) ? _dummies[index] : new RankEntry(playerId, 0, string.Empty, 0);
			System.Random random = new System.Random(Seed + index + 1);

			PlayerProfile profile = new PlayerProfile();
			profile.playerId = playerId;
			profile.playerName = entry.playerName;
			profile.battlePower = entry.battlePower;
			profile.level = random.Next(MinLevel, MaxLevel + 1);

			for (int i = 1; i < profile.equipped.Length; i++)
			{
				profile.equipped[i] = rollEquipment((EquipSlotTypes)i, random);
			}

			EquipmentInstance weapon = profile.equipped[(int)EquipSlotTypes.Weapon];
			profile.weaponCostumeId = rollCostume(CostumeType.Weapon, weapon, random);
			profile.bodyCostumeId = rollCostume(CostumeType.Body, null, random);

			return profile;
		}

		private int findDummyIndex(string playerId)
		{
			for (int i = 0; i < _dummies.Count; i++)
			{
				if (_dummies[i].playerId == playerId)
				{
					return i;
				}
			}

			return -1;
		}

		private void buildEquipCandidates()
		{
			if (_equipCandidates.Count > 0)
			{
				return;
			}

			Dictionary<int, Table_Equipment.Row> all = Table_Equipment.All();
			Dictionary<int, Table_Equipment.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Equipment.Row row = e.Current.Value;
				if (row.EquipSlotType == EquipSlotTypes.None || Table_Item.Get(row.ID) == null)
				{
					continue;
				}

				List<int> list;
				if (_equipCandidates.TryGetValue(row.EquipSlotType, out list) == false)
				{
					list = new List<int>();
					_equipCandidates.Add(row.EquipSlotType, list);
				}

				list.Add(row.ID);
			}

			// Dictionary 순회 순서에 기대지 않도록 정렬해 둔다 — 시드가 같아도 다른 장비가 나오면 안 된다.
			Dictionary<EquipSlotTypes, List<int>>.Enumerator c = _equipCandidates.GetEnumerator();
			while (c.MoveNext() == true)
			{
				c.Current.Value.Sort();
			}
		}

		private EquipmentInstance rollEquipment(EquipSlotTypes slot, System.Random random)
		{
			List<int> candidates;
			if (_equipCandidates.TryGetValue(slot, out candidates) == false || candidates.Count == 0)
			{
				return null;
			}

			int itemId = candidates[random.Next(candidates.Count)];
			Table_Item.Row item = Table_Item.Get(itemId);
			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);

			// 등급은 아이템의 시작 등급 ~ 최대 등급 사이에서 뽑는다.
			int minGrade = (int)item.Grade;
			int maxGrade = (int)equipment.MaxGrade;
			if (maxGrade < minGrade)
			{
				maxGrade = minGrade;
			}

			ItemGradeType grade = (ItemGradeType)random.Next(minGrade, maxGrade + 1);
			EquipmentInstance instance = EquipmentFactory.CreateExact(itemId, grade, random.Next(0, MaxQuality + 1));
			if (instance == null)
			{
				return null;
			}

			instance.level = random.Next(1, MaxEquipLevel + 1);
			instance.equippedSlot = slot;
			return instance;
		}

		// 착용 가능한 코스튬 중 하나. 무기 코스튬은 장비 무기와 종류가 맞는 것만 고른다. 없으면 0(미착용).
		private int rollCostume(CostumeType type, EquipmentInstance weapon, System.Random random)
		{
			List<int> candidates = new List<int>();

			Dictionary<int, Table_Costume.Row> all = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Costume.Row row = e.Current.Value;
				if (row.CostumeType != type)
				{
					continue;
				}

				if (type == CostumeType.Weapon)
				{
					Table_Equipment.Row equipment = (weapon != null) ? weapon.Equipment : null;
					if (equipment == null || row.WeaponType != equipment.WeaponType)
					{
						continue;
					}
				}

				candidates.Add(row.ID);
			}

			if (candidates.Count == 0)
			{
				return 0;
			}

			candidates.Sort();
			return candidates[random.Next(candidates.Count)];
		}
	}
}
