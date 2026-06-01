using UnityEngine;
using TMPro;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 풀링되는 단일 플로팅 텍스트. 위로 떠오르며 알파가 사라진 뒤 풀에 자가 반납한다.
	// 데미지 숫자뿐 아니라 향후 이미지형 텍스트(Break/Stun 등)도 같은 흐름으로 재사용 예정.
	public class DamageTextItem : MonoBehaviour, IPoolable
	{
		[SerializeField] private TMP_Text _text;
		[SerializeField] private float _moveSpeed = 1.5f;  // 위로 떠오르는 속도(월드 단위/초)
		[SerializeField] private float _lifeTime = 0.6f;   // 표시 후 사라지기까지 시간(초)

		private PoolBase<DamageTextItem> _ownerPool;
		private float _elapsed;

		// 풀에서 꺼낸 직후 호출 — 위치/문자열/색상/소속 풀을 설정
		public void Initialize(Vector3 worldPos, string text, Color color, PoolBase<DamageTextItem> pool)
		{
			_ownerPool = pool;
			_elapsed = 0f;
			transform.position = worldPos;
			_text.text = text;
			color.a = 1f;
			_text.color = color;
		}

		public void OnActivate()
		{
			_elapsed = 0f;
		}

		public void OnDeactivate()
		{
			_elapsed = 0f;
		}

		// DamageTextPool.TickAll()에서 매 프레임 호출. 수명이 끝나면 true를 반환한다.
		public bool Tick(float deltaTime)
		{
			_elapsed += deltaTime;
			transform.position += Vector3.up * (_moveSpeed * deltaTime);
			_text.alpha = 1f - (_elapsed / _lifeTime);
			return _elapsed >= _lifeTime;
		}
	}
}
