using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 보스 전멸기 파훼 기믹 위에 뜨는 진행 게이지 (원형 방사형 채움).
	//
	// 상호작용 공용 게이지(InteractionGauge)와 달리 **코어 1개당 1개**다 —
	// 파훼 코어는 동시에 여럿 떠 있고, 어느 것을 얼마나 밟았는지 각각 보여야 한다.
	// 그래서 히어로가 아니라 코어를 따라다니고, 붙어 있는 동안 늘 보인다(진행 전에는 0%).
	//
	// UIManager 의 Canvas_World(WorldSpace) 아래에 만들어진다 — UIManager.CreateBossGimmickGauge.
	public class BossGimmickGauge : MonoBehaviour
	{
		[Header("구성")]
		// type=Filled, fillMethod=Radial360
		[SerializeField] private Image _progress;

		[Header("배치")]
		// 대상 위치로부터 띄울 높이(월드 유닛)
		[SerializeField] private float _yOffset = 0.6f;

		private Transform _target;
		private float _lastProgress = -1f;

		// 코어에 건다. 이 시점부터 0% 로 보인다.
		public void Attach(Transform target)
		{
			_target = target;
			_lastProgress = -1f;
			SetProgress(0f);
			follow();
		}

		// 진행도 0~1. 같은 값이면 건드리지 않는다.
		public void SetProgress(float t)
		{
			if (_progress == null)
			{
				return;
			}

			float clamped = Mathf.Clamp01(t);
			if (Mathf.Approximately(clamped, _lastProgress) == true)
			{
				return;
			}

			_lastProgress = clamped;
			_progress.fillAmount = clamped;
		}

		// 코어가 움직인 뒤에 따라가야 하므로 LateUpdate 다.
		private void LateUpdate()
		{
			// 대상이 사라지거나(씬 언로드) 꺼지면(풀 반환) 스스로 걷힌다.
			// Canvas_World 는 영속인 UIManager 아래라 이 가드가 없으면 고아가 남는다 —
			// 완료·회수·풀반환·씬전환을 이 한 곳이 전부 덮으므로 코어 쪽에 회수 코드가 없다.
			if (_target == null || _target.gameObject.activeInHierarchy == false)
			{
				Destroy(gameObject);
				return;
			}

			follow();
		}

		private void follow()
		{
			Vector3 pos = _target.position;
			transform.position = new Vector3(pos.x, pos.y + _yOffset, transform.position.z);
		}
	}
}
