using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 대시 공격 — 시전자가 바라보는 방향(Facing)으로 벽은 막되 적은 관통하며 발동 스킬의 ScanParam1 거리만큼 직진.
	// 속도 = 거리 / 지속시간(등속). 경로상의 적은 한 번씩만 피격(_pathDamageEffect 효과 적용).
	// (구 BUFF_DASH_ATTACK → SKILL_DASH_ATTACK_01 을 다시 ISkillAction 빌딩블록으로 분해 — SkillSequence 가 조합)
	public sealed class DashAttackAction : ISkillAction
	{
		private const float _fallbackDistance = 6f;        // 발동 스킬 Row 가 없을 때 폴백 거리
		private const float _fallbackHitWidth = 0.6f;      // ScanParam2 미설정 시 폴백 피격 반경
		private const float _attackDuration = 0.15f;
		// 경로 데미지 효과: 구 BUFF_DASH_ATTACK 은 버프 테이블 Effect 칼럼(SE_DASH_ATTACK_DAMAGE_01)을 참조했다.
		// 스킬 직결로 옮기며 그 소스가 사라져 코드 상수로 고정 — 추후 스킬 테이블 칼럼화는 후속 과제.
		private const SkillEffect _pathDamageEffect = SkillEffect.SE_DASH_ATTACK_DAMAGE_01;

		private UnitBase _caster;
		private SkillInfo _skillId;

		private bool _finished;
		private float _distance;
		private float _hitWidth;
		private Vector2 _startPos;

		// 중복 피격 방지 집합 + 검출/적용용 재사용 버퍼 (할당 방지)
		private readonly HashSet<UnitBase> _hitSet = new HashSet<UnitBase>();
		private readonly List<UnitBase> _queryBuffer = new List<UnitBase>(16);
		private readonly List<UnitBase> _hitList = new List<UnitBase>(8);

		public void OnStart(UnitBase caster, SkillInfo skillId)
		{
			_caster = caster;
			_skillId = skillId;
			if (_caster == null || _caster.Mover == null)
			{
				_finished = true;
				return;
			}

			// 발동 스킬 데이터에서 거리(ScanParam1)/너비(ScanParam2) 확보 (없으면 폴백)
			_distance = _fallbackDistance;
			_hitWidth = _fallbackHitWidth;
			Table_SkillInfo.Row skillRow = Table_SkillInfo.Get(skillId);
			if (skillRow != null)
			{
				_distance = skillRow.ScanParam1;
				if (skillRow.ScanParam2 > 0f)
				{
					_hitWidth = skillRow.ScanParam2;
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
			_caster.BlockMove(nameof(DashAttackAction));   // 대시 중 이동 입력(플레이어/자동전투) 무시
			_caster.BlockSkill(nameof(DashAttackAction));

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
			if (UnitContainer.Instance == null || _pathDamageEffect == SkillEffect.None)
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

			// 경로 데미지 효과 적용 — skillID 귀속은 발동 스킬로.
			if (_hitList.Count > 0)
			{
				SkillEffectApplier.Apply(_pathDamageEffect, _caster, _skillId, _hitList);
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

			_caster.UnblockMove(nameof(DashAttackAction));
			_caster.UnblockSkill(nameof(DashAttackAction));
		}
	}
}
