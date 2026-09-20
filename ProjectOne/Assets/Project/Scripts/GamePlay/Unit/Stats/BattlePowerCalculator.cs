using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Items;
using ProjectOne.Skill;

namespace ProjectOne.Unit.Stats
{
	// 전투력 계산 — 전투력 = Σ(최종스탯 × Weight) + Σ(활성 스킬옵션 Point)
	//
	// Stat 축 (Table_BattlePower_Stat)
	//   Weight 는 "기준 빌드에서의 한계 기여도"다. 실제 피해식은 곱연산(DamageCalculator)이지만
	//   전투력은 선형 합으로 근사한다 — 단조증가가 보장되고 기획 튜닝이 쉽다.
	//
	//   **버프·디버프 등 임시 스탯은 제외한다** — 전투력은 육성 결과만 보여야 하므로
	//   GetStat(최종값) 대신 GetStatFrom(영구 출처만) 을 읽는다. 임시 modifier 의 source 는
	//   SkillEffect enum 이름(SkillEffectApplier 가 row.ID.ToString() 으로 만든다)이라
	//   미리 열거할 수 없다 → 블랙리스트가 아니라 화이트리스트로 간다.
	//   화이트리스트는 HeroAspectRegistry 에 등록된 Aspect 의 SourceKey 다 — 새 영구 컨텐츠가
	//   Aspect 로 들어오면 자동 포함되고, 버프는 Aspect 가 아니므로 자동 제외된다.
	//
	//   Percent 스탯은 **Lv1 기본값 초과분만** 점수화한다. Lv1 캐릭터가 이미 AtkSpeed 1.5 ·
	//   CritDamage 1.5 를 갖고 있어서, 절대값으로 재면 전투력의 대부분이 "아무것도 안 한 기본값"에서
	//   나오고 성장 체감이 뭉개진다. Flat 스탯(Atk/MaxHp 등)은 절대값이 곱셈 주축이므로 차감하지 않는다.
	//
	// Option 축 (Table_BattlePower_Option)
	//   OptionTypes.SkillGrant 만 대상이다. Stat 타입 옵션(공격력 +N 등)은 이미 최종 스탯에 녹아
	//   있으므로 점수를 또 주면 이중 계산이 된다.
	//
	//   활성 판정은 SkillContainer 를 그대로 믿는다 — 장비 등급 해금 옵션은
	//   EquipmentOptionCalculator 가 "현재 등급 이하 누적"으로 걸러 EquipmentAspect 가 등록하므로,
	//   도달하지 못한 등급의 스킬은 애초에 컨테이너에 없다.
	//
	// StatCatalog 와 동일하게 테이블 로드 이후 Build() 가 호출되어야 한다.
	// 부트 순서: TableBootLoader.LoadAllAsync → StatCatalog.Build() → OptionCatalog.Build() → Build()
	public static class BattlePowerCalculator
	{
		// Percent 스탯의 Lv1 기본값. 여기 없는 스탯은 차감하지 않는다.
		private static readonly Dictionary<Stat, float> _baselines = new Dictionary<Stat, float>();

		// 영구 출처 화이트리스트 버퍼 — Calculate 마다 Registry 에서 다시 채운다(장비 Aspect 추가 등 반영).
		private static readonly List<string> _sources = new List<string>(8);

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		// 테이블 로드 직후 1회 호출. 재호출하면 인덱스를 다시 만든다(에디터 리로드 대비).
		public static void Build()
		{
			_baselines.Clear();

			Dictionary<int, Table_CharacterStat.Row> rows = Table_CharacterStat.All();
			Dictionary<int, Table_CharacterStat.Row>.Enumerator e = rows.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_CharacterStat.Row row = e.Current.Value;

				StatPart part;
				if (StatCatalog.TryGetPart(row.StatDetailID, out part) == false)
				{
					continue;
				}

				// Base 레이어만 기준선이 된다. Add/Ratio/Amp 는 캐릭터 기본값이 아니다.
				if (part.Kind != StatDetailTypes.Base)
				{
					continue;
				}

				Table_Stat.Row stat = Table_Stat.Get(part.Group);
				if (stat == null || stat.ValueType != StatValueTypes.Percent)
				{
					continue;
				}

				// PerLevel 성장분은 일부러 뺀다 — 기준선이 레벨과 같이 오르면 레벨업이 전투력에 안 잡힌다.
				_baselines[part.Group] = row.BaseValue;
			}

