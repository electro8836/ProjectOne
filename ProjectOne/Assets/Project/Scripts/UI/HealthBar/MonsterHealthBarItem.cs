using UnityEngine;
using UnityEngine.UI;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 풀링되는 몬스터 체력바. 체력 퍼센트만 표시(수치 텍스트 없음)하며,
	// 한 번이라도 피격당하면 사망할 때까지 계속 표시된다(HealthBarManager 가 사망 시 회수).
	public class MonsterHealthBarItem : MonoBehaviour, IPoolable
	{
		[SerializeField] private Slider _hpSlider;

		private UnitBase _unit;

		public UnitBase Unit
		{
			get { return _unit; }
		}

		// 풀에서 꺼낸 직후 호출 — 대상 유닛을 설정
		public void Bind(UnitBase unit)
		{
			_unit = unit;
		}

		// 체력 퍼센트 갱신 (MaxHP 0 이면 0 처리)
		public void RefreshValue()
		{
			if (_unit == null || _unit.Stats == null || _unit.Vitals == null)
			{
				return;
			}

			float maxHp = _unit.Stats.GetStat(StatInfo.MaxHP);
			_hpSlider.value = (maxHp > 0f) ? (_unit.Vitals.Hp / maxHp) : 0f;
		}

		public void SetWorldPosition(Vector3 pos)
		{
			transform.position = pos;
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
			_unit = null;
		}
	}
}
