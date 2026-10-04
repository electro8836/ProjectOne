using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Ranking;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 랭킹 목록의 한 줄(UIPrefab_PlayerRankSlot). 목록 칸과 MyRankSlot 의 내 칸이 같은 프리펩을 쓴다.
	//
	// 순위 표시: 1~3위는 메달 아이콘, 4~9999위는 숫자, 순위 외(0 이하·10000위 이상)는 "---".
	public class PlayerRankSlot : MonoBehaviour
	{
		// 순위 표시 한도. 이보다 아래는 숫자 대신 "---" 로 표시한다.
		private const int MaxCountedRank = 9999;
		private const int IconRankCount = 3;
		private const string RankIconPrefix = "Icon_Rank_0";
		private const string UncountedRankText = "---";

		[SerializeField] private UIButton _button;				// 루트 UIButton
		[SerializeField] private Image _rankIcon;				// RankGroup/RankIcon
		[SerializeField] private TMP_Text _rankText;			// RankGroup/RankText
		[SerializeField] private TMP_Text _playerNameText;		// PlayerGroup/PlayerNameText
		[SerializeField] private TMP_Text _battlePowerText;	// BattlePowerGroup/BattlePowerText
		[SerializeField] private Image _border;					// Frame/Border
		[SerializeField] private Color[] _topRankBorderColors;	// 1~3위 순서 — 금·은·동

		// 슬롯 클릭 — 누구의 정보를 띄울지는 소유 화면이 정한다.
		public event Action<PlayerRankSlot, string> OnClicked;

		private string _playerId;

		// 4위 이하에 쓸 테두리 색 — 프리펩에 설정된 색을 그대로 쓴다.
		private Color _defaultBorderColor;

		private void Awake()
		{
			if (_border != null)
			{
				_defaultBorderColor = _border.color;
			}

			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
		}

		public void Bind(in RankEntry entry)
		{
			_playerId = entry.playerId;

			applyRank(entry.rank);
			_playerNameText.text = entry.playerName;
			_battlePowerText.text = entry.battlePower.ToString("N0");
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void applyRank(int rank)
		{
			bool useIcon = rank >= 1 && rank <= IconRankCount;

			applyBorderColor(rank);

			_rankIcon.gameObject.SetActive(useIcon);
			_rankText.gameObject.SetActive(useIcon == false);

			if (useIcon == true)
			{
				_rankIcon.sprite = AtlasManager.Instance.Get(RankIconPrefix + rank.ToString());
				return;
			}

			if (rank < 1 || rank > MaxCountedRank)
			{
				_rankText.text = UncountedRankText;
				return;
			}

			_rankText.text = rank.ToString();
		}

		// 1~3위는 금·은·동, 그 외는 원래 색. 슬롯이 재사용되므로 매번 다시 칠한다.
		private void applyBorderColor(int rank)
		{
			if (_border == null)
			{
				return;
			}

			bool isTopRank = rank >= 1 && rank <= IconRankCount && _topRankBorderColors != null && rank <= _topRankBorderColors.Length;
			_border.color = isTopRank ? _topRankBorderColors[rank - 1] : _defaultBorderColor;
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this, _playerId);
			}
		}
	}
}
