using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 클리어한 던전 ID 기록 도메인. 카드스킬 해금 등 진행도 게이트에 사용한다.
	// 현재는 로컬(PlayerPrefs) 영속 — 서버 연동 시 SetClearedDungeons(dto) 주입으로 전환한다(PlayerPrefs 제거 예정).
	public sealed class ClearedDungeons
	{
		private const string PrefsKey = "ClearedDungeons";

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

		// 클리어 기록 추가 — 새로 추가됐을 때만 로컬 영속(PlayerPrefs)에 저장.
		public void MarkCleared(int dungeonId)
		{
			if (dungeonId <= 0)
			{
				return;
			}

			if (_ids.Add(dungeonId) == true)
			{
				saveToPrefs();
			}
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

			// 서버 DTO 가 주어지면 그것을, 아니면 로컬 영속(PlayerPrefs)을 로드한다.
			if (dto != null && dto.dungeonIds != null)
			{
				for (int i = 0; i < dto.dungeonIds.Count; i++)
				{
					int id = dto.dungeonIds[i];
					if (id > 0)
					{
						_ids.Add(id);
					}
				}

				return;
			}

			loadFromPrefs();
		}

		// PlayerPrefs 에 쉼표 구분 정수로 보관 (예: "1,3,5")
		private void loadFromPrefs()
		{
			string raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
			if (string.IsNullOrEmpty(raw) == true)
			{
				return;
			}

			string[] tokens = raw.Split(',');
			for (int i = 0; i < tokens.Length; i++)
			{
				int id;
				if (int.TryParse(tokens[i], out id) == true && id > 0)
				{
					_ids.Add(id);
				}
			}
		}

		private void saveToPrefs()
		{
			StringBuilder sb = new StringBuilder();
			bool first = true;
			HashSet<int>.Enumerator e = _ids.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (first == false)
				{
					sb.Append(',');
				}

				sb.Append(e.Current);
				first = false;
			}

			PlayerPrefs.SetString(PrefsKey, sb.ToString());
			PlayerPrefs.Save();
		}
	}
}
