using UnityEngine;

namespace ProjectOne.UI
{
	// 씬 Canvas에 붙이면 UIManager에 자동으로 등록/해제된다.
	// 씬 언로드 시 OnDestroy가 호출돼 _sceneCanvas가 null로 정리된다.
	[RequireComponent(typeof(Canvas))]
	public class SceneCanvasRegistrar : MonoBehaviour
	{
		private Canvas _canvas;

		private void Awake()
		{
			_canvas = GetComponent<Canvas>();

			if (UIManager.HasInstance)
			{
				UIManager.Instance.RegisterSceneCanvas(_canvas);
			}
		}

		private void OnDestroy()
		{
			if (UIManager.HasInstance)
			{
				UIManager.Instance.UnregisterSceneCanvas(_canvas);
			}
		}
	}
}
