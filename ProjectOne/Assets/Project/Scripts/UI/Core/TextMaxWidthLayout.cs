using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// TMP 텍스트의 선호 폭에 상한을 씌우는 레이아웃 요소.
	// TMP는 줄바꿈이 켜져 있어도 preferredWidth로 "한 줄 전체 길이"를 반환하기 때문에,
	// ContentSizeFitter만 쓰면 문구가 길수록 팝업이 끝없이 가로로 늘어난다.
	// 이 컴포넌트가 TMP보다 높은 우선순위로 폭을 _maxWidth까지만 잘라주면
	// 최대 폭에 도달한 뒤부터는 줄바꿈되어 세로로 늘어난다.
	[RequireComponent(typeof(TMP_Text))]
	[DisallowMultipleComponent]
	public class TextMaxWidthLayout : UIBehaviour, ILayoutElement
	{
		[SerializeField] private float _maxWidth = 900f;	// 텍스트가 가로로 늘어날 수 있는 한계 폭

		private TMP_Text _text;
		private float _cachedWidth;
		private float _cachedHeight;

		public float minWidth { get { return -1f; } }
		public float minHeight { get { return -1f; } }
		public float preferredWidth { get { return _cachedWidth; } }
		public float preferredHeight { get { return _cachedHeight; } }
		public float flexibleWidth { get { return -1f; } }
		public float flexibleHeight { get { return -1f; } }
		public int layoutPriority { get { return 1; } }	// TMP 본체(0)보다 높아야 이 값이 채택된다

		private TMP_Text text
		{
			get
			{
				if (_text == null)
				{
					_text = GetComponent<TMP_Text>();
				}

				return _text;
			}
		}

		public void CalculateLayoutInputHorizontal()
		{
			// 줄바꿈을 무시한 전체 길이를 구한 뒤 상한으로 자른다.
			float rawWidth = text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x;
			_cachedWidth = Mathf.Min(rawWidth, _maxWidth);
		}

		public void CalculateLayoutInputVertical()
		{
			// 가로 계산에서 확정된 폭 기준으로 줄바꿈시켰을 때의 높이.
			_cachedHeight = text.GetPreferredValues(text.text, _cachedWidth, Mathf.Infinity).y;
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			SetDirty();
		}

		protected override void OnRectTransformDimensionsChange()
		{
			base.OnRectTransformDimensionsChange();
			SetDirty();
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			SetDirty();
		}
#endif

		private void SetDirty()
		{
			if (!IsActive())
			{
				return;
			}

			LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
		}
	}
}
