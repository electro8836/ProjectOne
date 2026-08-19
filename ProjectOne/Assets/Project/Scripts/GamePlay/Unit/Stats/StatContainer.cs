using System;
using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Unit.Stats
{
	// 유닛 스탯 컨테이너 (POCO)
	// 공식: Final = (Base + ΣAdd) * (1 + ΣRatio) * (1 + ΣAmp) → 클램프 1회
	// - 값을 넣는 입구는 언제나 StatDetail(레이어), 읽는 출구는 Stat(최종)
	// - 변경된 스탯만 dirty 표시 → GetStat 시 lazy 재계산
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

		readonly Dictionary<Stat, Bucket> _buckets = new Dictionary<Stat, Bucket>();

		// 출처(source)별 modifier 인덱스 — 빈 source는 등록 안 함
		readonly Dictionary<string, List<StatModifier>> _bySource = new Dictionary<string, List<StatModifier>>();

		int _version;

		// 값이 한 번이라도 바뀌면 증가한다. 소환물이 주인 스탯 변경을 감지하는 용도다.
		// 이벤트를 쓰지 않는 이유 — 장비 교체 한 번에 모디파이어가 수십 개 붙는데,
		// 이벤트면 그때마다 소환물 전체가 재계산된다. 버전 비교는 틱당 int 비교 한 번이다.
		public int Version
		{
			get { return _version; }
		}

		// === Base 채널 (영구 성장: 캐릭터/몬스터 기본 스탯, 레벨 성장) ===

		public void SetBase(StatDetail detail, float value)
		{
			StatPart part;
			if (tryResolveBase(detail, out part) == false)
			{
				return;
			}

			Bucket b = GetOrCreate(part.Group);
			ApplyDelta(b, part.Kind, value - GetField(b, part.Kind));
		}

		public void AddBase(StatDetail detail, float delta)
		{
			StatPart part;
			if (tryResolveBase(detail, out part) == false)
			{
				return;
			}

			Bucket b = GetOrCreate(part.Group);
			ApplyDelta(b, part.Kind, delta);
		}

		// === Modifier 채널 (탈착 가능: 장비, 스킬 트리, 버프) ===

		// AddModifier에 유효한 StatDetail 인지 사전 검사 (Base 레이어는 불가)
		public bool CanAddModifier(StatDetail detail)
		{
			StatPart part;
			if (StatCatalog.TryGetPart(detail, out part) == false)
			{
				return false;
			}

			return part.Kind != StatDetailTypes.Base;
		}

		// detail 은 Add / Ratio / Amp 레이어만 허용 (Base 는 SetBase/AddBase 사용)
		public StatModifier AddModifier(StatDetail detail, float value)
		{
			return AddModifier(detail, value, string.Empty);
		}

		// source 태그 부착 버전 — RemoveAllFromSource 로 일괄 제거 가능
		public StatModifier AddModifier(StatDetail detail, float value, string source)
		{
			StatPart part;
			if (StatCatalog.TryGetPart(detail, out part) == false)
			{
				Debug.LogError($"[StatContainer] StatDetail 정의를 찾을 수 없음: {detail}");
				return null;
			}

			if (part.Kind == StatDetailTypes.Base)
			{
				// Base 에 장비·노드·버프가 값을 넣으면 장비를 벗었을 때 기본 능력치가 사라진다.
				throw new ArgumentException($"AddModifier는 Add/Ratio/Amp 레이어만 허용. 입력: {detail}");
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

		// 최종 스탯 — 공식 적용 후 Min/Max 클램프를 딱 한 번 적용한다.
		public float GetStat(Stat stat)
		{
			Bucket b;
			if (_buckets.TryGetValue(stat, out b) == false)
			{
				return StatCatalog.ApplyClamp(stat, 0f);
			}

			if (b.Dirty == true)
			{
				float raw = (b.Base + b.Add) * (1f + b.Ratio) * (1f + b.Amp);
				b.Cached = StatCatalog.ApplyClamp(stat, raw);
				b.Dirty = false;
			}

			return b.Cached;
		}

		// 레이어 누적값 조회 (스탯 창의 구성 표시·디버그용). 최종값이 아니다.
		public float GetLayer(StatDetail detail)
		{
			StatPart part;
			if (StatCatalog.TryGetPart(detail, out part) == false)
			{
				return 0f;
			}

			Bucket b;
			if (_buckets.TryGetValue(part.Group, out b) == false)
			{
				return 0f;
			}

			return GetField(b, part.Kind);
		}

		// === 내부 ===

		// Base 레이어 전용 해석 — 캐릭터/몬스터 기본 스탯만 이 경로를 쓴다.
		static bool tryResolveBase(StatDetail detail, out StatPart part)
		{
			if (StatCatalog.TryGetPart(detail, out part) == false)
			{
				Debug.LogError($"[StatContainer] StatDetail 정의를 찾을 수 없음: {detail}");
				return false;
			}

			if (part.Kind != StatDetailTypes.Base)
			{
				Debug.LogError($"[StatContainer] SetBase/AddBase 는 Base 레이어만 허용. 입력: {detail}");
				return false;
			}

			return true;
		}

		Bucket GetOrCreate(Stat group)
		{
			Bucket b;
			if (_buckets.TryGetValue(group, out b) == false)
			{
				b = new Bucket();
				_buckets[group] = b;
			}

			return b;
		}

		static float GetField(Bucket b, StatDetailTypes kind)
		{
			switch (kind)
			{
				case StatDetailTypes.Base:  return b.Base;
				case StatDetailTypes.Add:   return b.Add;
				case StatDetailTypes.Ratio: return b.Ratio;
				case StatDetailTypes.Amp:   return b.Amp;
				default: return 0f;
			}
		}

		void ApplyDelta(Bucket b, StatDetailTypes kind, float delta)
		{
			switch (kind)
			{
				case StatDetailTypes.Base:  b.Base  += delta; break;
				case StatDetailTypes.Add:   b.Add   += delta; break;
				case StatDetailTypes.Ratio: b.Ratio += delta; break;
				case StatDetailTypes.Amp:   b.Amp   += delta; break;
				default: return;
			}

			b.Dirty = true;
			_version++;
		}
	}
}
