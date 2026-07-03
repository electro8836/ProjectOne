using System.Collections.Generic;
using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 클리어한 던전 ID 기록 도메인. 카드스킬 해금 등 진행도 게이트에 사용한다.
	// 영속은 서버(Backnd USER_DUNGEON)가 담당한다. GetUserData 로 SetClearedDungeons(dto) 주입,
	// 던전 클리어 시 DungeonClear 함수가 서버에 기록한다. 클라는 세션 내 즉시 해금용으로 인메모리만 유지한다.
	public sealed class ClearedDungeons
	{
		private readonly HashSet<int> _ids = new HashSet<int>();

		public ClearedDungeons(ClearedDungeonsDto dto)
		{
			buildFromDto(dto);
		}

		// ── 공개 API ──────────────────────────────────────────────────

		public bool IsCleared(int dungeonId)
		{
			return _ids.Contains(dungeonId);
		}

		// 클리어 기록 추가 — 세션 내 즉시 해금용 인메모리 반영(영속은 서버 DungeonClear 담당).
		public void MarkCleared(int dungeonId)
		{
			if (dungeonId <= 0)
			{
				return;
			}

			_ids.Add(dungeonId);
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public ClearedDungeonsDto ToDto()
		{
			ClearedDungeonsDto dto = new ClearedDungeonsDto();
			dto.dungeonIds.AddRange(_ids);
			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildFromDto(ClearedDungeonsDto dto)
		{
			_ids.Clear();

			// 서버 DTO 주입 — 없으면 빈 상태(미로그인/오프라인).
			if (dto == null || dto.dungeonIds == null)
			{
				return;
			}

			for (int i = 0; i < dto.dungeonIds.Count; i++)
			{
				int id = dto.dungeonIds[i];
				if (id > 0)
				{
					_ids.Add(id);
				}
			}
		}
	}
}
