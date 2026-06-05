using UnityEngine;

namespace ProjectOne.Dev
{
	// 개발용 FPS 표시 — 빈 GameObject 에 붙여 사용
	// OnGUI 로 화면 왼쪽 위에 현재 FPS 를 출력
	public class FPSCounter : MonoBehaviour
	{
		[SerializeField] private int _fontSize = 24;
		[SerializeField] private Color _color = Color.white;

		private float _deltaTime;
		private GUIStyle _style;

		void Update()
		{
			// 지수이동평균으로 프레임 시간 평활화
			_deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;
		}

		void OnGUI()
		{
			if (_style == null)
			{
				_style = new GUIStyle(GUI.skin.label);
			}

			_style.fontSize = _fontSize;
			_style.normal.textColor = _color;

			float fps = 1f / Mathf.Max(_deltaTime, 0.0001f);
			GUI.Label(new Rect(10f, 10f, 500f, 200f), $"FPS: {fps:0.0}", _style);
		}
	}
}
