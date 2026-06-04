using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 풀링되는 단일 브레이크 이미지. 지정 위치에 표시된 뒤 수명이 끝나면 풀에 반납된다.
	// 현재는 위치/수명만 관리하며, 추후 애니메이션이 적용된 이미지로 교체 예정.
	public class BreakImgItem : MonoBehaviour, IPoolable
	{
		[SerializeField] private float _lifeTime = 0.6f;   // 표시 후 사라지기까지 시간(초)

		private float _elapsed;

		// 풀에서 꺼낸 직후 호출 — 표시 위치를 설정
		public void Initialize(Vector3 worldPos)
		{
			_elapsed = 0f;
			transform.position = worldPos;
		}

		public void OnActivate()
		{
			_elapsed = 0f;
		}

		public void OnDeactivate()
		{
			_elapsed = 0f;
		}

		// BreakImgPool.TickAll()에서 매 프레임 호출. 수명이 끝나면 true를 반환한다.
		public bool Tick(float deltaTime)
		{
			_elapsed += deltaTime;
			return _elapsed >= _lifeTime;
		}
	}
}
