using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 플레이어 히어로 전용 체력바. 체력/스테미너 게이지와 현재 체력 수치를 상시 표시한다.
	// 히어로는 1명뿐이라 풀링하지 않고 HealthBarManager 가 직접 생성/파괴한다.
	public class HeroHealthBarItem : MonoBehaviour
	{
		[SerializeField] private Slider _hpSlider;
		[SerializeField] private Slider _staminaSlider;
		[SerializeField] private TMP_Text _currentHpText;

		private UnitBase _unit;
		private int _lastShownHp = -1;  // 직전 표시한 정수 체력 — 변할 때만 텍스트 갱신(불필요한 문자열 할당 방지)

		public UnitBase Unit
		{
			get { return _unit; }
		}

		public void Bind(UnitBase unit)
		{
			_unit = unit;
			_lastShownHp = -1;
		}

		// 체력/스테미너 퍼센트 슬라이더 + 현재 체력 수치 갱신 (Max 0 이면 0 처리)
		public void RefreshValues()
		{
			if (_unit == null || _unit.Stats == null || _unit.Vitals == null)
			{
				return;
			}

			float maxHp = _unit.Stats.GetStat(StatInfo.MaxHP);
			float hp = _unit.Vitals.Hp;
			_hpSlider.value = (maxHp > 0f) ? (hp / maxHp) : 0f;

			int shown = Mathf.CeilToInt(hp);
			if (shown != _lastShownHp)
			{
				_currentHpText.text = shown.ToString();
				_lastShownHp = shown;
			}

			float maxStamina = _unit.Stats.GetStat(StatInfo.MaxStamina);
			_staminaSlider.value = (maxStamina > 0f) ? (_unit.Vitals.Stamina / maxStamina) : 0f;
		}

		public void SetWorldPosition(Vector3 pos)
		{
			transform.position = pos;
		}
	}
}
