using System.Collections.Generic;
using EDT;

namespace ProjectOne.Skill
{
	// 리졸브가 끝난 스킬 1개 — 테이블 원본이 아니라 **사본**이다 (설계 11.2).
	//
	// 원본을 수정하면 같은 스킬을 참조하는 소환물까지 주인의 옵션에 오염된다.
	// 그래서 리졸브는 언제나 사본 위에서만 일어난다.
	//
	// 사본의 타입을 자동생성 Row 그대로 쓰는 이유 — Row 의 모든 프로퍼티가 setter 를 가지므로
	// 별도 미러 클래스가 필요 없다. 덕분에 SkillEffectParams / SkillEffectApplier 의
	// `Table_SkillEffect.Row` 시그니처를 하나도 바꾸지 않는다.
	//
	// 테이블은 효과 슬롯이 2칸(EffectID_01/_02)이지만 런타임은 가변 리스트다 — Append 모디파이어 때문이다.
	public sealed class ResolvedSkill
	{
		// Replace 로 교체된 뒤의 최종 스킬 ID. 교체가 없으면 요청한 ID 와 같다.
		public EDT.Skill Id;

		// Skill 행 사본. 패스스루(모디파이어 없음)일 때는 테이블 원본을 그대로 가리킨다.
		public Table_Skill.Row Row;

		// 효과 행 사본 목록. 패스스루일 때도 리스트 자체는 만든다(순회 경로를 하나로 유지).
		public readonly List<Table_SkillEffect.Row> Effects = new List<Table_SkillEffect.Row>(2);

		// 사본을 만들지 않고 원본을 그대로 물고 있는가 — 쓰기 전에 반드시 확인해야 한다.
		public bool IsPassthrough;

		public bool IsValid
		{
			get { return Row != null; }
		}

		public void Clear()
		{
			Id = EDT.Skill.None;
			Row = null;
			Effects.Clear();
			IsPassthrough = false;
		}

		// 효과 ID 로 이 스킬의 효과 사본을 찾는다. 없으면 null.
		public Table_SkillEffect.Row FindEffect(EDT.SkillEffect effectId)
		{
			for (int i = 0; i < Effects.Count; i++)
			{
				if (Effects[i] != null && Effects[i].ID == effectId)
				{
					return Effects[i];
				}
			}

			return null;
		}

		// ── 사본 생성 ─────────────────────────────────────────────────

		public static Table_Skill.Row CopySkill(Table_Skill.Row src)
		{
			Table_Skill.Row dst = new Table_Skill.Row();
			dst.ID = src.ID;
			dst.Name = src.Name;
			dst.Desc = src.Desc;
			dst.Icon = src.Icon;
			dst.SkillCategory = src.SkillCategory;
			dst.AnimName = src.AnimName;
			dst.AnimLength = src.AnimLength;
			dst.CastingType = src.CastingType;
			dst.CastingParam = src.CastingParam;
			dst.ApplyTarget = src.ApplyTarget;
			dst.ScanType = src.ScanType;
			dst.ScanRange = src.ScanRange;
			dst.ScanParam = src.ScanParam;
			dst.Cooldown = src.Cooldown;
			dst.EffectID_01 = src.EffectID_01;
			dst.EffectID_02 = src.EffectID_02;
			dst.SkillVFX = src.SkillVFX;
			dst.SkillSFX = src.SkillSFX;
			dst.SkillVFXFacing = src.SkillVFXFacing;
			dst.SkillVFXOffset = src.SkillVFXOffset;
			return dst;
		}

		public static Table_SkillEffect.Row CopyEffect(Table_SkillEffect.Row src)
		{
			Table_SkillEffect.Row dst = new Table_SkillEffect.Row();
			dst.ID = src.ID;
			dst.EffectType = src.EffectType;
			dst.EffectOrigin = src.EffectOrigin;
			dst.EffectTime = src.EffectTime;
			dst.EffectParam_1 = src.EffectParam_1;
			dst.EffectParam_2 = src.EffectParam_2;
			dst.EffectParam_3 = src.EffectParam_3;
			dst.EffectParam_4 = src.EffectParam_4;
			dst.EffectParam_5 = src.EffectParam_5;
			dst.ChainEffectIDs = src.ChainEffectIDs;	// 읽기 전용으로만 쓰이므로 배열은 공유한다
			dst.OnHitTrigger = src.OnHitTrigger;
			dst.EffectVFX = src.EffectVFX;
			dst.EffectSFX = src.EffectSFX;
			return dst;
		}

		// 테이블 원본을 그대로 물고 있는 패스스루 뷰. 모디파이어 출처가 없는 유닛(몬스터)이 쓴다.
		//
		// 테이블은 불변이므로 스킬 ID 당 하나만 만들어 전역으로 공유한다 —
		// 이게 없으면 몬스터가 평타를 칠 때마다 할당이 생긴다.
		private static readonly Dictionary<EDT.Skill, ResolvedSkill> _passthroughCache =
			new Dictionary<EDT.Skill, ResolvedSkill>();

		public static ResolvedSkill CreatePassthrough(EDT.Skill id)
		{
			ResolvedSkill cached;
			if (_passthroughCache.TryGetValue(id, out cached) == true)
			{
				return cached;
			}

			Table_Skill.Row row = Table_Skill.Get(id);
			if (row == null)
			{
				return null;
			}

			ResolvedSkill resolved = new ResolvedSkill();
			resolved.Id = id;
			resolved.Row = row;
			resolved.IsPassthrough = true;

			addEffect(resolved, row.EffectID_01);
			addEffect(resolved, row.EffectID_02);

			_passthroughCache[id] = resolved;
			return resolved;
		}

		// 도메인 리로드가 꺼진 환경에서 테이블이 다시 로드되면 캐시가 옛 행을 물고 있다.
		// TableBootLoader 재로드 경로가 생기면 여기를 함께 비운다.
		public static void ClearPassthroughCache()
		{
			_passthroughCache.Clear();
		}

		private static void addEffect(ResolvedSkill resolved, EDT.SkillEffect effectId)
		{
			if (effectId == EDT.SkillEffect.None)
			{
				return;
			}

			Table_SkillEffect.Row row = Table_SkillEffect.Get(effectId);
			if (row != null)
			{
				resolved.Effects.Add(row);
			}
		}
	}
}
