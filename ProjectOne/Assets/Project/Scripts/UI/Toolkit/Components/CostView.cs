using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Upgrade;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 재화 아이콘 + "x수량" 1칸 (UI Toolkit). 비용 1건의 필요량을 표시하고 보유량이 모자라면 빨갛게 칠한다.
	// 스타일은 Theme.uss 의 .cost.
	public sealed class CostView
	{
		private static readonly Color LackColor = new Color(1f, 0.35f, 0.35f);

		private readonly ToolkitIconLoader _icons;
		private readonly VisualElement _root;
		private readonly VisualElement _icon;
		private readonly Label _count;

		public VisualElement Root
		{
			get { return _root; }
		}

		public CostView(VisualElement parent, ToolkitIconLoader icons)
		{
			_icons = icons;

			_root = new VisualElement();
			_root.AddToClassList("cost");

			_icon = new VisualElement();
			_icon.AddToClassList("cost__icon");
			_root.Add(_icon);

			_count = new Label();
			_count.AddToClassList("cost__count");
			_root.Add(_count);

			parent.Add(_root);
		}

		public UniTask BindAsync(UpgradeCost cost, CancellationToken ct)
		{
			_count.text = "x" + cost.amount.ToString();
			_count.style.color = cost.IsEnough ? new StyleColor(StyleKeyword.Null) : new StyleColor(LackColor);

			Table_Currency.Row row = Table_Currency.Get(cost.currency);
			return _icons.SetAsync(_icon, (row != null) ? row.Icon : string.Empty, ct);
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
