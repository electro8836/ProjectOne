using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Buff;
using ProjectOne.Event;
using ProjectOne.Skill;

namespace ProjectOne.Unit
{
	// 브레이크 게이지 — 엘리트/보스 전용.
	//
	// 일반 몬스터에는 아예 붙지 않는다. 부착은 MonsterPool.CreateItem 이 등급을 보고 결정한다
	// (프리팹마다 손으로 달면 등급이 바뀔 때마다 프리팹을 고쳐야 한다).
	//
	// Update 를 쓰지 않는다 — 유닛 갱신은 UnitSimulator → UnitBase.ManualTick 중앙 틱이
	// 담당하고, Monster.ManualTick 이 여기 Tick(dt) 를 위임한다.
	public class MonsterBreak : MonoBehaviour
	{
		private Monster _owner;

		// 현재 브레이크 게이지. 최대치는 Stat_MaxBreakGauge 가 정한다 (체력과 같은 층위 분리).
		private float _current;

		// 브레이크 진행 중 — 이 동안 추가 BreakDamage 는 전부 무시된다(재발동 금지).
		private bool _broken;

		private float _elapsed;

		// 부여된 BUFF_BreakStun 의 지속시간. 회복 진행률의 분모다.
		// 상수로 두지 않는 이유 — 일반 브레이크(5초)와 보스 패턴 파훼(10초)의 유지시간이 다르고,
		// 그 값의 단일 출처는 스킬이펙트 테이블이다.
		private float _duration;

		// 머리 위 브레이크 게이지 스프라이트. 엘리트 체력바 프리팹에만 있고 보스는 null 이다.
		// UnitHealthBar 는 이 컴포넌트가 AddComponent 되기 전에 Awake 를 마치므로 구독으로 잡을 수
		// 없다 — 값이 바뀔 때마다 여기서 밀어 넣는다.
		private UnitHealthBar _healthBar;

		// SkillEffectApplier 에 넘길 자기 자신 1개짜리 대상 목록 — 매 발동마다 할당하지 않는다.
		private readonly List<UnitBase> _selfBuffer = new List<UnitBase>(1);

		public float Current
		{
			get { return _current; }
		}

		public float Max
		{
			get
			{
				if (_owner == null || _owner.Stats == null)
				{
					return 0f;
				}

				return _owner.Stats.GetStat(Stat.Stat_MaxBreakGauge);
			}
		}

		public bool IsBroken
		{
			get { return _broken; }
		}

		// 부착 직후 1회. BuffContainer 는 UnitFactory.ComposeBase 에서 한 번 만들어지고
		// 교체되지 않으므로 여기서 잡은 핸들이 개체 수명 내내 유효하다.
		public void SetOwner(Monster owner)
		{
			_owner = owner;
			_healthBar = (_owner != null) ? _owner.GetComponentInChildren<UnitHealthBar>(true) : null;

			if (_owner != null && _owner.BuffContainer != null)
			{
				_owner.BuffContainer.BuffRemoved += onBuffRemoved;
			}

			ResetForSpawn();
		}

		// 풀 재사용 — 스폰마다 만충으로 되돌린다. 레벨이 바뀌었으면 새 최대치가 기준이 된다.
		public void ResetForSpawn()
		{
			_broken = false;
			_elapsed = 0f;
			_duration = 0f;
			setGauge(Max);
		}

		// 피격 1회분 차감. 스킬의 Table_Skill.BreakDamage 를 Monster.TakeDamage 가 넘긴다.
		public void ApplyBreakDamage(float amount)
		{
			// 브레이크 중에는 게이지가 회복 중이다 — 여기서 막지 않으면 회복분이 깎여
			// 기절이 끝나기도 전에 다시 0이 된다.
			if (_broken == true || amount <= 0f)
			{
				return;
			}

			setGauge(_current - amount);
			if (_current > 0f)
			{
				return;
			}

			triggerBreak();
		}

		// 패턴 파훼 — 남은 게이지와 무관하게 0으로 만들고 진행 상태로 넣는다.
		// 기절 부여는 호출자(BossPhaseRunner)가 SE_Boss_Break_Stun_Buff 로 이미 마쳤다.
		public void NotifyForcedBreak()
		{
			beginBreak();
		}

		// Monster.ManualTick 이 위임. 브레이크 유지시간 동안 0% → 100% 로 회복한다.
		public void Tick(float dt)
		{
			if (_broken == false)
			{
				return;
			}

			_elapsed += dt;

			float progress = (_duration > 0f) ? Mathf.Clamp01(_elapsed / _duration) : 1f;
			setGauge(Max * progress);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 게이지 값의 유일한 대입 지점 — 여기서 머리 위 게이지까지 함께 갱신한다.
		// 대입처가 흩어지면 표시만 옛 값에 남는 경로가 반드시 생긴다.
		void setGauge(float value)
		{
			_current = value;

			if (_healthBar == null)
			{
				return;
			}

			float max = Max;
			_healthBar.SetBreakRatio((max > 0f) ? _current / max : 0f);
		}

		void triggerBreak()
		{
			if (_owner == null)
			{
				return;
			}

			_selfBuffer.Clear();
			_selfBuffer.Add(_owner);
			SkillEffectApplier.Apply(SkillEffect.SE_Monster_Break_Stun_Buff, _owner, EDT.Skill.None, _selfBuffer, 0);
			_selfBuffer.Clear();

			beginBreak();
		}

		// 기절이 실제로 걸린 뒤에 부른다 — 유지시간을 버프에서 그대로 읽어 오기 때문이다.
		void beginBreak()
		{
			BuffRuntime rt = null;
			if (_owner != null && _owner.BuffContainer != null)
			{
				rt = _owner.BuffContainer.GetRuntime(EDT.Buff.BUFF_BreakStun);
			}

			// 기절이 안 걸렸으면 게이지만 0으로 두는 것은 의미가 없다 —
			// 회복시켜 줄 종료 콜백이 영영 오지 않아 게이지가 0에 굳는다.
			if (rt == null)
			{
				Debug.LogError("[MonsterBreak] BUFF_BreakStun 이 부여되지 않았습니다 — 게이지를 되돌립니다.");
				ResetForSpawn();
				return;
			}

			_broken = true;
			_elapsed = 0f;
			_duration = rt.RemainingDuration;
			setGauge(0f);

			// 기절이 실제로 걸린 경우에만 여기까지 온다 — 일반 브레이크와 패턴 파훼가 모두 이 지점을 지난다.
			EventManager.Instance.Publish(new MonsterBrokenEvent(_owner));
		}

		void onBuffRemoved(BuffRuntime rt)
		{
			if (rt == null || rt.Id != EDT.Buff.BUFF_BreakStun || _broken == false)
			{
				return;
			}

			// 완전 회복 + 면역 해제. 틱 진행률이 반올림으로 100%에 못 미쳐도 여기서 맞춰진다.
			_broken = false;
			_elapsed = 0f;
			_duration = 0f;
			setGauge(Max);
		}

		void OnDestroy()
		{
			if (_owner != null && _owner.BuffContainer != null)
			{
				_owner.BuffContainer.BuffRemoved -= onBuffRemoved;
			}
		}
	}
}
