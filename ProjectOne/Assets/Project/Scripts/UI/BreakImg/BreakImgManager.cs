using UnityEngine;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 브레이크 이미지 진입점. GuardBreakTriggeredEvent를 구독해 피격 유닛 위로 브레이크 UI를 띄운다.
	// 전투 씬의 WorldUICanvas 하위에 배치되어 씬과 생명주기를 함께한다.
	public class BreakImgManager : MonoBehaviour
	{
		[SerializeField] private BreakImgPool _pool;
		[SerializeField] private float _offsetY = 0.0f;			// 콜라이더 반지름 위로 추가로 올릴 거리
		[SerializeField] private float _cullingMargin = 0.05f;	// 화면 경계 컬링 여유값

		private Camera _mainCamera;

		private void Awake()
		{
			_mainCamera = Camera.main;

			EventManager.Instance.Subscribe<GuardBreakTriggeredEvent>(onGuardBreak);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<GuardBreakTriggeredEvent>(onGuardBreak);
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
