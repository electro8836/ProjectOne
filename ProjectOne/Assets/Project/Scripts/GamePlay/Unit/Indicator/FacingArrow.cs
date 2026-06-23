using UnityEngine;

namespace ProjectOne.Unit
{
	// 캐릭터가 바라보는 방향(facing)을 가리키는 화살표를 항상 표시한다(face 표시용).
	// 스프라이트는 placeholder 이며, 나중에 _arrowSprite 슬롯의 이미지만 교체하면 된다.
	public class FacingArrow : MonoBehaviour
	{
		[SerializeField] private Sprite _arrowSprite;	// 교체용 (나중에 이미지만 교체)
		[SerializeField] private string _sortingLayerName = "Shadow";	// 바닥(캐릭터 아래)
		[SerializeField] private int _sortingOrder = 0;
		[SerializeField] private float _spriteAngleOffset = -90f;	// 스프라이트가 위(+Y)를 향함 보정
		[SerializeField] private Vector2 _scale = Vector2.one;		// 화살표 크기 배율

		private UnitMover _mover;
		private Transform _arrowTr;

		private void Awake()
		{
			_mover = this.GetComponent<UnitMover>();
			createArrow();
		}

		private void Update()
		{
			if (_arrowTr == null)
			{
				return;
			}

			Vector2 facing = GetFacing();
			float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
			_arrowTr.localRotation = Quaternion.Euler(0f, 0f, angle + _spriteAngleOffset);
		}

		// 화살표 스프라이트 자식을 생성한다. 피벗(꼬리)이 루트에 오고, 위(+Y) 기준 스프라이트를 facing 으로 회전시킨다.
		private void createArrow()
		{
			GameObject go = new GameObject("FaceArrow");
			go.transform.SetParent(this.transform, false);
			go.transform.localScale = new Vector3(_scale.x, _scale.y, 1f);
			SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
			sr.sprite = _arrowSprite;
			sr.color = new Color(1f,1f,1f,0.5f);
			sr.sortingLayerName = _sortingLayerName;
			sr.sortingOrder = _sortingOrder;
			_arrowTr = go.transform;
		}

		private Vector2 GetFacing()
		{
			if (_mover == null)
			{
				return Vector2.right;
			}

			Vector2 facing = _mover.Facing;
			if (facing.sqrMagnitude < 1E-06f)
			{
				return Vector2.right;
			}

			return facing;
		}
	}
}
