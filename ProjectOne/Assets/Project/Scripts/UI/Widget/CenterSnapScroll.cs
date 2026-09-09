using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 가로 스크롤을 캐러셀로 만든다 — 항상 한 칸이 뷰포트 정중앙에 놓인다.
	//
	// 첫 칸도 중앙에서 시작해야 하므로 좌우에 "칸 없는 여백"이 필요하다. 그 여백과 Content 폭을
	// 여기서 직접 계산해 넣는다. LayoutGroup + ContentSizeFitter 조합에 맡기면 서로 폭을 우기다
	// 스크롤이 아예 죽는다(그 조합이 이 화면에서 실제로 그랬다).
	//
	// 어느 칸이 중앙인지만 소유한다 — 선택 표시를 어떻게 그릴지는 구독자가 정한다.
	[RequireComponent(typeof(ScrollRect))]
	public class CenterSnapScroll : MonoBehaviour, IEndDragHandler
	{
		[SerializeField] private ScrollRect _scrollRect;
		[SerializeField] private GridLayoutGroup _gridLayout;	// 여백 대상 겸 칸 크기 출처

		// 칸이 실제로 들어가는 부모. ScrollRect.content 와 다를 수 있다 —
		// Content 아래에 Grid 를 한 겹 더 두는 구성이 흔한데, 그때 content 의 자식은 Grid 하나뿐이라
		// 중앙 판정이 항상 같은 답을 낸다. 비워 두면 content 를 그대로 쓴다.
		[SerializeField] private RectTransform _itemsParent;

		[SerializeField] private float _snapDuration = 0.25f;

		// 중앙 칸이 바뀔 때만 발행한다 (스크롤 매 프레임이 아니라).
		public event Action<int> OnCenterChanged;

		public int CenterIndex
		{
			get { return _centerIndex; }
		}

		private int _centerIndex = -1;

		private void Reset()
		{
			_scrollRect = this.GetComponent<ScrollRect>();
		}

		private void Awake()
		{
			if (_scrollRect == null)
			{
				_scrollRect = this.GetComponent<ScrollRect>();
			}

			_scrollRect.onValueChanged.AddListener(onScrolled);
		}

		private void OnDestroy()
		{
			// 진행 중인 스냅 트윈이 파괴된 RectTransform 에 접근하지 않도록 정리
			if (_scrollRect != null && _scrollRect.content != null)
			{
				_scrollRect.content.DOKill();
			}

			_scrollRect.onValueChanged.RemoveListener(onScrolled);
		}

		// 칸을 다 채운 뒤 호출한다. 폭과 여백을 다시 잡고 첫 칸을 중앙에 세운다.
		public void Refresh()
		{
			applyContentSize();

			// 레이아웃이 이번 프레임에 아직 반영되지 않았다면 자식 위치가 전부 0 이라 중앙 판정이 틀어진다.
			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);

			_centerIndex = -1;
			SnapTo(0, false);
		}

		// 지정한 칸을 뷰포트 중앙으로 가져온다.
		public void SnapTo(int index, bool animate = true)
		{
			RectTransform child = getChild(index);
			if (child == null)
			{
				return;
			}

			RectTransform content = _scrollRect.content;
			content.DOKill();

			float targetX = getContentXForCenter(child);

			if (animate == false)
			{
				content.anchoredPosition = new Vector2(targetX, content.anchoredPosition.y);
				setCenter(index);
				return;
			}

			_scrollRect.velocity = Vector2.zero;
			content.DOAnchorPosX(targetX, _snapDuration).SetEase(Ease.OutCubic);

			// 표시가 이동을 기다릴 이유는 없다 — 목적지가 정해진 순간 선택도 정해진다.
			setCenter(index);
		}

		// 드래그를 놓으면 가장 가까운 칸으로 정렬한다. 관성에 맡기면 칸 사이에서 멈춘다.
		public void OnEndDrag(PointerEventData eventData)
		{
			_scrollRect.velocity = Vector2.zero;
			SnapTo(findNearestIndex());
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 첫 칸과 마지막 칸도 중앙까지 올 수 있도록 양옆에 반 화면씩 여백을 두고,
		// 그만큼 늘어난 Content 폭을 직접 지정한다.
		private void applyContentSize()
		{
			if (_gridLayout == null)
			{
				return;
			}

			int count = getActiveCount();
			if (count == 0)
			{
				return;
			}

			float viewportWidth = _scrollRect.viewport.rect.width;
			float cell = _gridLayout.cellSize.x;
			float spacing = _gridLayout.spacing.x;

			int pad = Mathf.Max(0, Mathf.RoundToInt((viewportWidth - cell) * 0.5f));
			_gridLayout.padding.left = pad;
			_gridLayout.padding.right = pad;

			float width = pad * 2 + count * cell + (count - 1) * spacing;

			RectTransform content = _scrollRect.content;
			content.sizeDelta = new Vector2(width, content.sizeDelta.y);

			LayoutRebuilder.MarkLayoutForRebuild(_gridLayout.transform as RectTransform);
		}

		// 그 칸이 뷰포트 중앙에 오려면 Content 가 어디에 있어야 하는가.
		// 앵커·피벗 설정에 덜 민감하도록 월드 좌표 차이로 구한다.
		private float getContentXForCenter(RectTransform child)
		{
			RectTransform content = _scrollRect.content;
			RectTransform viewport = _scrollRect.viewport;

			float childCenter = child.TransformPoint(child.rect.center).x;
			float viewCenter = viewport.TransformPoint(viewport.rect.center).x;

			float scale = content.lossyScale.x;
			if (Mathf.Approximately(scale, 0f) == true)
			{
				return content.anchoredPosition.x;
			}

			float target = content.anchoredPosition.x + (viewCenter - childCenter) / scale;

			// 여백 덕에 첫/마지막 칸도 범위 안이지만, 칸이 적어 스크롤이 필요 없을 땐 0 으로 고정된다.
			float limit = Mathf.Max(0f, (content.rect.width - viewport.rect.width) * 0.5f);
			return Mathf.Clamp(target, -limit, limit);
		}

		private void onScrolled(Vector2 _)
		{
			// 스냅 트윈이 도는 중에는 목적지가 이미 정해져 있다 — 중간 위치로 선택을 흔들지 않는다.
			if (DOTween.IsTweening(_scrollRect.content) == true)
			{
				return;
			}

			setCenter(findNearestIndex());
		}

		// 뷰포트 중심에 가장 가까운 활성 칸.
		private int findNearestIndex()
		{
			RectTransform items = getItemsParent();
			RectTransform viewport = _scrollRect.viewport;
			if (items == null || viewport == null)
			{
				return -1;
			}

			float center = viewport.TransformPoint(viewport.rect.center).x;

			int nearest = -1;
			float nearestDistance = float.MaxValue;
			int visibleIndex = 0;

			for (int i = 0; i < items.childCount; i++)
			{
				RectTransform child = items.GetChild(i) as RectTransform;
				if (child == null || child.gameObject.activeSelf == false)
				{
					continue;
				}

				float distance = Mathf.Abs(child.TransformPoint(child.rect.center).x - center);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = visibleIndex;
				}

				visibleIndex++;
			}

			return nearest;
		}

		// 활성 칸 중 index 번째.
		private RectTransform getChild(int index)
		{
			RectTransform items = getItemsParent();
			if (items == null || index < 0)
			{
				return null;
			}

			int visibleIndex = 0;
			for (int i = 0; i < items.childCount; i++)
			{
				RectTransform child = items.GetChild(i) as RectTransform;
				if (child == null || child.gameObject.activeSelf == false)
				{
					continue;
				}

				if (visibleIndex == index)
				{
					return child;
				}

				visibleIndex++;
			}

			return null;
		}

		private int getActiveCount()
		{
			RectTransform items = getItemsParent();
			if (items == null)
			{
				return 0;
			}

			int count = 0;
			for (int i = 0; i < items.childCount; i++)
			{
				if (items.GetChild(i).gameObject.activeSelf == true)
				{
					count++;
				}
			}

			return count;
		}

		private RectTransform getItemsParent()
		{
			return _itemsParent != null ? _itemsParent : _scrollRect.content;
		}

		private void setCenter(int index)
		{
			if (index < 0 || index == _centerIndex)
			{
				return;
			}

			_centerIndex = index;

			if (OnCenterChanged != null)
			{
				OnCenterChanged.Invoke(_centerIndex);
			}
		}
	}
}
