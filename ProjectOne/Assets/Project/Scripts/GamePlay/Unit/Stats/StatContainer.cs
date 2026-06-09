using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Unit.Stats
{
	// 유닛 스탯 컨테이너 (POCO)
	// 공식: Final = (Base + Add) * (1 + Ratio) * (1 + Amp)
	// - StatInfo 키 하나로 그룹/파트 모두 표현 (예: ATK, ATK_Base, ATK_Add, ATK_Ratio, ATK_Amp)
	// - 변경된 그룹만 dirty 표시 → GetStat 시 lazy 재계산
	public sealed class StatContainer
	{
		sealed class Bucket
		{
			public float Base;
			public float Add;
			public float Ratio;
			public float Amp;
			public float Cached;
			public bool Dirty;
		}

		// (그룹, 파트) 매핑 — enum 이름에서 자동 파싱하여 1회 초기화
		static readonly Dictionary<StatInfo, StatPart> _partMap = BuildPartMap();

		readonly Dictionary<StatInfo, Bucket> _buckets = new Dictionary<StatInfo, Bucket>();

		// 출처(source)별 modifier 인덱스 — 빈 source는 등록 안 함
		readonly Dictionary<string, List<StatModifier>> _bySource = new Dictionary<string, List<StatModifier>>();

		// === Base 채널 (영구 성장: Table 주입, 레벨업, 강화 등) ===

		public void SetBase(StatInfo type, float value)
		{
			StatPart part = _partMap[type];
			Bucket b = GetOrCreate(part.Group);
			ApplyDelta(b, part.Kind, value - GetField(b, part.Kind));
		}

		public void AddBase(StatInfo type, float delta)
		{
			StatPart part = _partMap[type];
			Bucket b = GetOrCreate(part.Group);
			ApplyDelta(b, part.Kind, delta);
		}

		// === Modifier 채널 (탈착 가능: 아이템, 버프) ===

		// AddModifier에 유효한 타입인지 사전 검사 (Base/Final은 불가)
		public bool CanAddModifier(StatInfo type)
		{
			StatPart part = _partMap[type];
			return part.Kind != ModifierTypes.Base && part.Kind != ModifierTypes.Final;
		}

		// type은 _Add / _Ratio / _Amp 중 하나만 허용 (_Base는 SetBase/AddBase 사용)
		public StatModifier AddModifier(StatInfo type, float value)
		{
			return AddModifier(type, value, string.Empty);
		}

		// source 태그 부착 버전 — RemoveAllFromSource 로 일괄 제거 가능
		public StatModifier AddModifier(StatInfo type, float value, string source)
		{
			StatPart part = _partMap[type];
			if (part.Kind == ModifierTypes.Base || part.Kind == ModifierTypes.Final)
			{
				throw new ArgumentException($"AddModifier는 _Add/_Ratio/_Amp만 허용. 입력: {type}");
			}

			Bucket b = GetOrCreate(part.Group);
			ApplyDelta(b, part.Kind, value);

			StatModifier mod = new StatModifier(part.Group, part.Kind, value, source);
			if (string.IsNullOrEmpty(mod.Source) == false)
			{
				List<StatModifier> list;
				if (_bySource.TryGetValue(mod.Source, out list) == false)
				{
					list = new List<StatModifier>(4);
					_bySource[mod.Source] = list;
				}

				list.Add(mod);
			}

			return mod;
		}

		public void RemoveModifier(StatModifier mod)
		{
			if (mod == null)
			{
				return;
			}

			Bucket b = GetOrCreate(mod.Group);
			ApplyDelta(b, mod.Kind, -mod.Value);

			if (string.IsNullOrEmpty(mod.Source) == false)
			{
				List<StatModifier> list;
				if (_bySource.TryGetValue(mod.Source, out list) == true)
				{
					list.Remove(mod);
					if (list.Count == 0)
					{
						_bySource.Remove(mod.Source);
					}
				}
			}
		}

		// 특정 source 로 등록된 modifier 전체 제거
		public void RemoveAllFromSource(string source)
		{
			if (string.IsNullOrEmpty(source) == true)
			{
				return;
			}

			List<StatModifier> list;
			if (_bySource.TryGetValue(source, out list) == false)
			{
				return;
			}

			for (int i = 0; i < list.Count; i++)
			{
				StatModifier mod = list[i];
				Bucket b = GetOrCreate(mod.Group);
				ApplyDelta(b, mod.Kind, -mod.Value);
			}

			_bySource.Remove(source);
		}

		// 디버그/UI 용 조회 — 빈 리스트 반환 (null 안전)
		static readonly List<StatModifier> _emptyMods = new List<StatModifier>(0);
		public IReadOnlyList<StatModifier> GetModifiersBySource(string source)
		{
			if (string.IsNullOrEmpty(source) == true)
			{
				return _emptyMods;
			}

			List<StatModifier> list;
			if (_bySource.TryGetValue(source, out list) == false)
			{
				return _emptyMods;
			}

			return list;
		}

		// === 조회 ===

		// type=ATK면 최종(공식 적용), type=ATK_Base/_Add/_Ratio/_Amp면 누적 구성값
		public float GetStat(StatInfo type)
		{
			StatPart part = _partMap[type];
			if (!_buckets.TryGetValue(part.Group, out Bucket b))
			{
				return 0f;
			}

			if (part.Kind == ModifierTypes.Final)
			{
				if (b.Dirty)
				{
					b.Cached = (b.Base + b.Add) * (1f + b.Ratio) * (1f + b.Amp);
					b.Dirty = false;
				}

				return b.Cached;
			}

			return GetField(b, part.Kind);
		}

		// === 내부 ===

		Bucket GetOrCreate(StatInfo group)
		{
			if (!_buckets.TryGetValue(group, out Bucket b))
			{
				b = new Bucket();
				_buckets[group] = b;
			}

			return b;
		}

		static float GetField(Bucket b, ModifierTypes kind)
		{
			switch (kind)
			{
				case ModifierTypes.Base:  return b.Base;
				case ModifierTypes.Add:   return b.Add;
				case ModifierTypes.Ratio: return b.Ratio;
				case ModifierTypes.Amp:   return b.Amp;
				default: return 0f;
			}
		}

		static void ApplyDelta(Bucket b, ModifierTypes kind, float delta)
		{
			switch (kind)
			{
				case ModifierTypes.Base:  b.Base  += delta; break;
				case ModifierTypes.Add:   b.Add   += delta; break;
				case ModifierTypes.Ratio: b.Ratio += delta; break;
				case ModifierTypes.Amp:   b.Amp   += delta; break;
				default: return;
			}

			b.Dirty = true;
		}

		// enum 이름의 suffix(_Base/_Add/_Ratio/_Amp)로 그룹과 파트를 자동 파싱
		// - "ATK_Base" → group=ATK, kind=Base
		// - "ATK"      → group=ATK, kind=Final
		// - "LifeSteal_Ratio" 등 단독 비율(그룹 enum이 따로 없음) → group=자기자신, kind=Final
		static Dictionary<StatInfo, StatPart> BuildPartMap()
		{
			var map = new Dictionary<StatInfo, StatPart>();
			foreach (StatInfo t in Enum.GetValues(typeof(StatInfo)))
			{
				if (t == StatInfo.None)
				{
					continue;
				}

				string name = t.ToString();
				ModifierTypes kind;
				string groupName;

				if (name.EndsWith("_Base"))
				{
					kind = ModifierTypes.Base;
					groupName = name.Substring(0, name.Length - 5);
				}
				else if (name.EndsWith("_Add"))
				{
					kind = ModifierTypes.Add;
					groupName = name.Substring(0, name.Length - 4);
				}
				else if (name.EndsWith("_Ratio"))
				{
					kind = ModifierTypes.Ratio;
					groupName = name.Substring(0, name.Length - 6);
				}
				else if (name.EndsWith("_Amp"))
				{
					kind = ModifierTypes.Amp;
					groupName = name.Substring(0, name.Length - 4);
				}
				else
				{
					map[t] = new StatPart(t, ModifierTypes.Final);
					continue;
				}

				if (Enum.TryParse<StatInfo>(groupName, out StatInfo group))
				{
					map[t] = new StatPart(group, kind);
				}
				else
				{
					// 그룹 enum이 없는 단독 스탯(LifeSteal_Ratio 등) → 자기 자신을 final로 취급
					map[t] = new StatPart(t, ModifierTypes.Final);
				}
			}

			return map;
		}
	}
}
