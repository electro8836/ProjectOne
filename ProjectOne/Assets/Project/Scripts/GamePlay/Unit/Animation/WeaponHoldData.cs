using UnityEngine;

namespace ProjectOne.Unit
{
	// 무기 쥐는 자세 데이터. 장비 행(Equipment.WeaponHold)이 주소로 고른다.
	//
	// 자세 회전은 Model 의 z 회전, 조준각별 오프셋은 Offset 의 위치로 HeroWeapon 이 직접 쓴다 — 애니메이터는 건드리지 않는다.
	// 평소엔 조준각이 0 이라 정면 값만 쓰이고, 공격 중엔 조준각별 값이 공격 모션(Motion)에 더해져
	// 공격 모션 하나로 각도마다 자연스러운 위치가 나오게 보정한다.
	// 조준각은 아래가 + 다(위 90 = -90, 아래 90 = +90). 사이 각은 이웃한 두 값을 선형 보간한다.
	[CreateAssetMenu(fileName = "WeaponHold_", menuName = "ProjectOne/Avatar/WeaponHoldData")]
	public class WeaponHoldData : ScriptableObject
	{
		// Model 자세 회전 z(도)
		[SerializeField] private float _poseRotation;

		[SerializeField] private Vector2 _offsetUp90;

		[SerializeField] private Vector2 _offsetUp45;

		[SerializeField] private Vector2 _offsetFront;

		[SerializeField] private Vector2 _offsetDown45;

		[SerializeField] private Vector2 _offsetDown90;

		public float PoseRotation
		{
			get { return _poseRotation; }
		}

		// 조준각(-90 ~ 90)의 Offset 위치
		public Vector3 GetOffset(float angle)
		{
			float a = Mathf.Clamp(angle, -90f, 90f);
			Vector2 result;
			if (a < -45f)
			{
				result = Vector2.Lerp(_offsetUp90, _offsetUp45, (a + 90f) / 45f);
			}
			else if (a < 0f)
			{
				result = Vector2.Lerp(_offsetUp45, _offsetFront, (a + 45f) / 45f);
			}
			else if (a < 45f)
			{
				result = Vector2.Lerp(_offsetFront, _offsetDown45, a / 45f);
			}
			else
			{
				result = Vector2.Lerp(_offsetDown45, _offsetDown90, (a - 45f) / 45f);
			}

			return new Vector3(result.x, result.y, 0f);
		}
	}
}
