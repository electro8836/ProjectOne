using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 브레이크 이미지 전역 진입점. GuardBreakTriggeredEvent를 구독해 피격 유닛 위로 브레이크 UI를 띄운다.
	public class BreakImgManager : MonoSingleton<BreakImgManager>
	{
		[SerializeField] private BreakImgPool _pool;
		[SerializeField] private float _offsetY = 0.0f;			// 콜라이더 반지름 위로 추가로 올릴 거리
		[SerializeField] private float _cullingMargin = 0.05f;	// 화면 경계 컬링 여유값

		private Camera _mainCamera;

		protected override void Awake()
		{
			base.Awake();
			_mainCamera = Camera.main;

			EventManager.Instance.Subscribe<GuardBreakTriggeredEvent>(onGuardBreak);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<GuardBreakTriggeredEvent>(onGuardBreak);

			base.OnDestroy();
		}

		private void Update()
		{
			_pool.TickAll(Time.deltaTime);
		}

		private void onGuardBreak(GuardBreakTriggeredEvent e)
		{
			if (e.Victim == null)
			{
				return;
			}

			float upOffset = e.Victim.Radius * 2 + _offsetY;
			Vector3 worldPos = e.Victim.transform.position + Vector3.up * upOffset;

			if (isOffscreen(worldPos))
			{
				return;
			}

			_pool.Spawn(worldPos);
		}

		private bool isOffscreen(Vector3 worldPos)
		{
			if (_mainCamera == null)
			{
				_mainCamera = Camera.main;
				if (_mainCamera == null)
				{
					return false;
				}
			}

			Vector3 vp = _mainCamera.WorldToViewportPoint(worldPos);
			return vp.z < 0f
				|| vp.x < -_cullingMargin || vp.x > 1f + _cullingMargin
				|| vp.y < -_cullingMargin || vp.y > 1f + _cullingMargin;
		}
	}
}
