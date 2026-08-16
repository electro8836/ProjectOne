using System;
using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Items
{
	// Option 정적 조회 캐시 — Table_Option 의 문자열 OptionTarget 을 실제 enum 으로 1회 해석한다.
	//
	// Option 은 장비(EquipOption) · 스킬 트리(SkillTreeNode) · 마스터리(LevelBonusOption) 세 곳이 공유한다
	// (아이템 설계 3.12). 값은 언제나 부착처가 소유하고, Option 은 "무엇을 건드리는가"만 정의한다.
	//
	// StatCatalog / SkillParamCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class OptionCatalog
	{
		// 해석된 옵션 정의. 타입에 따라 유효한 필드가 다르다.
		public struct Entry
		{
			public OptionTypes type;
			public StatDetail statDetail;		// type == Stat
			public EDT.Skill skill;				// type == SkillGrant / SkillLevel
			public EDT.SkillModifier modifier;	// type == Modifier
		}

		private static readonly Dictionary<Option, Entry> _entries = new Dictionary<Option, Entry>();
		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_entries.Clear();

			int resolved = 0;
			Dictionary<Option, Table_Option.Row> all = Table_Option.All();
			Dictionary<Option, Table_Option.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Option.Row row = e.Current.Value;
				if (row.ID == Option.None)
				{
					continue;
				}

				Entry entry;
				if (tryResolve(row, out entry) == false)
				{
					continue;
				}

				_entries[row.ID] = entry;
				resolved++;
			}

			_built = true;
			Debug.Log($"[OptionCatalog] 구축 완료 — 해석 {resolved} / 전체 {all.Count}");
		}

		public static bool TryGet(Option option, out Entry entry)
		{
			if (_built == false)
			{
				Debug.LogError("[OptionCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				entry = default(Entry);
				return false;
			}

			return _entries.TryGetValue(option, out entry);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// OptionType 별로 OptionTarget 이 가리키는 시트가 다르다 (아이템 설계 3.12 표).
		private static bool tryResolve(Table_Option.Row row, out Entry entry)
		{
			entry = default(Entry);
			entry.type = row.OptionType;

			switch (row.OptionType)
			{
				case OptionTypes.Stat:
				{
					StatDetail detail;
					if (tryParse<StatDetail>(row, out detail) == false)
					{
						return false;
					}

					entry.statDetail = detail;
					return true;
				}

				case OptionTypes.SkillGrant:
				case OptionTypes.SkillLevel:
				{
					EDT.Skill skill;
					if (tryParse<EDT.Skill>(row, out skill) == false)
					{
						return false;
					}

					entry.skill = skill;
					return true;
				}

				case OptionTypes.Modifier:
				{
					EDT.SkillModifier modifier;
					if (tryParse<EDT.SkillModifier>(row, out modifier) == false)
					{
						return false;
					}

					entry.modifier = modifier;
					return true;
				}
			}

			Debug.LogWarning($"[OptionCatalog] OptionType 이 지정되지 않은 행: {row.ID}");
			return false;
		}

		private static bool tryParse<T>(Table_Option.Row row, out T value) where T : struct
		{
			value = default(T);
			if (string.IsNullOrEmpty(row.OptionTarget) == true)
			{
				Debug.LogWarning($"[OptionCatalog] OptionTarget 이 비어 있음: {row.ID}");
				return false;
			}

			if (Enum.TryParse<T>(row.OptionTarget, false, out value) == false)
			{
				Debug.LogError($"[OptionCatalog] OptionTarget '{row.OptionTarget}' 을 {typeof(T).Name} 으로 해석하지 못했습니다: {row.ID}");
				return false;
			}

			return true;
		}
	}
}
