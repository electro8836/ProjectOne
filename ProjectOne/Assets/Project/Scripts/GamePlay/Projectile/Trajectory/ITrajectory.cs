using UnityEngine;

namespace ProjectOne.Projectile
{
	// 발사체 이동 방식 추상화 — 궤적별 컴포넌트가 구현하고 Projectile 이 위임한다.
	// 궤적별 고유 파라미터(선회속도/아크높이 등)는 각 구현 컴포넌트가 [SerializeField]로 소유한다.
	public interface ITrajectory
	{
		// 발사 시 1회 초기화 — 런타임 컨텍스트(방향/타겟/시작위치)와 속도/사거리/수명 수신
		void OnLaunch(in ProjectileData data, float speed, float maxDistance, float lifeTime);

		// 한 프레임 진행 — position 갱신, 이동 방향(facing) 산출. 반환값 = 이번 프레임 실제 이동거리(사거리 누적용)
		float Tick(float deltaTime, ref Vector3 position, out Vector2 facing);

		// 궤적 자체 종료 신호 (유도: 타겟 소실 후 마지막 위치 도달)
		bool IsFinished { get; }
	}
}
