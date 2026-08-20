using UnityEngine;
using EDT;

namespace ProjectOne.Unit
{
	// 유닛 머리 위 체력바. **UI 가 아니라 SpriteRenderer 로 그린다.**
	//
	// 월드 캔버스를 쓰지 않는 이유 — 유닛 프리팹에 붙어 다니면 위치 추적 코드가 아예 필요 없고,
	// 캔버스 리빌드 비용도 없다. 유닛이 죽으면 체력바도 함께 사라진다.
	//
	// Fill 은 Sliced 스프라이트라 size.x 로 길이를 조절한다(0~1 = 체력 비율).
	// 갱신은 UnitBase.ManualTick 이 위임한다 — 개별 Update 콜백을 늘리지 않는다.
	public class UnitHealthBar : MonoBehaviour
	{
		[Tooltip("체력에 따라 가로 길이가 변하는 Fill 스프라이트")]
		[SerializeField] private SpriteRenderer _fill;

		// 체력이 가득 찼을 때의 가로 길이. 프리팹 초기값을 기준으로 삼는다.
		private float _fullWidth = 1f;

		private UnitBase _owner;

		// 마지막으로 반영한 비율 — 값이 바뀔 때만 size 를 대입한다.
		private float _lastRatio = -1f;

		private void Awake()
		{
			_owner = this.GetComponentInParent<UnitBase>();

			if (_fill == null)
			{
				Debug.LogWarning($"[UnitHealthBar] Fill 스프라이트가 지정되지 않았습니다 — {this.name}");
				return;
			}

			_fullWidth = _fill.size.x;
		}

		// 유닛이 매 틱 호출한다. 비율이 그대로면 아무 일도 하지 않는다.
		public void Refresh()
		{
			if (_fill == null || _owner == null || _owner.Vitals == null || _owner.Stats == null)
			{
				return;
			}

			float maxHp = _owner.Stats.GetStat(Stat.Stat_MaxHp);
			if (maxHp <= 0f)
			{
				return;
			}

			float ratio = Mathf.Clamp01(_owner.Vitals.Hp / maxHp);
			if (Mathf.Approximately(ratio, _lastRatio) == true)
			{
				return;
			}

			_lastRatio = ratio;

			Vector2 size = _fill.size;
			size.x = _fullWidth * ratio;
			_fill.size = size;
		}
	}
}
