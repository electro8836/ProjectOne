using UnityEngine;

namespace ProjectOne.Unit
{
	// 소환물에서 대상까지 이어지는 빔 연출 — 스프라이트를 대상 방향으로 돌리고 가로 스케일을 거리만큼 늘린다.
	//
	// VFXManager 를 쓰지 않는 이유 — VFXItem 은 종료 판정이 ParticleSystem 생존 여부라 스프라이트 빔에 맞지 않는다.
	// 소환물 자체가 SummonPool 로 풀링되므로 자식으로 달아 두면 별도 풀이 필요 없다.
	//
	// 전제 2가지 — 어긋나면 빔이 엉뚱하게 그려진다.
	// - 스프라이트 pivot 은 가운데여야 한다(임포트 기본값). 두 점의 중점에 놓고 양쪽으로 늘리기 때문이다.
	// - 부모(소환물)의 스케일은 1이어야 한다. Table_Summon 의 ScaleByRadius 를 켜면 길이가 그만큼 어긋난다.
	[RequireComponent(typeof(SpriteRenderer))]
	public class SummonBeamView : MonoBehaviour
	{
		private SpriteRenderer _renderer;

		// 스케일 1일 때의 빔 길이(월드 단위) — 스프라이트 원본 가로 크기다.
		private float _baseLength;

		private void Awake()
		{
			_renderer = GetComponent<SpriteRenderer>();
			if (_renderer.sprite != null)
			{
				_baseLength = _renderer.sprite.bounds.size.x;
			}

			if (_baseLength <= 0f)
			{
				Debug.LogError($"[SummonBeamView] 빔 스프라이트 가로 길이를 구할 수 없다 — {name}");
				_baseLength = 1f;
			}

			// 프리팹에는 켜진 채로 저장해 둔다(꺼두면 Awake 가 돌지 않는다). 시작 상태는 여기서 만든다.
			Hide();
		}

		// 두 월드 좌표를 잇는다. from 은 보통 소환물의 HitCenter 다.
		public void Show(Vector2 from, Vector2 to)
		{
			Vector2 delta = to - from;
			float distance = delta.magnitude;
			if (distance <= 0f)
			{
				Hide();
				return;
			}

			// pivot 이 가운데라 두 점의 중점에 놓아야 양 끝이 from/to 에 맞는다.
			transform.position = from + delta * 0.5f;
			transform.right = delta / distance;

			Vector3 scale = transform.localScale;
			scale.x = distance / _baseLength;
			transform.localScale = scale;

			_renderer.enabled = true;
		}

		public void Hide()
		{
			_renderer.enabled = false;
		}
	}
}