			_built = true;
			Debug.Log($"[BattlePowerCalculator] 구축 완료 — 기준선 {_baselines.Count}개");
		}

		// stats 는 필수, skills 는 없으면 Option 축을 건너뛴다(스킬 없는 유닛도 계산 가능).
		public static int Calculate(StatContainer stats, SkillContainer skills)
		{
			if (_built == false)
			{
				Debug.LogError("[BattlePowerCalculator] Build() 이전에 계산했습니다. 부트 순서를 확인하세요.");
				return 0;
			}

			if (stats == null)
			{
				return 0;
			}

			HeroAspectRegistry.Instance.CollectSourceKeys(_sources);
			if (_sources.Count == 0)
			{
				// 조용히 넘어가면 Base 만 남아 전투력이 통째로 무너진다 — 여기서 바로 드러낸다.
				Debug.LogError("[BattlePowerCalculator] 등록된 Aspect 가 없습니다 — 영구 출처를 알 수 없어 계산할 수 없습니다.");
				return 0;
			}

			float total = sumStats(stats);
			if (skills != null)
			{
				total += sumOptions(skills);
			}

			if (total < 0f)
			{
				total = 0f;
			}

			return Mathf.RoundToInt(total);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static float sumStats(StatContainer stats)
		{
			float total = 0f;

			Dictionary<int, Table_BattlePower_Stat.Row> rows = Table_BattlePower_Stat.All();
			Dictionary<int, Table_BattlePower_Stat.Row>.Enumerator e = rows.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_BattlePower_Stat.Row row = e.Current.Value;
				if (row.SourceStat == Stat.None || row.Weight == 0f)
				{
					continue;
				}

				// 영구 출처만 반영한 값. 클램프를 거치므로 상한 초과분은 자동으로 점수에 잡히지 않는다.
				float value = stats.GetStatFrom(row.SourceStat, _sources);

				float baseline;
				if (_baselines.TryGetValue(row.SourceStat, out baseline) == true)
				{
					value -= baseline;
					if (value < 0f)
					{
						value = 0f;
					}
				}

				total += value * row.Weight;
			}

			return total;
		}

		private static float sumOptions(SkillContainer skills)
		{
			float total = 0f;

			Dictionary<int, Table_BattlePower_Option.Row> rows = Table_BattlePower_Option.All();
			Dictionary<int, Table_BattlePower_Option.Row>.Enumerator e = rows.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_BattlePower_Option.Row row = e.Current.Value;
				if (row.SourceOption == Option.None || row.Point == 0f)
				{
					continue;
				}

				OptionCatalog.Entry entry;
				if (OptionCatalog.TryGet(row.SourceOption, out entry) == false)
				{
					Debug.LogWarning($"[BattlePowerCalculator] 정의되지 않은 옵션: {row.SourceOption}");
					continue;
				}

				// Stat 타입은 최종 스탯에 이미 반영되어 있다 — 여기서 점수를 주면 이중 계산이다.
				if (entry.type != OptionTypes.SkillGrant)
				{
					Debug.LogWarning($"[BattlePowerCalculator] SkillGrant 가 아닌 옵션은 전투력에 넣을 수 없습니다: {row.SourceOption} (type={entry.type})");
					continue;
				}

				if (isSkillRegistered(skills, entry.skill) == false)
				{
					continue;
				}

				total += row.Point;
			}

			return total;
		}

		private static bool isSkillRegistered(SkillContainer skills, EDT.Skill skill)
		{
			if (skill == EDT.Skill.None)
			{
				return false;
			}

			IReadOnlyList<EDT.Skill> registered = skills.GetAll();
			for (int i = 0; i < registered.Count; i++)
			{
				if (registered[i] == skill)
				{
					return true;
				}
			}

			return false;
		}
	}
}
