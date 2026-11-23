using System;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Ranking;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 랭킹 목록 줄 1개 (UI Toolkit). 1~3위는 순위 아이콘과 금·은·동 이름 색, 그 밖은 숫자로 표시한다.
	// 집계 밖(0 이하·9999 초과)은 "---". 스타일은 UIToolkit/Popups/Ranking/Ranking.uss.
	public sealed class RankCell
	{
		private const int MaxCountedRank = 9999;
		private const int IconRankCount = 3;
		private const string RankIconPrefix = "Icon_Rank_0";
		private const string UncountedRankText = "---";

		private readonly VisualElement _root;
		private readonly VisualElement _rankIcon;
		private readonly Label _rank;
		private readonly Label _name;
		private readonly Label _power;

		private string _playerId;

		public event Action<RankCell, string> OnClicked;

		public RankCell(VisualElement parent)
		{
			_root = new VisualElement();
			_root.AddToClassList("rank-row");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			VisualElement rankBox = new VisualElement();
			rankBox.AddToClassList("rank-row__rank");
			rankBox.pickingMode = PickingMode.Ignore;
			_rankIcon = new VisualElement();
			_rankIcon.AddToClassList("rank-row__rank-icon");
			_rankIcon.pickingMode = PickingMode.Ignore;
			_rank = new Label();
			_rank.AddToClassList("rank-row__rank-text");
			_rank.pickingMode = PickingMode.Ignore;
			rankBox.Add(_rankIcon);
			rankBox.Add(_rank);
			_root.Add(rankBox);

			_name = new Label();
			_name.AddToClassList("rank-row__name");
			_name.pickingMode = PickingMode.Ignore;
			_root.Add(_name);

			_power = new Label();
			_power.AddToClassList("rank-row__power");
			_power.pickingMode = PickingMode.Ignore;
			_root.Add(_power);

			parent.Add(_root);
		}

		public VisualElement Root
		{
			get { return _root; }
		}

		public void Bind(in RankEntry entry)
		{
			_playerId = entry.playerId;
			applyRank(entry.rank);
			_name.text = entry.playerName;
			_power.text = entry.battlePower.ToString("N0");
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private void applyRank(int rank)
		{
			bool top = rank >= 1 && rank <= IconRankCount;
			for (int i = 1; i <= IconRankCount; i++)
			{
				_root.EnableInClassList("rank-row--top" + i.ToString(), rank == i);
			}

			_rankIcon.style.display = top ? DisplayStyle.Flex : DisplayStyle.None;
			_rank.style.display = top ? DisplayStyle.None : DisplayStyle.Flex;

			if (top == true)
			{
				Sprite icon = AtlasManager.Instance.Get(RankIconPrefix + rank.ToString());
				_rankIcon.style.backgroundImage = (icon != null) ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
				return;
			}

			_rank.text = (rank < 1 || rank > MaxCountedRank) ? UncountedRankText : rank.ToString();
		}

		private void onClicked(ClickEvent evt)
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this, _playerId);
			}
		}
	}
}
