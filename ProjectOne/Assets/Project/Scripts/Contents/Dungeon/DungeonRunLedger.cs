using System.Collections.Generic;
using EDT;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 한 판(런)의 원장 — 시드 기반 정산의 클라 쪽 기록과 결과창 합산.
	//
	// 1) 미궁 상자 — 입장 때 서버가 발급한 런 시드로 상자를 굴려 바닥에 떨군다(DropManager.SpawnChestDrops).
	//    주운 보상 비트(pickedMask)를 상자마다 남기고, 런이 끝나면 DungeonDirector 가 목록을 DungeonClear 에 실어 보낸다.
	//    서버는 같은 시드로 재현해 주운 것만 저장한다. 장비 UID 도 (런, 상자, 보상 순서)로 서버와 같은 값을 쓴다.
	// 2) 결과창 합산 — 런 중 실제로 주운 보상(처치 드랍 + 상자 드랍)을 모은다. 결과창은 여기에 클리어 보상을 더해 보여준다.
	//
	// 서버 런이 없으면(미로그인·개발용 이동) 임의 시드의 로컬 런이다 — 드랍·합산은 같고 서버로 보내지 않는다.
	public sealed class DungeonRunLedger : Singleton<DungeonRunLedger>
	{
		// pickedMask(long) 의 비트 수 — 상자 1개에서 추적할 수 있는 보상 수 상한.
		public const int MaxTrackedRewards = 64;

		private readonly List<DungeonChestDto> _chests = new List<DungeonChestDto>();
		private readonly List<GrantedReward> _picked = new List<GrantedReward>();

		private int _runId;
		private long _seed;
		private bool _isServerRun;
		private bool _isActive;

		private DungeonRunLedger() { }

		// 서버가 발급한 런으로 정산하는가 — false 면 로컬 지급만 하고 정산 요청을 보내지 않는다.
		public bool IsServerRun
		{
			get { return _isServerRun; }
		}

		public int RunId
		{
			get { return _runId; }
		}

		// 런 중 주운 보상(처치 드랍 + 상자 드랍). 결과창 합산용이다.
		public List<GrantedReward> PickedRewards
		{
			get { return _picked; }
		}

		// 던전 시작(DungeonDirector.Begin). run 이 null 이면 로컬 런이다.
		public void Begin(DungeonRunDto run)
		{
			_chests.Clear();
			_picked.Clear();
			_isActive = true;
			_isServerRun = run != null;
			_runId = (run != null) ? run.runId : 0;
			_seed = (run != null) ? run.seed : ((long)UnityEngine.Random.Range(int.MinValue, int.MaxValue) << 32) | (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
		}

		// 던전 종료(정리). 이후의 획득(필드)은 기록하지 않는다.
		public void End()
		{
			_isActive = false;
		}

		// 상자 1개를 연다 — 런 시드로 굴려 buffer 에 결과를 채운다(지급하지 않는다 — 바닥 드랍을 주울 때 지급된다).
		// 같은 상자를 두 번 열면 false.
		public bool OpenChest(int chestIndex, DungeonChestGrade grade, int groupId, List<GrantedReward> buffer)
		{
			buffer.Clear();
			if (findChest(chestIndex) != null)
			{
				return false;
			}

			DeterministicRandom rng = new DeterministicRandom(DungeonRules.ChestSeed(_seed, chestIndex, groupId));
			RewardGranter.RollWith(groupId, 0, rng, buffer);

			// 서버 런이면 장비 UID 를 상자 좌표로 정한다 — 서버가 같은 값으로 저장해야 이후 장착 저장이 통과한다.
			if (_isServerRun == true)
			{
				for (int i = 0; i < buffer.Count; i++)
				{
					if (buffer[i].equipment != null)
					{
						buffer[i].equipment.uid = EquipmentUid.ForDungeonChest(_runId, chestIndex, i);
					}
				}
			}

			if (buffer.Count > MaxTrackedRewards)
			{
				UnityEngine.Debug.LogError($"[DungeonRunLedger] 상자 1개의 보상이 {buffer.Count}개 — {MaxTrackedRewards}개 초과분은 서버에 정산되지 않는다.");
			}

			DungeonChestDto chest = new DungeonChestDto();
			chest.chestIndex = chestIndex;
			chest.grade = (int)grade;
			_chests.Add(chest);
			return true;
		}

		// 상자 드랍 하나를 주웠다 — 정산 비트를 남긴다.
		public void MarkChestPicked(int chestIndex, int rewardIndex)
		{
			DungeonChestDto chest = findChest(chestIndex);
			if (chest == null || rewardIndex < 0 || rewardIndex >= MaxTrackedRewards)
			{
				return;
			}

			chest.pickedMask |= 1L << rewardIndex;
		}

		// 바닥 드랍을 주웠다 — 결과창 합산에 더한다. 던전 밖(필드)에서는 무시한다.
		public void RecordPicked(List<GrantedReward> rewards)
		{
			if (_isActive == false || rewards == null)
			{
				return;
			}

			_picked.AddRange(rewards);
		}

		// 정산 요청에 실을 연 상자 목록.
		public DungeonChestDto[] GetChests()
		{
			return _chests.ToArray();
		}

		private DungeonChestDto findChest(int chestIndex)
		{
			for (int i = 0; i < _chests.Count; i++)
			{
				if (_chests[i].chestIndex == chestIndex)
				{
					return _chests[i];
				}
			}

			return null;
		}
	}
}
