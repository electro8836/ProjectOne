using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Upgrade;

namespace ProjectOne.Ranking
{
	// 뒤끝 유저 리더보드(전투력, 누적). 목록·정보 모두 서버 펑션이 읽어 준다.
	//
	// 순위는 서버가 계산한 최고 전투력 기준이다. 전투력이 바뀌는 펑션(장착·강화 등)이 서버에서 스스로 갱신하므로
	// 클라는 점수를 보내지 않는다. 목록은 잠시 캐시하고, 유저 정보는 누를 때마다 서버에서 읽는다.
	// 내 정보는 지금 Account 로 그린다.
	//
	// 뒤끝 리더보드는 한 번에 50명까지만 읽힌다 — 처음 50명을 받고, 화면이 요청하면 100위까지 이어 받는다.
	public sealed class BackndRankingProvider : IRankingProvider
	{
		// 목록 캐시 유효 시간 — 순위 변동은 이 시간이 지나야 보인다.
		private const float CacheSeconds = 300f;

		// 한 번에 받는 줄 수(서버 PageSize 와 같다)와 화면에 보여 주는 상한.
		private const int PageSize = 50;
		private const int ListLimit = 100;

		// 마지막 목록 응답. 실패한 응답은 담지 않는다.
		private RankingResult _cached;
		private float _fetchedAt = float.NegativeInfinity;

		// 목록에서 받은 이름 — 정보 스냅샷에는 닉네임이 없어 여기서 채운다.
		private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

		// 같은 action 은 응답 전 중복 호출이 버려진다(BackndFunctionCaller) — 동시에 들어온 목록 요청은 한 응답을 나눠 기다린다.
		private UniTaskCompletionSource<RankingResult> _listTcs;

		// 이어 받기 — 마지막으로 받은 페이지가 꽉 찼으면 다음이 있다. 진행 중인 요청은 하나를 나눠 기다린다.
		private bool _hasMore;
		private UniTaskCompletionSource<bool> _moreTcs;

		public bool HasMore
		{
			get { return _hasMore; }
		}

		public UniTask<RankingResult> GetRankingAsync(CancellationToken ct)
		{
			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				return UniTask.FromResult(buildMineOnly());
			}

			// 이어 받는 중이면 첫 페이지 요청이 같은 action 이라 버려진다 — 캐시가 만료됐어도 지금 목록을 쓴다.
			if (_cached != null && (_moreTcs != null || Time.realtimeSinceStartup - _fetchedAt < CacheSeconds))
			{
				return UniTask.FromResult(_cached);
			}

			if (_listTcs == null)
			{
				_listTcs = new UniTaskCompletionSource<RankingResult>();

				// 서버가 목록을 읽기 전에 내 점수를 다시 계산한다 — 미저장 장착·외형·강화를 먼저 보낸다(SendQueue 순서).
				NetworkManager.Instance.FlushLoadoutIfDirty();
				NetworkManager.Instance.FlushAppearanceIfDirty();
				EnhanceBatcher.Instance.Flush();
				NetworkManager.Instance.RequestRankList(0, onListReceived, true);
			}

			return _listTcs.Task.AttachExternalCancellation(ct);
		}

		public UniTask<bool> LoadMoreAsync(CancellationToken ct)
		{
			if (_hasMore == false || _cached == null || _listTcs != null)
			{
				return UniTask.FromResult(false);
			}

			if (_moreTcs == null)
			{
				_moreTcs = new UniTaskCompletionSource<bool>();
				NetworkManager.Instance.RequestRankList(_cached.top.Count, onMoreReceived, false);
			}

			return _moreTcs.Task.AttachExternalCancellation(ct);
		}

