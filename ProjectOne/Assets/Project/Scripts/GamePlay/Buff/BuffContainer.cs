using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 유닛에 걸린 버프/디버프 관리 (POCO)
	// - 동일 ID 재적용 시: 갱신(refresh duration) — 스택 정책은 본 작업 범위 외
	// - UnitBase.LateUpdate 에서 Tick(dt) 위임 호출
	public sealed class BuffContainer
	{
		readonly UnitBase _owner;
		readonly Dictionary<BuffInfo, BuffRuntime> _byId = new Dictionary<BuffInfo, BuffRuntime>();
		readonly List<BuffRuntime> _ordered = new List<BuffRuntime>(8);
		readonly List<BuffInfo> _activeView = new List<BuffInfo>(8);
		readonly List<BuffInfo> _debuffView = new List<BuffInfo>(8);
		readonly List<BuffInfo> _toRemove   = new List<BuffInfo>(4);

		public BuffContainer(UnitBase owner)
		{
			_owner = owner;
		}

		public UnitBase Owner
		{
			get { return _owner; }
		}

		public void Apply(BuffInfo id, float duration, float interval, UnitBase source)
		{
			if (id == BuffInfo.None)
			{
				return;
			}

			BuffRuntime existing;
			if (_byId.TryGetValue(id, out existing) == true)
			{
				// 동일 ID 재적용: 새 지속시간이 남은 시간보다 길 때만 갱신 (짧으면 무시 — 기존 상태이상 유지)
				bool newIsInfinite = duration <= 0f;
				if (newIsInfinite == false)
				{
					if (existing.IsInfinite == true || duration < existing.RemainingDuration)
					{
						return;
					}
				}

				existing.Refresh(duration, interval);
				return;
			}

			BuffRuntime rt = new BuffRuntime(id, _owner, source, duration, interval);
			_byId.Add(id, rt);
			_ordered.Add(rt);
		}

		public void Remove(BuffInfo id)
		{
			BuffRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return;
			}

			rt.Dispose();
			_byId.Remove(id);
			_ordered.Remove(rt);
		}

		public bool Has(BuffInfo id)
		{
			return _byId.ContainsKey(id);
		}

		public BuffRuntime GetRuntime(BuffInfo id)
		{
			BuffRuntime rt;
			_byId.TryGetValue(id, out rt);
			return rt;
		}

		// 보유 (디)버프 ID 조회 — 매 호출마다 내부 캐시 리스트 재구성
		public IReadOnlyList<BuffInfo> GetActive()
		{
			_activeView.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				_activeView.Add(_ordered[i].Id);
			}

			return _activeView;
		}

		public IReadOnlyList<BuffInfo> GetDebuffs()
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
			// 진행 (만료된 것 수집)
			_toRemove.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				BuffRuntime rt = _ordered[i];
				rt.Tick(dt);
				if (rt.IsExpired == true)
				{
					_toRemove.Add(rt.Id);
				}
			}

			// 만료 정리
			for (int i = 0; i < _toRemove.Count; i++)
			{
				Remove(_toRemove[i]);
			}

			_toRemove.Clear();
		}
	}
}
