using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Aura
{
	// 유닛(오라 시전자)에 부착된 오라들을 관리 (POCO)
	// - 동일 ID 재적용 시: 갱신(무한이거나 더 긴 지속만) — BuffContainer 정책과 동일
	// - UnitBase.ManualTick 에서 Tick(dt) 위임 호출
	public sealed class AuraContainer
	{
		readonly UnitBase _owner;
		readonly Dictionary<AuraInfo, AuraRuntime> _byId = new Dictionary<AuraInfo, AuraRuntime>();
		readonly List<AuraRuntime> _ordered = new List<AuraRuntime>(4);
		readonly List<AuraInfo> _toRemove = new List<AuraInfo>(2);

		public AuraContainer(UnitBase owner)
		{
			_owner = owner;
		}

		public UnitBase Owner
		{
			get { return _owner; }
		}

		public void Apply(AuraInfo id, float duration, UnitBase source, SkillInfo sourceSkill)
		{
			if (id == AuraInfo.None)
			{
				return;
			}

			AuraRuntime existing;
			if (_byId.TryGetValue(id, out existing) == true)
			{
				// 동일 ID 재적용: 새 지속시간이 남은 시간보다 길 때만 갱신 (짧으면 무시)
				bool newIsInfinite = duration <= 0f;
				if (newIsInfinite == false)
				{
					if (existing.IsInfinite == true || duration < existing.RemainingDuration)
					{
						return;
					}
				}

				existing.Refresh(duration);
				return;
			}

			AuraRuntime rt = new AuraRuntime(id, _owner, source, duration, sourceSkill);
			_byId.Add(id, rt);
			_ordered.Add(rt);
		}

		public void Remove(AuraInfo id)
		{
			AuraRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return;
			}

			rt.Dispose();
			_byId.Remove(id);
			_ordered.Remove(rt);
		}

		public bool Has(AuraInfo id)
		{
			return _byId.ContainsKey(id);
		}

		public void Tick(float dt)
		{
			// 진행 (만료된 것 수집)
			_toRemove.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				AuraRuntime rt = _ordered[i];
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
