using EDT;
using UnityEngine;

namespace ProjectOne.Skill
{
	// 스킬 한 개의 런타임 상태 — 쿨타임 진행 + 사이클 타이밍 계산 (설계 4.2)
	//
	// 판정은 로직 타이머가 소유하고 애니메이션은 따라오기만 한다.
	// 애니메이션 진행률로 타격을 판정하면 고속·프레임 드랍에서 판정이 유실된다.
	public sealed class SkillRuntime
	{
		public EDT.Skill Id { get; private set; }
		public float Cooldown { get; private set; }		// 테이블 정의 재사용 대기시간(공속 미적용)
		public float RemainingCooldown { get; private set; }
		public string Source { get; private set; }		// 출처 태그 (Base/Equipment/...)
		public SkillCategoryTypes SkillCategory { get; private set; }
		public SkillCastingTypes CastingType { get; private set; }
		public float CastingParam { get; private set; }	// 의미는 CastingType 이 결정 (설계 2.2)
		public float AnimLength { get; private set; }	// 클립 실제 길이(공속 1.0 기준)

		// 평타 여부 — 구 IsBasicAttack 컬럼이 사라져 SkillCategory 로 판정한다.
		public bool IsNormal
		{
			get { return SkillCategory == SkillCategoryTypes.Normal; }
		}

		public SkillRuntime(EDT.Skill id)
			: this(id, string.Empty)
		{
		}

		public SkillRuntime(EDT.Skill id, string source)
		{
			Id = id;
			Source = source ?? string.Empty;
			ReadFrom(Table_Skill.Get(id));
		}

		// 테이블 행 또는 리졸브 사본에서 사이클 계산에 필요한 값만 읽어 온다.
		//
		// 리졸브가 무효화돼도 런타임을 새로 만들지 않고 이 메서드로 값만 갱신한다 —
		// 재생성하면 진행 중인 쿨타임이 초기화되어 무기 교체가 쿨타임 리셋 수단이 된다.
		public void ReadFrom(Table_Skill.Row row)
		{
			if (row == null)
			{
				Debug.LogError($"[SkillRuntime] Table_Skill 행 없음 — EDT.Skill:{Id}");
				return;
			}

			Cooldown = row.Cooldown;
			SkillCategory = row.SkillCategory;
			CastingType = row.CastingType;
			CastingParam = row.CastingParam;
			AnimLength = row.AnimLength;
		}

		public bool IsOnCooldown
		{
			get { return RemainingCooldown > 0f; }
		}

		public bool CanCast()
		{
			return IsOnCooldown == false;
		}

		public void Tick(float dt)
		{
			if (RemainingCooldown > 0f)
			{
				RemainingCooldown -= dt;
				if (RemainingCooldown < 0f)
				{
					RemainingCooldown = 0f;
				}
			}
		}

		// ── 사이클 계산 (설계 4.2) ────────────────────────────────────

		// 공속 배율 — 평타만 적용받는다. 그 외 스킬의 쿨타임/모션은 공속과 무관하다.
		public float GetUseSpeed(float atkSpeed)
		{
			if (IsNormal == false)
			{
				return 1f;
			}

			// AtkSpeed_Base 누락 시 0 나눗셈으로 주기 계산이 붕괴한다. 데이터를 고치도록 알린다.
			if (atkSpeed <= 0f)
			{
				Debug.LogError($"[SkillRuntime] 평타 스킬인데 Stat_AtkSpeed 가 0 이하 — StatDetail_AtkSpeed_Base 를 확인하세요. EDT.Skill:{Id}");
				return 1f;
			}

			return atkSpeed;
		}

		// 스킬 재사용 주기
		public float GetCycleTime(float useSpeed)
		{
			return Cooldown / useSpeed;
		}

		// 모션 실제 재생 시간
		public float GetAnimPlayTime(float useSpeed)
		{
			float animSpeed = Mathf.Clamp(useSpeed, SkillConstants.ANIM_SPEED_MIN, SkillConstants.ANIM_SPEED_MAX);
			return AnimLength / animSpeed;
		}

		// 효과 발동 시점의 기준 시간.
		// 3분기를 지켜야 한다 — min 을 무조건 적용하면 Cooldown=0 인 패시브에서 모든 효과가 즉시 터진다.
		public float GetActionTime(float useSpeed)
		{
			if (AnimLength <= 0f)
			{
				return 0f;		// 패시브·오라·착탄·버프 틱 — 모션이 없으므로 즉시
			}

			float animPlayTime = GetAnimPlayTime(useSpeed);
			if (Cooldown <= 0f)
			{
				return animPlayTime;
			}

			float cycleTime = GetCycleTime(useSpeed);
			return animPlayTime < cycleTime ? animPlayTime : cycleTime;
		}

		// 애니메이터에 넘길 재생 배속
		public float GetAnimSpeed(float useSpeed)
		{
			return Mathf.Clamp(useSpeed, SkillConstants.ANIM_SPEED_MIN, SkillConstants.ANIM_SPEED_MAX);
		}

		// 시전 성공 시 호출 — 평타는 공속으로 나눈 주기가 쿨타임이 된다.
		public void StartCooldown(float useSpeed)
		{
			RemainingCooldown = GetCycleTime(useSpeed);
		}

		// 쿨타임 회복 (CooldownReduce 효과). 진행 중이 아니면 무시된다.
		public void ReduceCooldown(float amount)
		{
			if (RemainingCooldown <= 0f || amount <= 0f)
			{
				return;
			}

			RemainingCooldown -= amount;
			if (RemainingCooldown < 0f)
			{
				RemainingCooldown = 0f;
			}
		}
	}
}
