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

		[Tooltip("체력바 배경 스프라이트")]
		[SerializeField] private SpriteRenderer _bg;

		[Tooltip("체력바 테두리 스프라이트")]
		[SerializeField] private SpriteRenderer _border;

		[Tooltip("브레이크 게이지 Fill 스프라이트 — 엘리트 전용, 없는 유닛은 비워 둔다")]
		[SerializeField] private SpriteRenderer _breakFill;

		[Tooltip("브레이크 게이지 배경 스프라이트")]
		[SerializeField] private SpriteRenderer _breakBg;

		[Tooltip("브레이크 게이지 테두리 스프라이트")]
		[SerializeField] private SpriteRenderer _breakBorder;

		// 체력이 가득 찼을 때의 가로 길이. 프리팹 초기값을 기준으로 삼는다.
		private float _fullWidth = 1f;

		// 브레이크 게이지가 가득 찼을 때의 가로 길이. 위와 같은 규칙이다.
		private float _breakFullWidth = 1f;

		private UnitBase _owner;

		// 정렬 단계를 알려주는 소유 유닛의 애니메이터.
		private UnitAnimator _animator;

		// 마지막으로 반영한 비율 — 값이 바뀔 때만 size 를 대입한다.
		private float _lastRatio = -1f;

		private float _lastBreakRatio = -1f;

		private void Awake()
		{
			_owner = this.GetComponentInParent<UnitBase>();
			_animator = this.GetComponentInParent<UnitAnimator>();

			if (_fill == null)
			{
				Debug.LogWarning($"[UnitHealthBar] Fill 스프라이트가 지정되지 않았습니다 — {this.name}");
				return;
			}

			_fullWidth = _fill.size.x;

			// 브레이크 게이지는 엘리트에만 있다 — 없으면 그냥 놔둔다.
			if (_breakFill != null)
			{
				_breakFullWidth = _breakFill.size.x;
			}
		}

		private void OnEnable()
		{
			// 풀에서 재사용되면 유닛의 체력이 리셋된다 — 다음 통지를 반드시 반영하도록 캐시를 비운다.
			_lastRatio = -1f;
			_lastBreakRatio = -1f;

			if (_owner != null)
			{
				_owner.HpChanged += onHpChanged;
			}

			if (_animator == null)
			{
				return;
			}

			_animator.SortingOrderChanged += ApplySortingOrder;

			// 아직 이벤트가 한 번도 오지 않았을 수 있다 — 현재 단계가 유효하면 즉시 반영한다.
			if (_animator.SortingOrder != int.MinValue)
			{
				ApplySortingOrder(_animator.SortingOrder);
			}
		}

		private void OnDisable()
		{
			if (_owner != null)
			{
				_owner.HpChanged -= onHpChanged;
			}

			if (_animator == null)
			{
				return;
			}

			_animator.SortingOrderChanged -= ApplySortingOrder;
		}

		// 체력바는 UnitRoot 의 SortingGroup 밖에 있으므로, 유닛 몸통의 정렬 단계 바로 위에
		// BG → Fill → Border 순으로 직접 얹는다.
		private void ApplySortingOrder(int order)
		{
			if (_bg != null)
			{
				_bg.sortingOrder = order + 1;
			}

			if (_fill != null)
			{
				_fill.sortingOrder = order + 2;
			}

			if (_border != null)
			{
				_border.sortingOrder = order + 3;
			}

			// 브레이크 게이지는 체력바 위 단계에 얹는다 — 같은 유닛 안에서 순서가 겹치지 않게.
			if (_breakBg != null)
			{
				_breakBg.sortingOrder = order + 4;
			}

			if (_breakFill != null)
			{
				_breakFill.sortingOrder = order + 5;
			}

			if (_breakBorder != null)
			{
				_breakBorder.sortingOrder = order + 6;
			}
		}

		// 브레이크 게이지 비율(0~1)을 Fill 가로 길이에 반영한다.
		// 체력과 달리 통지 이벤트가 없어 MonsterBreak 가 직접 밀어 넣는다.
		public void SetBreakRatio(float ratio)
		{
			if (_breakFill == null)
			{
				return;
			}

			float clamped = Mathf.Clamp01(ratio);
			if (Mathf.Approximately(clamped, _lastBreakRatio) == true)
			{
				return;
			}

			_lastBreakRatio = clamped;

			Vector2 size = _breakFill.size;
			size.x = _breakFullWidth * clamped;
			_breakFill.size = size;
		}

		// 유닛의 체력/최대체력이 바뀐 프레임에 1회 불린다 (UnitBase.HpChanged).
		private void onHpChanged(UnitBase unit)
		{
			if (_fill == null || _owner.Vitals == null || _owner.Stats == null)
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
