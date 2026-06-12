using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 대시 — 발동 스킬의 ScanParam1 을 탐지 반경으로 가장 가까운 적을 찾아 밀착 위치까지 이동.
	// 이동 거리 = 적까지 거리 − 적 반경 − 시전자 반경. 속도 = 이동 거리 / 지속시간(등속). 거리 도달/막힘 시 종료. 이동만 수행(피격 없음).
	// (구 BUFF_DASH → SKILL_DASH_01 을 다시 ISkillAction 빌딩블록으로 분해 — SkillSequence 가 조합)
	public sealed class DashAction : ISkillAction
	{
		private const float _fallbackDetectRange = 8f;     // 발동 스킬 Row 가 없을 때 폴백 탐지 반경
		// 지속시간 소스: 구 BUFF_DASH 는 ActivateBuff 효과의 Duration 으로 속도를 정했다.
		// 스킬 직결로 옮기며 그 값이 사라져 폴백 상수로 시작 — 추후 스킬 테이블 칼럼화는 후속 과제.
		private const float _dashDuration = 0.15f;

		private UnitBase _caster;

		private bool _finished;
		private float _targetDistance;
		private Vector2 _startPos;

		public void OnStart(UnitBase caster, SkillInfo skillId)
		{
			_caster = caster;
			if (_caster == null || _caster.Mover == null)
			{
				_finished = true;
				return;
			}

			// 발동 스킬 데이터에서 탐지 반경 확보 (없으면 폴백)
			float detectRange = _fallbackDetectRange;
			Table_SkillInfo.Row skillRow = Table_SkillInfo.Get(skillId);
			if (skillRow != null)
			{
				detectRange = skillRow.ScanParam1;
			}

			// 탐지 반경 내 가장 가까운 적 1명
			List<UnitBase> scanned = TargetResolver.ScanByType(SkillScanType.Target, detectRange, 0f, _caster);
			if (scanned.Count == 0)
			{
				_finished = true;
				return;
			}

			UnitBase target = scanned[0];
			Vector2 toTarget = (Vector2)target.HitCenter - _caster.HitCenter;
			float dist = toTarget.magnitude;
			if (dist <= Mathf.Epsilon)
			{
				_finished = true;
				return;
			}

			Vector2 dir = toTarget / dist;
			// 두 유닛 표면이 닿도록 적 반경 + 시전자 반경만큼 당겨 목표 거리 산출 (적 바로 앞 밀착)
			_targetDistance = Mathf.Max(0f, dist - target.Radius - _caster.Radius);

			// 속도 = 목표 거리 / 지속시간 (지속시간 동안 등속 이동)
			float speed = _targetDistance / _dashDuration;

			_startPos = _caster.transform.position;

			_caster.Mover.SetFacing(dir);
			_caster.Mover.SetOverride(dir * speed);
			_caster.BlockMove(nameof(DashAction));   // 대시 중 이동 입력(플레이어/자동전투) 무시
			_caster.BlockSkill(nameof(DashAction));

			// 대시 직전 진행 중이던 스킬(평타 등)의 예약 효과 취소 — 대시 중 공격 발동 방지
			if (_caster.SkillContainer != null)
			{
				_caster.SkillContainer.CancelPendingEffects();
			}
		}

		public bool Tick(float dt)
		{
			if (_finished == true || _caster == null || _caster.Mover == null)
			{
				return true;
			}

			// 막힘 — 다음 이동 위치를 다른 유닛이 막거나 갈 수 없는 지역(벽)이면 정지 (UnitMover 충돌 판정 결과)
			if (_caster.Mover.OverrideBlocked == true)
			{
				return true;
			}

			// 목표 거리(적 바로 앞) 도달
			if (((Vector2)_caster.transform.position - _startPos).sqrMagnitude >= _targetDistance * _targetDistance)
			{
				return true;
			}

			return false;
		}

		public void OnEnd()
		{
			if (_caster == null)
			{
				return;
			}

			if (_caster.Mover != null)
			{
				_caster.Mover.ClearOverride();
			}

			_caster.UnblockMove(nameof(DashAction));
			_caster.UnblockSkill(nameof(DashAction));
		}
	}
}
