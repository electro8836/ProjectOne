using UnityEngine;
using UnityEngine.UI;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 유닛 머리 위 상호작용 진행 게이지 (원형 방사형 채움 + 가운데 아이콘).
	//
	// 버텨서 하는 상호작용이 공용으로 쓴다 — 기믹 파훼·상자 개봉·자원 수집.
	// 한 캐릭터가 동시에 두 가지를 버틸 수는 없으므로 인스턴스는 하나면 된다.
	// UIManager 의 Canvas_World(WorldSpace) 자식으로 미리 놓고 필요할 때 켠다.
	//
	// 월드 캔버스인 이유 — 유닛을 따라다녀야 하는데 인스턴스가 하나뿐이라
	// UnitHealthBar 가 SpriteRenderer 를 쓰는 근거(유닛마다 붙는 다수 인스턴스)가 걸리지 않고,
	// 방사형 채움을 Image.fillAmount 로 공짜로 얻는다.
	public class InteractionGauge : MonoBehaviour
	{
		[Header("구성")]
		// 표시 단위. 이 오브젝트를 켜고 끄며, 위치도 이것을 옮긴다.
		[SerializeField] private GameObject _root;
		// type=Filled, fillMethod=Radial360
		[SerializeField] private Image _ring;
		[SerializeField] private Image _icon;

		[Header("배치")]
		// 대상 HitCenter 로부터 띄울 높이(월드 유닛)
		[SerializeField] private float _yOffset = 0.9f;

		[Header("아이콘")]
		// 같은 인덱스끼리 짝을 이룬다 — 딕셔너리는 인스펙터에 노출되지 않는다.
		[SerializeField] private InteractionKind[] _iconKinds;
		[SerializeField] private Sprite[] _iconSprites;

		private UnitBase _target;
		private float _lastProgress = -1f;

		private void Awake()
		{
			// 켜서 쓰는 방식이라 기본은 꺼진 상태다.
			Hide();
		}

		// 상호작용 시작 — 대상 위에 붙고 종류에 맞는 아이콘을 건다.
		public void Show(UnitBase target, InteractionKind kind)
		{
			if (_root == null || target == null)
			{
				return;
			}

			_target = target;
			_lastProgress = -1f;
			applyIcon(kind);
			SetProgress(0f);
			follow();

			_root.SetActive(true);
		}

		// 진행도 0~1. 같은 값이면 건드리지 않는다.
		public void SetProgress(float t)
		{
			if (_ring == null)
			{
				return;
			}

			float clamped = Mathf.Clamp01(t);
			if (Mathf.Approximately(clamped, _lastProgress) == true)
			{
				return;
			}

			_lastProgress = clamped;
			_ring.fillAmount = clamped;
		}

		public void Hide()
		{
			_target = null;
			_lastProgress = -1f;

			if (_root != null)
			{
				_root.SetActive(false);
			}
		}

		// 유닛이 움직인 뒤에 따라가야 하므로 LateUpdate 다.
		private void LateUpdate()
		{
			if (_root == null || _root.activeSelf == false)
			{
				return;
			}

			// 대상이 사라지거나 죽으면 스스로 걷힌다 — 호출자가 Hide 를 놓쳐도 남지 않는다.
			if (_target == null || _target.IsDead == true)
			{
				Hide();
				return;
			}

			follow();
		}

		private void follow()
		{
			Vector2 center = _target.HitCenter;
			_root.transform.position = new Vector3(center.x, center.y + _yOffset, _root.transform.position.z);
		}

		private void applyIcon(InteractionKind kind)
		{
			if (_icon == null || _iconKinds == null || _iconSprites == null)
			{
				return;
			}

			for (int i = 0; i < _iconKinds.Length; i++)
			{
				if (_iconKinds[i] != kind || i >= _iconSprites.Length)
				{
					continue;
				}

				_icon.sprite = _iconSprites[i];
				_icon.enabled = (_iconSprites[i] != null);
				return;
			}

			// 아이콘이 아직 준비되지 않은 종류는 게이지만 보여준다.
			_icon.enabled = false;
		}
	}
}
