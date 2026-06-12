using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 대시 공격 — 시전자가 바라보는 방향(Facing)으로 벽은 막되 적은 관통하며 발동 스킬의 ScanParam1 거리만큼 직진.
	// 속도 = 거리 / 지속시간(등속). 경로상의 적은 한 번씩만 피격하며, 행의 효과 칼럼(StartEffect~FinishEffect)을 적용한다.
	// 효과 ID는 테이블에서 참조(하드코딩 없음). 클래스명 = SkillInfo enum 값명.
	public sealed class SKILL_DASH_ATTACK_01 : ISkillBehavior
	{
		private const float _fallbackDistance = 6f;        // 발동 스킬 Row 가 없을 때 폴백 거리
		private const float _fallbackHitWidth = 0.6f;      // ScanParam2 미설정 시 폴백 피격 반경
		private const float _attackDuration = 0.15f;       // 거리 주파 시간 (속도 = 거리 / 이 값)

		private UnitBase _caster;
		private SkillInfo _skillId;
		private Table_SkillInfo.Row _row;

		private bool _finished;
		private float _distance;
		private float _hitWidth;
		private Vector2 _startPos;

		// 중복 피격 방지 집합 + 검출/적용용 재사용 버퍼 (할당 방지)
		private readonly HashSet<UnitBase> _hitSet = new HashSet<UnitBase>();
		private readonly List<UnitBase> _queryBuffer = new List<UnitBase>(16);
		private readonly List<UnitBase> _hitList = new List<UnitBase>(8);

		public void SetContext(UnitBase caster, SkillInfo skillId)
		{
			_caster = caster;
			_skillId = skillId;
		}

		public void OnStart()
		{
			if (_caster == null || _caster.Mover == null)
			{
				_finished = true;
				return;
			}

			// 발동 스킬 데이터에서 거리(ScanParam1)/너비(ScanParam2) 확보 (없으면 폴백)
			_row = Table_SkillInfo.Get(_skillId);
			_distance = _fallbackDistance;
			_hitWidth = _fallbackHitWidth;
			if (_row != null)
			{
				_distance = _row.ScanParam1;
				if (_row.ScanParam2 > 0f)
				{
					_hitWidth = _row.ScanParam2;
				}
			}

			Vector2 dir = _caster.Mover.Facing;
			if (dir.sqrMagnitude < 1e-6f)
			{
				dir = Vector2.right;
			}

			// 속도 = 거리 / 지속시간 (지속시간 동안 등속 이동)
			float speed = _distance / _attackDuration;

			_startPos = _caster.transform.position;
			_caster.Mover.SetFacing(dir);
			_caster.Mover.SetOverride(dir * speed, pierceUnits: true);
			_caster.BlockMove(nameof(SKILL_DASH_ATTACK_01));   // 대시 중 이동 입력(플레이어/자동전투) 무시
			_caster.BlockSkill(nameof(SKILL_DASH_ATTACK_01));

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

			// 막힘 — 다음 이동 위치가 갈 수 없는 지역(벽)이면 정지 (유닛은 관통하므로 벽만)
			if (_caster.Mover.OverrideBlocked == true)
			{
				return true;
			}

			// 경로상 적 검출 — 미타격 적만 1회 피격
			DamagePathEnemies();

			// 목표 거리 도달 시 종료
			Vector2 currentPos = _caster.transform.position;
			if ((currentPos - _startPos).sqrMagnitude >= _distance * _distance)
			{
				return true;
			}

			return false;
		}

		private void DamagePathEnemies()
		{
			if (UnitContainer.Instance == null || _row == null)
			{
				return;
			}

			_hitList.Clear();
			UnitContainer.Instance.SpatialHash.Query(_caster.HitCenter, _queryBuffer);
			for (int i = 0; i < _queryBuffer.Count; i++)
			{
				UnitBase u = _queryBuffer[i];
				if (u == null || u.IsDead == true || u == _caster)
				{
					continue;
				}

				if (TargetResolver.IsEnemy(_caster.Faction, u.Faction) == false)
				{
					continue;
				}

				if (_hitSet.Contains(u) == true)
				{
					continue;
				}

				float reach = _hitWidth + u.Radius;
				if (((Vector2)u.HitCenter - _caster.HitCenter).sqrMagnitude > reach * reach)
				{
					continue;
				}

				_hitSet.Add(u);
				_hitList.Add(u);
			}

			// 경로 적에게 행 전체 효과(StartEffect~FinishEffect) 적용 — 효과 ID는 테이블에서 참조. skillID 귀속은 발동 스킬로.
			if (_hitList.Count > 0)
			{
				SkillExecutor.ApplyAllEffects(_row, _skillId, _caster, _hitList);
			}
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

			_caster.UnblockMove(nameof(SKILL_DASH_ATTACK_01));
			_caster.UnblockSkill(nameof(SKILL_DASH_ATTACK_01));
		}
	}
}
