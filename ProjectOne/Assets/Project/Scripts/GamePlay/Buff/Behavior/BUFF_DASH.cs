using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Skill;

namespace ProjectOne.Buff
{
	// 대시 — 발동 스킬의 ScanParam1 을 탐지 반경으로 가장 가까운 적을 찾아 밀착 위치까지 이동.
	// 이동 거리 = 적까지 거리 − 적 반경. 속도 = 이동 거리 / 버프 지속시간(등속). 거리 도달/막힘 시 종료. 이동만 수행(피격 없음).
	public sealed class BUFF_DASH : ITickableBuff
	{
		private const float _fallbackDetectRange = 8f;     // 발동 스킬 Row 가 없을 때 폴백 탐지 반경
		private const float _fallbackDuration = 0.3f;      // 지속시간이 무한/0 일 때 폴백

		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		private BuffRuntime _host;
		private bool _finished;
		private float _targetDistance;
		private Vector2 _startPos;

		public BUFF_DASH(UnitBase owner, UnitBase caster)
		{
			_owner = owner;
			_caster = caster;
		}

		public void SetHost(BuffRuntime host)
		{
			_host = host;
		}

		public void OnActivate()
		{
			if (_owner == null || _owner.Mover == null)
			{
				_finished = true;
				return;
			}

			// 발동 스킬 데이터에서 탐지 반경 확보 (없으면 폴백)
			float detectRange = _fallbackDetectRange;
			if (_host != null)
			{
				Table_SkillInfo.Row skillRow = Table_SkillInfo.Get(_host.SourceSkill);
				if (skillRow != null)
				{
					detectRange = skillRow.ScanParam1;
				}
			}

			// 탐지 반경 내 가장 가까운 적 1명
			List<UnitBase> scanned = TargetResolver.ScanByType(SkillScanType.Target, detectRange, 0f, _owner);
			if (scanned.Count == 0)
			{
				_finished = true;
				return;
			}

			UnitBase target = scanned[0];
			Vector2 toTarget = (Vector2)target.HitCenter - _owner.HitCenter;
			float dist = toTarget.magnitude;
			if (dist <= Mathf.Epsilon)
			{
				_finished = true;
				return;
			}

			Vector2 dir = toTarget / dist;
			// 두 유닛 표면이 닿도록 적 반경 + 시전자 반경만큼 당겨 목표 거리 산출 (적 바로 앞 밀착)
			_targetDistance = Mathf.Max(0f, dist - target.Radius - _owner.Radius);

			// 속도 = 목표 거리 / 지속시간 (지속시간 동안 등속 이동)
			float duration = ResolveDuration();
			float speed = _targetDistance / duration;

			_startPos = _owner.transform.position;

			_owner.Mover.SetFacing(dir);
			_owner.Mover.SetOverride(dir * speed);
			_owner.BlockMove(nameof(BUFF_DASH));   // 대시 중 이동 입력(플레이어/자동전투) 무시
			_owner.BlockSkill(nameof(BUFF_DASH));

			// 대시 직전 진행 중이던 스킬(평타 등)의 예약 효과 취소 — 대시 중 공격 발동 방지
			if (_owner.SkillContainer != null)
			{
				_owner.SkillContainer.CancelPendingEffects();
			}
		}

		public bool Tick(float dt)
		{
			if (_finished == true || _owner == null || _owner.Mover == null)
			{
				return true;
			}

			// 막힘 — 다음 이동 위치를 다른 유닛이 막거나 갈 수 없는 지역(벽)이면 정지 (UnitMover 충돌 판정 결과)
			if (_owner.Mover.OverrideBlocked == true)
			{
				return true;
			}

			// 목표 거리(적 바로 앞) 도달
			if (((Vector2)_owner.transform.position - _startPos).sqrMagnitude >= _targetDistance * _targetDistance)
			{
				return true;
			}

			return false;
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			if (_owner.Mover != null)
			{
				_owner.Mover.ClearOverride();
			}

			_owner.UnblockMove(nameof(BUFF_DASH));
			_owner.UnblockSkill(nameof(BUFF_DASH));
		}

		// 호스트의 남은 지속시간(생성 직후 = 초기 Duration) — 무한/0 이면 폴백
		private float ResolveDuration()
		{
			if (_host == null || _host.IsInfinite == true || _host.RemainingDuration <= 0f)
			{
				return _fallbackDuration;
			}

			return _host.RemainingDuration;
		}
	}
}
