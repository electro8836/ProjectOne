using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 마스터리 스킬 트리 노드 팝업의 View(MVP). UIManager.ShowMasteryTraitPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 투자·회수 판정은 MasteryTraitPopupPresenter 가 한다.
	//
	// 다른 팝업과 달리 Frame 이 화면 중앙에 고정되지 않는다 — 누른 노드 옆에 붙어야 하므로
	// PlaceFrame 에서 노드의 스크린 좌표를 직접 읽어 배치한다.
	public class MasteryTraitPopup : UIScreen, IView
	{
		// 노드에서 Frame 까지의 가로 간격. 좌우 어느 쪽에 붙을지는 노드의 화면 위치가 정한다.
		private const float FRAME_X_OFFSET = 400f;

		[Header("프레임")]
		[SerializeField] private RectTransform _frameRect;	// Frame

		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _nameText;			// Frame/NameText
		[SerializeField] private TMP_Text _levelText;			// Frame/LevelText
		[SerializeField] private TMP_Text _descText;			// Frame/DescText
		[SerializeField] private RectTransform _descRect;		// Frame/DescText
		[SerializeField] private TMP_Text _requirePointText;	// Frame/RequirePointText

		[Header("배치 여백")]
		// 팝업이 놓일 수 있는 영역은 스킬트리 뷰포트보다 좁다 — 위는 화면 여백, 아래는 네비게이션 바가 겹친다.
		// 캔버스 로컬 단위다(프레임 높이 320 과 같은 축). 캔버스 6종이 스케일러 설정을 공유해 창/팝업이 같은 축을 쓴다.
		[SerializeField] private float _placementPaddingTop = 50f;
		[SerializeField] private float _placementPaddingBottom = 200f;

		[Header("버튼")]
		[SerializeField] private UIButton _plusButton;		// Frame/PointButtons/PlusButton
		[SerializeField] private UIButton _minusButton;		// Frame/PointButtons/MinusButton

		// 닫기는 ExitButton 과 Dim 두 경로다.
		// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dim 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
		[SerializeField] private UIButton _exitButton;	// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;	// Dim

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnPlusClicked;
		public event Action OnMinusClicked;
		public event Action OnExitClicked;

		private readonly MasteryTraitPopupPresenter _presenter = new MasteryTraitPopupPresenter();
		private UniTaskCompletionSource<bool> _tcs;

		// 배치가 끝날 때까지 숨겼다가 한 번에 보여주기 위한 그룹 —
		// 프레임이 중앙에서 노드 옆으로 튀는 것을 감춘다.
		private CanvasGroup _canvasGroup;

		// 팝업 루트(Dim 과 같은 크기). 스크린 좌표를 로컬 좌표로 되돌릴 때의 기준면이다.
		private RectTransform _rootRect;
		private Canvas _canvas;

		// 프리펩에 박힌 기준 높이 — Desc 가 이보다 길어진 만큼 Frame 도 커진다.
		private float _descBaseHeight;
		private float _frameBaseHeight;

		private void Awake()
		{
			_rootRect = (RectTransform)transform;
			_canvas = GetComponentInParent<Canvas>();

			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			// 상수로 박지 않고 프리펩 값을 기준선으로 잡는다 — 프리펩에서 크기를 바꿔도 코드가 따라간다.
			_descBaseHeight = _descRect.sizeDelta.y;
			_frameBaseHeight = _frameRect.sizeDelta.y;

			// 배치 완료 전까지 숨김 — Reveal 에서 보여준다.
			setVisible(false);

			_plusButton.OnClickEvent += onPlusClicked;
			_minusButton.OnClickEvent += onMinusClicked;
			_exitButton.OnClickEvent += onExitClicked;
			_dimButton.OnClickEvent += onExitClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_plusButton.OnClickEvent -= onPlusClicked;
			_minusButton.OnClickEvent -= onMinusClicked;
			_exitButton.OnClickEvent -= onExitClicked;
			_dimButton.OnClickEvent -= onExitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(int nodeId, TraitPopupAnchor anchor, CancellationToken ct)
		{
			return _presenter.ShowAsync(nodeId, anchor, ct);
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// 텍스트 4종. Desc 길이에 맞춰 Frame 높이도 여기서 확정한다(특징1).
		public void SetInfo(MasteryNodePopupData data)
		{
			_nameText.text = data.name;
			_levelText.text = data.levelText;
			_descText.text = data.desc;

			// 요구 포인트를 이미 채웠으면 문구 자체를 끈다 — 자리는 그대로 비워 둔다.
			bool hasRequire = string.IsNullOrEmpty(data.requireText) == false;
			_requirePointText.gameObject.SetActive(hasRequire);
			if (hasRequire == true)
			{
				_requirePointText.text = data.requireText;
			}

			applyDescHeight(data.desc);
		}

		// 조작 버튼 잠금. 숨기지 않고 interactable 만 끈다(회색 틴트는 UIButton 이 처리).
		public void SetControlsInteractable(bool plus, bool minus)
		{
			_plusButton.interactable = plus;
			_minusButton.interactable = minus;
		}

		// 누른 노드 옆에 Frame 을 배치한다(특징2). SetInfo 로 높이가 확정된 뒤에 호출해야 한다.
		public void PlaceFrame(TraitPopupAnchor anchor)
		{
			if (anchor == null || anchor.nodeRect == null)
			{
				return;	// 위치 정보가 없으면 프리펩 그대로(중앙) 둔다
			}

			// 트리 쪽 레이아웃이 이번 프레임에 갱신됐을 수 있다 — 확정 좌표로 만든 뒤 읽는다.
			Canvas.ForceUpdateCanvases();

			if (anchor.treeScroll != null && anchor.treeViewport != null)
			{
				scrollNodeIntoView(anchor);
				Canvas.ForceUpdateCanvases();
			}

			Vector2 nodeScreen = worldToScreen(anchor.nodeRect, anchor.nodeRect.position);

			// 스크롤로 다 못 밀어낸 넘침을 여기서 막는다 — 트리 맨 위/맨 아래 노드는 스크롤이 이미 끝이라
			// 노드 Y 에 그대로 붙이면 프레임이 밴드 밖(위 여백·네비게이션 바)으로 삐져나간다.
			if (anchor.treeViewport != null)
			{
				nodeScreen.y = clampToBand(anchor.treeViewport, anchor.nodeRect);
			}

			// 좌우는 노드의 화면 위치가 정한다 — 뷰포트 가로 중앙보다 왼쪽이면 오른쪽에 붙인다.
			float offsetX = FRAME_X_OFFSET;
			if (anchor.treeViewport != null)
			{
				Vector2 viewportCenter = worldToScreen(anchor.treeViewport, anchor.treeViewport.position);
				if (nodeScreen.x >= viewportCenter.x)
				{
					offsetX = -FRAME_X_OFFSET;
				}
			}

			Camera camera = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;

			Vector2 local;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, nodeScreen, camera, out local) == false)
			{
				return;
			}

			// Frame 은 앵커·피벗이 모두 (0.5, 0.5) 라 루트 로컬 좌표가 곧 anchoredPosition 이다.
			_frameRect.anchoredPosition = new Vector2(local.x + offsetX, local.y);
		}

		// Presenter 가 배치까지 끝낸 뒤 호출 — 채워진 상태로 한 번에 표시.
		public void Reveal()
		{
			setVisible(true);
		}

		// 닫힘 대기 — Presenter 의 ShowAsync 가 마지막에 await 한다.
		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 입력(Exit/Dim)으로 닫힘을 확정한다.
		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// ── 내부: 레이아웃 ─────────────────────────────────────────────────

		// DescText 가 기준 높이보다 길어진 만큼 Frame 을 키운다.
		// 프리펩 앵커가 이미 그렇게 잡혀 있어 추가 레이아웃 컴포넌트가 필요 없다 —
		// Desc 는 상단 기준(pivot y=1)으로 아래로 자라고, 요구 문구와 버튼은 하단 앵커라 함께 밀려난다.
		private void applyDescHeight(string desc)
		{
			float width = _frameRect.rect.width + _descRect.sizeDelta.x;
			float preferred = _descText.GetPreferredValues(desc, width, Mathf.Infinity).y;
			float descHeight = (preferred < _descBaseHeight) ? _descBaseHeight : preferred;

			_descRect.sizeDelta = new Vector2(_descRect.sizeDelta.x, descHeight);
			_frameRect.sizeDelta = new Vector2(_frameRect.sizeDelta.x, _frameBaseHeight + (descHeight - _descBaseHeight));
		}

		// 팝업이 놓일 수 있는 세로 구간 — 뷰포트에서 위/아래 여백을 깎은 것. 뷰포트 로컬 좌표다.
		// 스크린 변환을 거치지 않으므로 해상도·캔버스 배율과 무관하다.
		private Rect placementBand(RectTransform viewport)
		{
			Rect r = viewport.rect;
			float yMin = r.yMin + _placementPaddingBottom;
			float yMax = r.yMax - _placementPaddingTop;

			// 여백 합이 뷰포트보다 크면 구간이 뒤집힌다 — 중앙의 높이 0 구간으로 무너뜨려
			// 이후 클램프가 "중앙 정렬"로 동작하게 둔다.
			if (yMin > yMax)
			{
				float center = (yMin + yMax) * 0.5f;
				yMin = center;
				yMax = center;
			}

			return new Rect(r.xMin, yMin, r.width, yMax - yMin);
		}

		// 프레임 중심을 배치 밴드 안으로 가둔 스크린 Y 를 돌려준다.
		// 계산은 뷰포트 로컬에서 하고 마지막에만 스크린으로 되돌린다.
		private float clampToBand(RectTransform viewport, RectTransform node)
		{
			Rect band = placementBand(viewport);
			float nodeY = viewport.InverseTransformPoint(node.position).y;

			float frameHalf = _frameRect.rect.height * 0.5f;
			float bandHalf = band.height * 0.5f;

			// 밴드가 프레임보다 짧으면 어디에 놓아도 넘친다 — 중앙에 두는 것이 최선이다.
			float y = (frameHalf > bandHalf)
				? band.center.y
				: Mathf.Clamp(nodeY, band.yMin + frameHalf, band.yMax - frameHalf);

			Vector3 world = viewport.TransformPoint(new Vector3(0f, y, 0f));
			return worldToScreen(viewport, world).y;
		}

		// 프레임이 배치 밴드 위아래로 넘치면 트리를 스크롤해 노드를 밴드 안으로 끌어온다.
		// 스킬트리 ScrollRect 는 세로 전용이라 가로는 손대지 않는다.
		//
		// 계산은 전부 뷰포트 로컬 단위다 — content.anchoredPosition 과 같은 축이라 변환 없이 더할 수 있다.
		private void scrollNodeIntoView(TraitPopupAnchor anchor)
		{
			RectTransform content = anchor.treeScroll.content;
			if (content == null)
			{
				return;
			}

			RectTransform viewport = anchor.treeViewport;
			Rect band = placementBand(viewport);

			float frameHalf = _frameRect.rect.height * 0.5f;
			float bandHalf = band.height * 0.5f;

			// 밴드가 프레임보다 짧으면 스크롤로는 해결되지 않는다 — 밴드에 맞춰 놓고 넘치는 만큼은 포기한다.
			if (frameHalf > bandHalf)
			{
				frameHalf = bandHalf;
			}

			float nodeY = viewport.InverseTransformPoint(anchor.nodeRect.position).y;

			// 넘친 만큼만 움직인다. 콘텐츠를 올리면(anchoredPosition.y +) 노드도 위로 올라가므로,
			// "위로 넘쳤다(over > 0)" 는 콘텐츠를 그만큼 내려야 한다는 뜻이다 — 부호가 반대다.
			float over = 0f;
			if (nodeY + frameHalf > band.yMax)
			{
				over = (nodeY + frameHalf) - band.yMax;
			}
			else if (nodeY - frameHalf < band.yMin)
			{
				over = (nodeY - frameHalf) - band.yMin;
			}

			if (Mathf.Approximately(over, 0f) == true)
			{
				return;
			}

			// 스크롤 가능 범위를 벗어나면 끝에서 멈춘다(고무줄로 늘어나지 않게).
			// 여기서 빼는 것은 여백을 깎지 않은 뷰포트 높이다 — ScrollRect 의 실제 스크롤 폭이다.
			float scrollable = content.rect.height - viewport.rect.height;
			if (scrollable < 0f)
			{
				scrollable = 0f;
			}

			float y = Mathf.Clamp(content.anchoredPosition.y - over, 0f, scrollable);
			content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
		}

		// 대상이 속한 캔버스 기준으로 월드 좌표를 스크린 좌표로 옮긴다(Overlay 면 카메라 없음).
		private Vector2 worldToScreen(RectTransform target, Vector3 world)
		{
			Canvas canvas = target.GetComponentInParent<Canvas>();
			Camera camera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

			return RectTransformUtility.WorldToScreenPoint(camera, world);
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onPlusClicked()
		{
			if (OnPlusClicked != null) { OnPlusClicked.Invoke(); }
		}

		private void onMinusClicked()
		{
			if (OnMinusClicked != null) { OnMinusClicked.Invoke(); }
		}

		private void onExitClicked()
		{
			if (OnExitClicked != null) { OnExitClicked.Invoke(); }
		}
	}
}
