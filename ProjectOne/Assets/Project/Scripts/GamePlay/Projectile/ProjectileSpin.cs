using UnityEngine;

namespace ProjectOne.Projectile
{
	// 발사체 이미지 회전 연출(표창 등). 이동 방향 회전은 루트(Projectile)가 매 프레임 쓰므로 이미지 자식에 붙여 그것만 돌린다.
	public class ProjectileSpin : MonoBehaviour
	{
		// 초당 회전 각(도). 음수면 시계 방향
		[SerializeField] private float _degreesPerSecond = -1080f;

		private void Update()
		{
			transform.Rotate(0f, 0f, _degreesPerSecond * Time.deltaTime);
		}
	}
}
