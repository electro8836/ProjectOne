using EDT;

namespace ProjectOne.Skill
{
	// 스킬 한 개의 런타임 상태 — 쿨타임 진행 / (TODO) 캐스팅 진행
	public sealed class SkillRuntime
	{
		public SkillInfo Id { get; private set; }
		public float Cooltime { get; private set; }      // 테이블 정의 쿨타임
		public float RemainingCooltime { get; private set; }
		public string Source { get; private set; }       // 출처 태그 (Base/Equipment/...)
		public SkillCastingTypes CastingType { get; private set; }  // 시전 방식 (TryCast 거부/OnHit 판정용)
		public int CastingParam { get; private set; }              // OnHit=확률%, Casting=지연 초
		public bool IsBasicAttack { get; private set; }

		public SkillRuntime(SkillInfo id)
			: this(id, string.Empty)
		{
		}

		public SkillRuntime(SkillInfo id, string source)
		{
			Id = id;
			Source = source ?? string.Empty;
			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row != null)
			{
				Cooltime = row.CooltimeSec;
				CastingType = row.CastingType;
				CastingParam = row.CastingParam;
				IsBasicAttack = row.IsBasicAttack;
			}
		}

		public bool IsOnCooldown
		{
			get { return RemainingCooltime > 0f; }
		}

		public bool CanCast()
		{
			return IsOnCooldown == false;
		}

		public void StartCooldown()
		{
			RemainingCooltime = Cooltime;
		}

		public void StartCooldown(float overrideCooltime)
		{
			RemainingCooltime = overrideCooltime;
		}

		public void Tick(float dt)
		{
			if (RemainingCooltime > 0f)
			{
				RemainingCooltime -= dt;
				if (RemainingCooltime < 0f)
				{
					RemainingCooltime = 0f;
				}
			}
		}
	}
}