		public UniTask<PlayerProfile> GetProfileAsync(string playerId, CancellationToken ct)
		{
			if (playerId == MyPlayerProfile.PlayerId)
			{
				return UniTask.FromResult(MyPlayerProfile.Build());
			}

			UniTaskCompletionSource<PlayerProfile> tcs = new UniTaskCompletionSource<PlayerProfile>();
			string playerName;
			_names.TryGetValue(playerId, out playerName);
			ProfileHandler handler = new ProfileHandler(tcs, playerId, playerName);

			RankProfileRequest request = new RankProfileRequest();
			request.inDate = playerId;
			NetworkManager.Instance.RequestRankProfile(request, handler.OnReceived);

			return tcs.Task.AttachExternalCancellation(ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 실패하면 내 줄만 그린다(실패 안내는 4단계).
		private void onListReceived(bool success, RankListResponse data, string error)
		{
			UniTaskCompletionSource<RankingResult> tcs = _listTcs;
			_listTcs = null;

			RankingResult result = buildMineOnly();
			if (success == false || data == null || data.top == null)
			{
				Debug.LogWarning($"[BackndRankingProvider] 랭킹 조회 실패: {error}");
				tcs.TrySetResult(result);
				return;
			}

			// 목록은 왔지만 내 점수 갱신이 실패한 경우 — 서버가 원인을 실어 보낸다.
			if (string.IsNullOrEmpty(error) == false)
			{
				Debug.LogWarning($"[BackndRankingProvider] 내 랭킹 갱신 실패: {error}");
			}

			_names.Clear();
			addEntries(result, data.top);
			_hasMore = data.top.Length >= PageSize && result.top.Count < ListLimit;

			// 리더보드에 올라가 있으면 서버 기록(최고 전투력)으로 그린다.
			if (data.mine != null && data.mine.rank > 0)
			{
				MyPlayerProfile.SetBestBattlePower(data.mine.battlePower);
				result.mine = new RankEntry(MyPlayerProfile.PlayerId, data.mine.rank, MyPlayerProfile.PlayerName, data.mine.battlePower);
			}

			_cached = result;
			_fetchedAt = Time.realtimeSinceStartup;
			tcs.TrySetResult(result);
		}

		// 다음 페이지 — 캐시된 목록 뒤에 붙인다. 실패하면 목록은 그대로 두고, 다음 요청 때 다시 받는다.
		private void onMoreReceived(bool success, RankListResponse data, string error)
		{
			UniTaskCompletionSource<bool> tcs = _moreTcs;
			_moreTcs = null;

			if (success == false || data == null || data.top == null)
			{
				Debug.LogWarning($"[BackndRankingProvider] 랭킹 이어 받기 실패: {error}");
				tcs.TrySetResult(false);
				return;
			}

			addEntries(_cached, data.top);
			_hasMore = data.top.Length >= PageSize && _cached.top.Count < ListLimit;
			tcs.TrySetResult(data.top.Length > 0);
		}

		// 상한(100위)을 넘는 줄은 버린다.
		private void addEntries(RankingResult result, RankEntryDto[] entries)
		{
			for (int i = 0; i < entries.Length && result.top.Count < ListLimit; i++)
			{
				RankEntryDto entry = entries[i];
				result.top.Add(new RankEntry(entry.inDate, entry.rank, entry.nickname, entry.battlePower));
				_names[entry.inDate] = entry.nickname;
			}
		}

		// 내 줄만 있는 결과 — 순위 0(순위 외)과 지금 전투력.
		private static RankingResult buildMineOnly()
		{
			RankingResult result = new RankingResult();
			result.mine = new RankEntry(MyPlayerProfile.PlayerId, 0, MyPlayerProfile.PlayerName, MyPlayerProfile.BattlePower);
			return result;
		}

		private static PlayerProfile toProfile(string playerId, string playerName, PlayerProfileDto dto)
		{
			PlayerProfile profile = new PlayerProfile();
			profile.playerId = playerId;
			profile.playerName = playerName;
			profile.level = dto.level;
			profile.masteryLevel = dto.masteryLevel;
			profile.battlePower = dto.battlePower;
			profile.bestBattlePower = dto.bestBattlePower;
			profile.weaponCostumeId = dto.weaponCostumeId;
			profile.bodyCostumeId = dto.bodyCostumeId;

			if (dto.equipped == null)
			{
				return profile;
			}

			for (int i = 0; i < dto.equipped.Count; i++)
			{
				EquipmentInstanceDto src = dto.equipped[i];
				if (src == null || src.itemId <= 0 || src.equippedSlot <= 0 || src.equippedSlot >= profile.equipped.Length)
				{
					continue;
				}

				EquipmentInstance instance = new EquipmentInstance();
				instance.uid = src.uid;
				instance.itemId = src.itemId;
				instance.grade = (ItemGradeType)src.grade;
				instance.level = src.level > 0 ? src.level : 1;
				instance.quality = src.quality;
				instance.equippedSlot = (EquipSlotTypes)src.equippedSlot;

				profile.equipped[src.equippedSlot] = instance;
			}

			return profile;
		}

		// 정보 응답 1건의 처리 — 콜백을 메서드 그룹으로 넘기기 위해 상태를 묶는다.
		private sealed class ProfileHandler
		{
			private readonly UniTaskCompletionSource<PlayerProfile> _tcs;
			private readonly string _playerId;
			private readonly string _playerName;

			public ProfileHandler(UniTaskCompletionSource<PlayerProfile> tcs, string playerId, string playerName)
			{
				_tcs = tcs;
				_playerId = playerId;
				_playerName = playerName;
			}

			// 실패하면 null — 팝업을 띄우지 않는다.
			public void OnReceived(bool success, RankProfileResponse data, string error)
			{
				if (success == false || data == null || data.profile == null)
				{
					Debug.LogWarning($"[BackndRankingProvider] 유저 정보 조회 실패: {error}");
					_tcs.TrySetResult(null);
					return;
				}

				_tcs.TrySetResult(toProfile(_playerId, _playerName, data.profile));
			}
		}
	}
}
