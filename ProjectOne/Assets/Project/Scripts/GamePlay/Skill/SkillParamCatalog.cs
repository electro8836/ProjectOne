using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Skill
{
	// SkillEffect 파라미터 슬롯 정의 캐시 — Table_SkillParamDef(45행)를 런타임 조회용으로 인덱싱한다.
	//
	// SkillEffect.EffectParam_1~5 는 전부 문자열이고, 각 칸의 의미와 타입은
	// (EffectType, Index) 조합이 결정한다. 모디파이어가 슬롯을 "이름"으로 지목하므로
	// (EffectType, ParamKey) → Index 역방향 인덱스도 함께 만든다.
	//
	// StatCatalog 와 동일하게 테이블 로드 이후 Build() 가 호출되어야 한다.
	// 부트 순서: TableBootLoader.LoadAllAsync → StatCatalog.Build() → SkillParamCatalog.Build()
	public static class SkillParamCatalog
	{
		// 슬롯 1칸의 정의 (테이블 행의 런타임 표현)
		public readonly struct ParamDef
		{
			public readonly string Key;
			public readonly ParamDefValueTypes ValueType;

			public ParamDef(string key, ParamDefValueTypes valueType)
			{
				Key = key;
				ValueType = valueType;
			}

			public bool IsDefined
			{
				get { return string.IsNullOrEmpty(Key) == false; }
			}
		}

		// EffectType 당 슬롯은 5칸 고정 (EffectParam_1~5)
		public const int SlotCount = 5;

		// (EffectType, Index 1~5) → 정의. 인덱스 = (int)EffectType * SlotCount + (Index - 1)
		private static readonly Dictionary<int, ParamDef> _byIndex = new Dictionary<int, ParamDef>();

		// (EffectType, ParamKey) → Index. 모디파이어가 이름으로 슬롯을 찾을 때 쓴다.
		private static readonly Dictionary<string, int> _byKey = new Dictionary<string, int>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byIndex.Clear();
			_byKey.Clear();

			Dictionary<int, Table_SkillParamDef.Row> all = Table_SkillParamDef.All();
			Dictionary<int, Table_SkillParamDef.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillParamDef.Row row = e.Current.Value;
				if (row.EffectType == SkillEffectTypes.None)
				{
					continue;
				}

				if (row.Index < 1 || row.Index > SlotCount)
				{
					Debug.LogError($"[SkillParamCatalog] Index 범위를 벗어난 슬롯 정의: {row.EffectType} Index={row.Index}");
					continue;
				}

				if (string.IsNullOrEmpty(row.ParamKey) == true)
				{
					continue;	// 미사용 슬롯 — 정의가 비어 있는 것이 정상이다
				}

				_byIndex[slotHash(row.EffectType, row.Index)] = new ParamDef(row.ParamKey, row.ValueType);
				_byKey[keyHash(row.EffectType, row.ParamKey)] = row.Index;
			}

			_built = true;
			Debug.Log($"[SkillParamCatalog] 구축 완료 — 정의된 슬롯 {_byIndex.Count}개 / 전체 행 {all.Count}");
		}

		// (EffectType, Index) 슬롯의 정의. 미정의 슬롯이면 IsDefined == false 인 값을 돌려준다.
		public static ParamDef GetDef(SkillEffectTypes effectType, int index)
		{
			if (_built == false)
			{
				Debug.LogError("[SkillParamCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				return default(ParamDef);
			}

			ParamDef def;
			_byIndex.TryGetValue(slotHash(effectType, index), out def);
			return def;
		}

		// ParamKey 로 슬롯 번호(1~5)를 찾는다. 없으면 -1.
		public static int GetIndex(SkillEffectTypes effectType, string paramKey)
		{
			if (_built == false || string.IsNullOrEmpty(paramKey) == true)
			{
				return -1;
			}

			int index;
			if (_byKey.TryGetValue(keyHash(effectType, paramKey), out index) == false)
			{
				return -1;
			}

			return index;
		}

		// 정의되지 않은 슬롯에 값이 있으면 경고 — 한 칸 밀려 적은 데이터를 잡는 유일한 장치다 (설계 5.6).
		public static void WarnUndefinedSlots(Table_SkillEffect.Row row)
		{
			if (_built == false || row == null)
			{
				return;
			}

			for (int i = 1; i <= SlotCount; i++)
			{
				string value = GetRawParam(row, i);
				if (string.IsNullOrEmpty(value) == true)
				{
					continue;
				}

				if (GetDef(row.EffectType, i).IsDefined == false)
				{
					Debug.LogWarning($"[SkillParamCatalog] 정의되지 않은 슬롯에 값이 있음 — Effect:{row.ID} Type:{row.EffectType} Param_{i}=\"{value}\"");
				}
			}
		}

		// 슬롯 번호(1~5)로 원본 문자열을 꺼낸다.
		public static string GetRawParam(Table_SkillEffect.Row row, int index)
		{
			if (row == null)
			{
				return string.Empty;
			}

			switch (index)
			{
				case 1: return row.EffectParam_1;
				case 2: return row.EffectParam_2;
				case 3: return row.EffectParam_3;
				case 4: return row.EffectParam_4;
				case 5: return row.EffectParam_5;
				default: return string.Empty;
			}
		}

		// ParamKey 로 원본 문자열을 꺼낸다. 정의가 없으면 빈 문자열.
		public static string GetRawParamByKey(Table_SkillEffect.Row row, string paramKey)
		{
			if (row == null)
			{
				return string.Empty;
			}

			int index = GetIndex(row.EffectType, paramKey);
			if (index < 0)
			{
				return string.Empty;
			}

			return GetRawParam(row, index);
		}

		private static int slotHash(SkillEffectTypes effectType, int index)
		{
			return (int)effectType * SlotCount + (index - 1);
		}

		private static string keyHash(SkillEffectTypes effectType, string paramKey)
		{
			return (int)effectType + "|" + paramKey;
		}
	}
}
