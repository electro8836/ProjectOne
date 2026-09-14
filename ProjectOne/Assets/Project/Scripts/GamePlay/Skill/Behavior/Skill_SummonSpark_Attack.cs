using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Summons;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 궤도 소환물(스파크)의 전기 빔 — 사거리 안의 적 하나를 골라 소환물에서 대상까지 선을 잇고 즉시 때린다.
	//
	// 코드 스킬로 두는 이유는 연출뿐이다. "소환물 위치에서 대상까지 이어지는 선"은 테이블로 표현할 방법이 없다
	// (SkillVFX 는 시전자 기준 1회성이라 길이를 대상까지 늘리지 못한다). 판정·데미지는 전부 테이블을 따른다.
	//
	// 대상 선정은 행의 ScanType/ScanRange 를 쓰되, 대상 수는 ScanParam 이 아니라 형제 구체 수를 쓴다.
	// 구체가 여럿이면 주인에게 가까운 순으로 그만큼 후보를 두고 번호대로 나눠 맡는다(구체 3·적 2면 1순위에 둘).
	public sealed class Skill_SummonSpark_Attack : ISkillBehavior
	{
		// 빔이 보이는 시간. 판정은 OnStart 에서 이미 끝나 있고 이건 연출 길이일 뿐이다.
		private const float BeamDuration = 0.12f;

		private UnitBase _caster;
		private EDT.Skill _skillId;
		private Table_Skill.Row _row;
		private SummonBeamView _beam;

		private float _elapsed;
		private bool _finished;

		// 효과 적용 대상 — 한 명뿐이지만 Apply 가 리스트를 받는다
		private readonly List<UnitBase> _targets = new List<UnitBase>(1);

		// 주인 기준으로 훑은 후보 목록(가까운 순). 이 중 내 번호에 해당하는 하나만 _targets 로 간다.
		private readonly List<UnitBase> _candidates = new List<UnitBase>(4);

		public void SetContext(UnitBase caster, EDT.Skill skillId)
		{
			_caster = caster;
			_skillId = skillId;
		}

		public void OnStart()
		{
			_row = Table_Skill.Get(_skillId);
			if (_caster == null || _row == null)
			{
				_finished = true;
				return;
			}

			// 형제 구체 중 내 번호와 총수 — 구체들이 같은 적만 때리지 않도록 나눠 맡는 데 쓴다.
			// 번호를 못 구하면(소환물이 아니거나 목록에 없으면) 혼자인 것처럼 동작한다.
			int index = 0;
			int total = 1;
			SummonUnit self = _caster as SummonUnit;
			if (self != null && SummonManager.HasInstance == true)
			{
				int found = SummonManager.Instance.GetIndexOf(self, out total);
				if (found < 0 || total <= 0)
				{
					index = 0;
					total = 1;
				}
				else
				{
					index = found;
				}
			}

			// 탐색 기준점은 SummonUnit.ScanOrigin 이 주인으로 돌려준다 — 구체마다 제 위치로 훑으면
			// 후보 목록이 서로 달라져 번호로 나눠 맡는 것이 깨진다. 모든 구체가 같은 순위표를 봐야 한다.
			// 대상 수만 행의 ScanParam 이 아니라 구체 수를 쓴다 — 구체 수만큼 후보를 두고 그 안에서 나눈다.
			//
			// ScanByType 은 static 공유 버퍼를 돌려준다 — 효과 적용 중 다른 탐색이 끼어들 수 있으니 로컬로 옮긴다.
			List<UnitBase> scanned = TargetResolver.ScanByType(_row.ScanType, SkillApplyTarget.Enemy, _row.ScanRange, total, _caster);
			_candidates.Clear();
			for (int i = 0; i < scanned.Count; i++)
			{
				_candidates.Add(scanned[i]);
			}

			if (_candidates.Count == 0)
			{
				_finished = true;
				return;
			}

			// 후보는 주인에게 가까운 순으로 정렬돼 있다(TargetResolver 의 Target 탐색이 보장).
			// 구체 3개 · 적 2명이면 0→1순위, 1→2순위, 2→1순위가 되어 1순위에 둘이 몰린다.
			_targets.Clear();
			_targets.Add(_candidates[index % _candidates.Count]);

			showBeam(_targets[0]);

			// 효과 ID 는 테이블에서 참조한다. 코드 스킬은 자체 타이밍으로 도니 EffectTime 지연 없이 즉시 적용한다.
			SkillEffectApplier.Apply(_row.EffectID_01, _caster, _skillId, _targets, 0);
			SkillEffectApplier.Apply(_row.EffectID_02, _caster, _skillId, _targets, 0);
		}

		public bool Tick(float dt)
		{
			if (_finished == true)
			{
				return true;
			}

			// 두 끝점을 매 프레임 다시 맞춘다 — 소환물은 공전 중이고 대상도 움직이므로
			// OnStart 에서 찍은 좌표 그대로 두면 연출 동안 빔이 제자리에 남아 어긋난다.
			//
			// 여기서 예외가 나면 SkillContainer.Tick 이 중단되어 그 아래 쿨다운 감소 루프까지 멈춘다.
			// 유닛의 모든 스킬이 영구 쿨다운에 걸리므로 인덱스 접근 전에 반드시 개수를 확인한다.
			if (_targets.Count > 0)
			{
				UnitBase target = _targets[0];
				if (target != null && target.IsDead == false)
				{
					showBeam(target);
				}
			}

			_elapsed += dt;
			return _elapsed >= BeamDuration;
		}

		public void OnEnd()
		{
			if (_beam != null)
			{
				_beam.Hide();
			}
		}

		private void showBeam(UnitBase target)
		{
			if (_beam == null)
			{
				// 빔은 소환물 프리팹의 자식이다 — 비활성 상태일 수 있어 includeInactive 로 찾는다.
				_beam = _caster.GetComponentInChildren<SummonBeamView>(true);
			}

			if (_beam == null)
			{
				Debug.LogError($"[Skill_SummonSpark_Attack] 소환물에 SummonBeamView 가 없다 — EDT.Skill:{_skillId}");
				return;
			}

			_beam.Show(_caster.HitCenter, target.HitCenter);
		}
	}
}
