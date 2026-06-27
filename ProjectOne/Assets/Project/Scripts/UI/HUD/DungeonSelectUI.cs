using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 던전 선택 화면. UIManager.OpenOverlayAsync로 오버레이 캔버스에 열린다.
	// Table_Dungeon을 ID 오름차순으로 나열하고 좌우(이전/다음)로 넘기며 선택한다.
	// 선택된 던전의 이름·권장 레벨·아이콘(이전/현재/다음)을 표시한다.
	public class DungeonSelectUI : UIScreen
	{
		[Header("정보 표시")]
		[SerializeField] private TMP_Text _dungeonNameText;		// Text_DungeonName
		[SerializeField] private TMP_Text _recommendLevelText;	// RecommandLevel/Level_Text

		[Header("아이콘")]
		[SerializeField] private Image _iconCurrent;	// DungeonIcon_Current
		[SerializeField] private Image _iconPrev;		// DungeonIcon_Prev
		[SerializeField] private Image _iconNext;		// DungeonIcon_Next

		[Header("버튼")]
		[SerializeField] private UIButton _prevButton;		// Button_Prev
		[SerializeField] private UIButton _nextButton;		// Button_Next
		[SerializeField] private UIButton _battleButton;	// Button_Battle
		[SerializeField] private UIButton _returnButton;	// Button_Return

		// ID 오름차순으로 정렬된 던전 목록
		private readonly List<Table_Dungeon.Row> _dungeons = new List<Table_Dungeon.Row>();
		// 현재 선택 인덱스
		private int _index;

		// 0=prev, 1=current, 2=next 아이콘 주소 추적 (Acquire/Release 짝 맞춤용)
		private readonly string[] _iconAddresses = new string[3];

		private void Awake()
		{
			_prevButton.OnClickEvent += onPrevClicked;
			_nextButton.OnClickEvent += onNextClicked;
			_battleButton.OnClickEvent += onBattleClicked;
			_returnButton.OnClickEvent += onReturnClicked;
		}

		private void OnDestroy()
		{
			_prevButton.OnClickEvent -= onPrevClicked;
			_nextButton.OnClickEvent -= onNextClicked;
			_battleButton.OnClickEvent -= onBattleClicked;
			_returnButton.OnClickEvent -= onReturnClicked;

			releaseIcon(0);
			releaseIcon(1);
			releaseIcon(2);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			collectDungeons();
			_index = 0;
			refresh();
			return UniTask.CompletedTask;
		}

		private void onPrevClicked()
		{
			if (_index > 0)
			{
				_index--;
				refresh();
			}
		}

		private void onNextClicked()
		{
			if (_index < _dungeons.Count - 1)
			{
				_index++;
				refresh();
			}
		}

		private void onReturnClicked()
		{
			UIManager.Instance.CloseOverlayAsync().Forget();
		}

		private void onBattleClicked()
		{
			// 전투 진입 — 추후 구현
		}

		// 전체 던전을 ID 오름차순으로 모은다.
		private void collectDungeons()
		{
			_dungeons.Clear();

			List<Table_Dungeon.Row> all = new List<Table_Dungeon.Row>(Table_Dungeon.All().Values);
			all.Sort(compareById);
			for (int i = 0; i < all.Count; i++)
			{
				_dungeons.Add(all[i]);
			}
		}

		private int compareById(Table_Dungeon.Row a, Table_Dungeon.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		// 현재 선택 인덱스 기준으로 이름·권장레벨·아이콘(이전/현재/다음)을 갱신한다.
		private void refresh()
		{
			if (_dungeons.Count == 0)
			{
				return;
			}

			CancellationToken ct = this.GetCancellationTokenOnDestroy();

			Table_Dungeon.Row current = _dungeons[_index];
			_dungeonNameText.text = current.Name;
			_recommendLevelText.text = current.RecommandedLvText;

			bool hasPrev = _index > 0;
			bool hasNext = _index < _dungeons.Count - 1;

			_iconPrev.gameObject.SetActive(hasPrev);
			_iconNext.gameObject.SetActive(hasNext);
			_prevButton.gameObject.SetActive(hasPrev);
			_nextButton.gameObject.SetActive(hasNext);

			setIcon(1, _iconCurrent, current.Icon, ct).Forget();

			if (hasPrev)
			{
				setIcon(0, _iconPrev, _dungeons[_index - 1].Icon, ct).Forget();
			}

			if (hasNext)
			{
				setIcon(2, _iconNext, _dungeons[_index + 1].Icon, ct).Forget();
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		// async에서 ref 필드를 못 쓰므로 슬롯 인덱스로 _iconAddresses 를 참조한다.
		private async UniTask setIcon(int slot, Image image, string address, CancellationToken ct)
		{
			if (_iconAddresses[slot] == address)
			{
				return;
			}

			releaseIcon(slot);
			_iconAddresses[slot] = address;

			if (string.IsNullOrEmpty(address))
			{
				image.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddresses[slot] 을 비운다.
			Sprite atlasSprite = IconAtlasCache.Instance.Get(address);
			if (atlasSprite != null)
			{
				image.sprite = atlasSprite;
				_iconAddresses[slot] = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 요청됐으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddresses[slot] != address)
			{
				return;
			}

			if (icon != null)
			{
				image.sprite = icon;
			}
		}

		private void releaseIcon(int slot)
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (!string.IsNullOrEmpty(_iconAddresses[slot]) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddresses[slot]);
				_iconAddresses[slot] = null;
			}
		}
	}
}
