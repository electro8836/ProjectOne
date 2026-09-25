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
	// 순위 표시: 1~3위는 메달 아이콘, 4~10000위는 숫자, 집계 밖(0 이하·10001위 이상)은 "---".
	public class PlayerRankSlot : MonoBehaviour
	{
		// 순위 집계 한도. 이보다 아래는 숫자 대신 "---" 로 표시한다.
		private const int MaxCountedRank = 10000;
		private const int IconRankCount = 3;
		private const string RankIconPrefix = "Icon_Rank_0";
		private const string UncountedRankText = "---";

		[SerializeField] private UIButton _button;				// 루트 UIButton
		[SerializeField] private Image _rankIcon;				// RankGroup/RankIcon
		[SerializeField] private TMP_Text _rankText;			// RankGroup/RankText
		[SerializeField] private TMP_Text _playerNameText;		// PlayerGroup/PlayerNameText
		[SerializeField] private TMP_Text _battlePowerText;	// BattlePowerGroup/BattlePowerText

		// 슬롯 클릭 — 누구의 정보를 띄울지는 소유 화면이 정한다.
		public event Action<PlayerRankSlot, string> OnClicked;

		private string _playerId;

		private void Awake()
		{
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

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this, _playerId);
			}
		}
	}
}
