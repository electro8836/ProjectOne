using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Skill;

namespace ProjectOne.Buff
{
	// 대시 공격 — 시전자가 바라보는 방향(Facing)으로 벽은 막되 적은 관통하며 발동 스킬의 ScanParam1 거리만큼 직진.
	// 속도 = 거리 / 버프 지속시간(등속). 경로상의 적은 한 번씩만 피격(버프 테이블 Effect 칼럼의 효과 적용).
	public sealed class BUFF_DASH_ATTACK : ITickableBuff
	{
		private const float _fallbackDistance = 6f;        // 발동 스킬 Row 가 없을 때 폴백 거리
		private const float _fallbackHitWidth = 0.6f;      // ScanParam2 미설정 시 폴백 피격 반경
		private const float _fallbackDuration = 0.3f;      // 지속시간이 무한/0 일 때 폴백

		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		private BuffRuntime _host;
		private bool _finished;
		private float _distance;
		private float _hitWidth;
		private Vector2 _startPos;

		// 중복 피격 방지 집합 + 검출/적용용 재사용 버퍼 (할당 방지)
		private readonly HashSet<UnitBase> _hitSet = new HashSet<UnitBase>();
		private readonly List<UnitBase> _queryBuffer = new List<UnitBase>(16);
		private readonly List<UnitBase> _hitList = new List<UnitBase>(8);

		public BUFF_DASH_ATTACK(UnitBase owner, UnitBase caster)
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

			// 발동 스킬 데이터에서 거리(ScanParam1)/너비(ScanParam2) 확보 (없으면 폴백)
			_distance = _fallbackDistance;
			_hitWidth = _fallbackHitWidth;
			if (_host != null)
			{
				Table_SkillInfo.Row skillRow = Table_SkillInfo.Get(_host.SourceSkill);
				if (skillRow != null)
				{
					_distance = skillRow.ScanParam1;
					if (skillRow.ScanParam2 > 0f)
					{
						_hitWidth = skillRow.ScanParam2;
					}
				}
			}

			Vector2 dir = _owner.Mover.Facing;
			if (dir.sqrMagnitude < 1e-6f)
			{
				dir = Vector2.right;
			}

			// 속도 = 거리 / 지속시간 (지속시간 동안 등속 이동)
			float duration = ResolveDuration();
			float speed = _distance / duration;

			_startPos = _owner.transform.position;
			_owner.Mover.SetFacing(dir);
			_owner.Mover.SetOverride(dir * speed, pierceUnits: true);
			_owner.BlockMove(nameof(BUFF_DASH_ATTACK));   // 대시 중 이동 입력(플레이어/자동전투) 무시
			_owner.BlockSkill(nameof(BUFF_DASH_ATTACK));

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

			// 막힘 — 다음 이동 위치가 갈 수 없는 지역(벽)이면 정지 (유닛은 관통하므로 벽만)
			if (_owner.Mover.OverrideBlocked == true)
			{
				return true;
			}

			// 경로상 적 검출 — 미타격 적만 1회 피격
			DamagePathEnemies();

			// 목표 거리 도달 시 종료
			Vector2 currentPos = _owner.transform.position;
			if ((currentPos - _startPos).sqrMagnitude >= _distance * _distance)
			{
				return true;
			}

			return false;
		}

		private void DamagePathEnemies()
		{
			if (UnitContainer.Instance == null || _host == null || _host.Effect == SkillEffect.None)
			{
				return;
			}

			_hitList.Clear();
			UnitContainer.Instance.SpatialHash.Query(_owner.HitCenter, _queryBuffer);
			for (int i = 0; i < _queryBuffer.Count; i++)
			{
				UnitBase u = _queryBuffer[i];
				if (u == null || u.IsDead == true || u == _owner)
				{
					continue;
				}

				if (TargetResolver.IsEnemy(_owner.Faction, u.Faction) == false)
				{
					continue;
				}

				if (_hitSet.Contains(u) == true)
				{
					continue;
				}

				float reach = _hitWidth + u.Radius;
				if (((Vector2)u.HitCenter - _owner.HitCenter).sqrMagnitude > reach * reach)
				{
					continue;
				}

				_hitSet.Add(u);
				_hitList.Add(u);
			}

			// 경로 데미지 효과 = 버프 테이블 Effect 칼럼(SE_DASH_ATTACK_DAMAGE_01). skillID 귀속은 발동 스킬로.
			if (_hitList.Count > 0)
			{
				SkillEffectApplier.Apply(_host.Effect, _owner, _host.SourceSkill, _hitList);
			}
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

			_owner.UnblockMove(nameof(BUFF_DASH_ATTACK));
			_owner.UnblockSkill(nameof(BUFF_DASH_ATTACK));
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
