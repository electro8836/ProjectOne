using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 한 줄짜리 설명 툴팁(UIPrefab_SimplePopup). 누른 슬롯 위에 잠깐 떴다가 스스로 사라진다.
	//
	// 정식 정보 팝업을 띄울 만큼의 내용이 없는 대상(재화·재료)에 쓴다.
	// 닫기 버튼도 딤도 없다 — 시간이 지나면 UIManager 가 파괴한다.
	public class SimplePopup : MonoBehaviour
	{
		[SerializeField] private TMP_Text _text;

		[Header("연출")]
		[SerializeField] private float _visibleSeconds = 2f;

		// 슬롯 위쪽 여백. "슬롯 중심에서 슬롯 높이 + 이 값" 만큼 올린다.
		[SerializeField] private float _anchorGap = 20f;

		// 화면 좌우 끝에서 최소한 띄울 여백
		[SerializeField] private float _screenPadding = 8f;

		public async UniTask ShowAsync(string text, RectTransform anchor, CancellationToken ct)
		{
			if (_text != null)
			{
				_text.text = text;
			}

			// ContentSizeFitter 가 크기를 확정해야 좌우 넘침을 계산할 수 있다.
			// 이번 프레임에 막 생성됐으므로 강제로 한 번 돌린다.
			Canvas.ForceUpdateCanvases();

			place(anchor);

			await UniTask.Delay(System.TimeSpan.FromSeconds(_visibleSeconds), cancellationToken: ct);
		}

		// 앵커(누른 슬롯) 위에 배치하고, 좌우로 잘리면 화면 안으로 밀어 넣는다.
		// **위쪽은 넘어가도 그대로 둔다** — 맨 윗줄 슬롯은 위로 삐져나가는 것이 정상이다.
		private void place(RectTransform anchor)
		{
			RectTransform self = this.transform as RectTransform;
			RectTransform parent = this.transform.parent as RectTransform;
			if (anchor == null || self == null || parent == null)
			{
				return;	// 위치 정보가 없으면 프리펩 그대로(중앙) 둔다
			}

			Vector2 screen = worldToScreen(anchor, anchor.position);
			screen.y += anchor.rect.height + _anchorGap;

			Canvas canvas = parent.GetComponentInParent<Canvas>();
			Camera camera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

			Vector2 local;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out local) == false)
			{
				return;
			}

			local.x = clampX(parent, self, local.x);

			// 루트는 앵커·피벗이 모두 (0.5, 0.5) 라 부모 로컬 좌표가 곧 anchoredPosition 이다.
			self.anchoredPosition = local;
		}

		// 팝업 폭 절반을 기준으로 부모(캔버스) 안쪽에 넣는다.
		// 팝업이 화면보다 넓으면 클램프할 자리가 없으므로 중앙에 둔다.
		private float clampX(RectTransform parent, RectTransform self, float x)
		{
			float halfSelf = self.rect.width * 0.5f;
			float halfParent = parent.rect.width * 0.5f;

			float limit = halfParent - halfSelf - _screenPadding;
			if (limit <= 0f)
			{
				return 0f;
			}

			return Mathf.Clamp(x, -limit, limit);
		}

		private static Vector2 worldToScreen(RectTransform target, Vector3 world)
		{
			Canvas canvas = target.GetComponentInParent<Canvas>();
			Camera camera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

			return RectTransformUtility.WorldToScreenPoint(camera, world);
		}
	}
}
