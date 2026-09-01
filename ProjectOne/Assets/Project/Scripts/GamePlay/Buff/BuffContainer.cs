using System.Collections.Generic;
using EDT;
using ProjectOne.Skill;
using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 유닛에 걸린 버프/디버프 관리 (POCO)
	// - 재적용 처리는 Table_Buff.StackPolicy 가 결정한다 (설계 2.12)
	// - UnitBase.ManualTick 에서 Tick(dt) 위임 호출
	//
	// Independent 정책은 같은 ID의 인스턴스가 여러 개 공존하므로 ID 키 딕셔너리로 표현할 수 없다.
	// 그래서 리스트를 1차 저장소로 두고, 딕셔너리는 "대표 인스턴스" 조회용 인덱스로만 쓴다.
	public sealed class BuffContainer
	{
		readonly UnitBase _owner;
		readonly List<BuffRuntime> _ordered = new List<BuffRuntime>(8);
		readonly Dictionary<EDT.Buff, BuffRuntime> _firstById = new Dictionary<EDT.Buff, BuffRuntime>();
		readonly List<EDT.Buff> _activeView = new List<EDT.Buff>(8);
		readonly List<EDT.Buff> _debuffView = new List<EDT.Buff>(8);
		readonly List<BuffRuntime> _toRemove = new List<BuffRuntime>(4);

		public BuffContainer(UnitBase owner)
		{
			_owner = owner;
		}

		public UnitBase Owner
		{
			get { return _owner; }
		}

		// 버프가 컨테이너에서 빠진 직후 1회 발행한다.
		// 만료 · Remove · TryConsume 세 경로가 전부 removeAt 을 지나므로 새는 곳이 없다.
		public event System.Action<BuffRuntime> BuffRemoved;

		// duration / stackMax 는 부여하는 SkillEffect 가 정한다 (설계 8.3).
		public void Apply(EDT.Buff id, float duration, int stackMax, UnitBase source, EDT.Skill sourceSkill)
		{
			if (id == EDT.Buff.None)
			{
				return;
			}

			Table_Buff.Row row = Table_Buff.Get(id);
			if (row == null)
			{
				UnityEngine.Debug.LogError($"[BuffContainer] Table_Buff.Get({id}) == null");
				return;
			}

			// 상태이상 면역 — 엘리트/보스는 행동을 막는 버프(BlockFlags 보유)에 걸리지 않는다.
			// 받는 피해 증가처럼 스탯만 건드리는 디버프는 그대로 걸린다 — 디버프 빌드가 통해야 한다.
			// 반드시 걸려야 하는 것만 Buff.IgnoreImmune 로 예외 처리한다 (브레이크 강제 기절).
			if (row.IgnoreImmune == false && row.BlockFlags != null && row.BlockFlags.Length > 0
				&& _owner != null && _owner.IsCrowdControlImmune == true)
			{
				return;
			}

			BuffRuntime existing;
			if (_firstById.TryGetValue(id, out existing) == false)
			{
				addNew(id, duration, source, sourceSkill);
				return;
			}

			switch (row.StackPolicy)
			{
				case BuffStackPolicy.Refresh:
					// 지속시간만 갱신 — 둔화·기절·스탯 버프
					existing.Refresh(duration);
					break;

				case BuffStackPolicy.Independent:
					// 스택마다 독립 인스턴스 — 화상·출혈
					if (stackMax > 0 && countOf(id) >= stackMax)
					{
						// 상한에 도달하면 가장 오래된 것을 갱신해 회전시킨다.
						existing.Refresh(duration);
						break;
					}

					addNew(id, duration, source, sourceSkill);
					break;

				case BuffStackPolicy.Extend:
					// 남은 시간에 가산 — 보호막
					existing.Extend(duration);
					existing.AddStack(stackMax);
					break;

				case BuffStackPolicy.Ignore:
					// 이미 있으면 무시 — 고유 각인
					break;

				default:
					UnityEngine.Debug.LogError($"[BuffContainer] StackPolicy 가 None 입니다 — Buff:{id}");
					existing.Refresh(duration);
					break;
			}
		}

		// 걸린 버프를 전부 걷는다 — 풀 재사용(OnSpawnReset)이 이전 생의 상태를 물고 오지 않게.
		// removeAt 을 그대로 지나므로 행동차단 해제 · StatModifier 회수 · BuffRemoved 통지가 모두 정상 수행된다.
		public void Clear()
		{
			for (int i = _ordered.Count - 1; i >= 0; i--)
			{
				removeAt(i);
			}
		}

		// 같은 ID 를 전부 제거한다.
		public void Remove(EDT.Buff id)
		{
			for (int i = _ordered.Count - 1; i >= 0; i--)
			{
				if (_ordered[i].Id == id)
				{
					removeAt(i);
				}
			}
		}

		public bool Has(EDT.Buff id)
		{
			return _firstById.ContainsKey(id);
		}

		public BuffRuntime GetRuntime(EDT.Buff id)
		{
			BuffRuntime rt;
			_firstById.TryGetValue(id, out rt);
			return rt;
		}

		// 현재 중첩 수 — Independent 는 인스턴스 개수, 나머지는 대표 인스턴스의 Stack.
		public int GetStack(EDT.Buff id)
		{
			BuffRuntime rt;
			if (_firstById.TryGetValue(id, out rt) == false)
			{
				return 0;
			}

			int instances = countOf(id);
			return instances > 1 ? instances : rt.Stack;
		}

		// 스택 차감 — 부족하면 false. BuffConsume 효과의 게이트다 (설계 5.9).
		public bool TryConsume(EDT.Buff id, int count)
		{
			if (count <= 0)
			{
				return false;
			}

			if (GetStack(id) < count)
			{
				return false;
			}

			int remaining = count;

			// 독립 인스턴스부터 오래된 순으로 제거한다.
			for (int i = 0; i < _ordered.Count && remaining > 0; )
			{
				if (_ordered[i].Id != id)
				{
					i++;
					continue;
				}

				if (_ordered[i].Stack > 1)
				{
					_ordered[i].ConsumeStack(1);
					remaining--;
					continue;
				}

				removeAt(i);
				remaining--;
			}

			return remaining == 0;
		}

		public IReadOnlyList<EDT.Buff> GetActive()
		{
			_activeView.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				_activeView.Add(_ordered[i].Id);
			}

			return _activeView;
		}

		// DEBUFF 모션을 내는 버프가 하나라도 걸려 있는가.
		//
		// 활성 버프는 보통 한 자릿수라 순회로 충분하다 — 스택 정책마다 캐시 무효화 시점이
		// 달라 카운터를 따로 들면 어긋나기 쉽다(먼저 끝난 스턴이 남은 스턴의 연출을 꺼버린다).
		public bool HasDebuffMotion
		{
			get
			{
				for (int i = 0; i < _ordered.Count; i++)
				{
					if (_ordered[i].PlaysDebuffMotion == true)
					{
						return true;
					}
				}

				return false;
			}
		}

		public IReadOnlyList<EDT.Buff> GetDebuffs()
		{
			_debuffView.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].IsDebuff == true)
				{
					_debuffView.Add(_ordered[i].Id);
				}
			}

			return _debuffView;
		}

		public void Tick(float dt)
		{
			_toRemove.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				BuffRuntime rt = _ordered[i];
				rt.Tick(dt);
				if (rt.IsExpired == true)
				{
					_toRemove.Add(rt);
				}
			}

			for (int i = 0; i < _toRemove.Count; i++)
			{
				int index = _ordered.IndexOf(_toRemove[i]);
				if (index >= 0)
				{
					removeAt(index);
				}
			}

			_toRemove.Clear();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		void addNew(EDT.Buff id, float duration, UnitBase source, EDT.Skill sourceSkill)
		{
			BuffRuntime rt = new BuffRuntime(id, _owner, source, duration, sourceSkill);
			_ordered.Add(rt);
			if (_firstById.ContainsKey(id) == false)
			{
				_firstById[id] = rt;
			}
		}

		void removeAt(int index)
		{
			BuffRuntime rt = _ordered[index];
			rt.Dispose();
			_ordered.RemoveAt(index);

			// 대표 인스턴스가 사라졌으면 남은 것 중 하나로 갈아끼운다.
			BuffRuntime first;
			if (_firstById.TryGetValue(rt.Id, out first) == true && first == rt)
			{
				_firstById.Remove(rt.Id);
				for (int i = 0; i < _ordered.Count; i++)
				{
					if (_ordered[i].Id == rt.Id)
					{
						_firstById[rt.Id] = _ordered[i];
						break;
					}
				}
			}

			// 인덱스 정리가 끝난 뒤에 알린다 — 구독자가 컨테이너를 다시 조회해도 일관된 상태를 본다.
			if (BuffRemoved != null)
			{
				BuffRemoved(rt);
			}
		}

		int countOf(EDT.Buff id)
		{
			int n = 0;
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].Id == id)
				{
					n++;
				}
			}

			return n;
		}
	}
}
