using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using EDT;
using ProjectOne.Event;
using ProjectOne.Quests;
using ProjectOne.UserData;
using Cysharp.Threading.Tasks;

namespace ProjectOne.UI
{
	// 메인 HUD 퀘스트 패널. 진행 중인 퀘스트 1개를 표시하고 FoldButton 으로 Frame 을 접는다.
	//
	// HeroInfo · BossUI 와 같은 결의 자율 컴포넌트다(MVP 를 쓰지 않는다).
	// 퀘스트는 초당 수십 번 바뀌지 않으므로 폴링하지 않고 QuestChangeEvent 로만 갱신한다.
	//
	// 실제 비용을 막는 것은 _lastXxx 캐시 게이트다 — 같은 문자열을 다시 대입해도 TMP 는 매번 dirty 가 된다.
	public class QuestInfo : MonoBehaviour
	{
		private const float SlideDuration = 1f;	// 접기/펼치기 블렌드 시간(고정)

		// 목표를 달성한 상태의 강조색.
		private static readonly Color MetColor = new Color(0.0078431f, 1f, 0f, 1f);		// #02FF00
		private static readonly Color BorderNormalColor = Color.black;
		private static readonly Color QuestInfoBorderNormalColor = new Color32(0x63, 0x63, 0x63, 0xFF);

		[Header("Fold")]
		[SerializeField] private RectTransform _frame;
		[SerializeField] private UIButton _foldButton;
		[SerializeField] private GameObject _closeImage;	// 펼침 상태에서 표시
		[SerializeField] private Image _openIcon;			// 접힘 상태에서 표시

		[Header("Quest")]
		[SerializeField] private UIButton _frameButton;		// CompleteType=UI 인 퀘스트의 수령 버튼
		[SerializeField] private TMP_Text _titleText;
		[SerializeField] private TMP_Text _descText;
		[SerializeField] private TMP_Text _completeText;	// 목표 달성 시에만 표시
		[SerializeField] private Slider _progress;
		[SerializeField] private Image _border;				// Frame 의 테두리
		[SerializeField] private UIButton _questInfoButton;	// Frame/QuestInfoButton — 퀘스트 목록 팝업 열기
		[SerializeField] private Image _questInfoBorder;	// Frame/QuestInfoButton/Frame/Border

		private Action<QuestChangeEvent> _onQuestChanged;
		private Action<CharacterChangeEvent> _onCharacterChanged;

		private float _expandedX;	// 프리팹에 배치된 펼침 위치
		private float _foldedX;		// 펼침 위치에서 Frame 너비만큼 왼쪽
		private bool _isExpanded = true;
		private bool _isSliding;

		// 마지막으로 그린 값 — 변화가 있을 때만 대입한다.
		private int _lastQuestId = -1;
		private string _lastDesc;
		private float _lastRatio = -1f;
		private int _lastMet = -1;	// -1 미정 / 0 미달성 / 1 달성

		private void Awake()
		{
			if (_frame != null)
			{
				_expandedX = _frame.anchoredPosition.x;
				_foldedX = _expandedX - _frame.rect.width;
			}

			if (_foldButton != null)
			{
				_foldButton.OnClickEvent += onFoldClicked;
			}

			if (_frameButton != null)
			{
				_frameButton.OnClickEvent += onFrameClicked;
			}

			if (_questInfoButton != null)
			{
				_questInfoButton.OnClickEvent += onQuestInfoClicked;
			}

			_onQuestChanged = onQuestChanged;
			_onCharacterChanged = onCharacterChanged;
			EventManager.Instance.Subscribe<QuestChangeEvent>(_onQuestChanged);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(_onCharacterChanged);

			// 프리팹 저장값과 무관하게 펼침 상태로 시작한다
			applyState(true, false);
			refresh();
		}

		private void OnDestroy()
		{
			if (_frame != null)
			{
				_frame.DOKill();
			}

			if (_foldButton != null)
			{
				_foldButton.OnClickEvent -= onFoldClicked;
			}

			if (_frameButton != null)
			{
				_frameButton.OnClickEvent -= onFrameClicked;
			}

			if (_questInfoButton != null)
			{
				_questInfoButton.OnClickEvent -= onQuestInfoClicked;
			}

			EventManager.Instance.Unsubscribe<QuestChangeEvent>(_onQuestChanged);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(_onCharacterChanged);
		}

		// ── 퀘스트 표시 ───────────────────────────────────────────────

		private void onQuestChanged(QuestChangeEvent e)
		{
			refresh();
		}

		// ReachLevel 목표는 전용 이벤트가 없다 — 레벨/경험치 알림으로 재평가한다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refresh();
		}

		private void refresh()
		{
			QuestBook book = Account.Instance.Quests;
			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();

			// 마지막 퀘스트까지 다 깼으면 패널째 숨긴다.
			if (baked == null)
			{
				this.gameObject.SetActive(false);
				return;
			}

			this.gameObject.SetActive(true);

			int questId = baked.row.ID;
			if (_lastQuestId != questId)
			{
				_lastQuestId = questId;

				if (_titleText != null)
				{
					_titleText.text = baked.row.Name;
				}
			}

			// 분자/분모는 QuestBook 이 목표 타입 차이를 이미 흡수했다 — 설명문과 게이지가 같은 값을 쓴다.
			int current = book.GetCurrentCount(baked);
			int required = QuestBook.GetRequiredCount(baked);

			applyDesc(baked, current, required);
			applyProgress(current, required);
			applyMetState(book.IsObjectiveMet(questId));
		}

		// 설명문 뒤에 진행도를 붙인다 — 예: "안개 어린 산길의 몬스터 처치 (12/50)".
		private void applyDesc(QuestCatalog.BakedQuest baked, int current, int required)
		{
			if (_descText == null)
			{
				return;
			}

			string text = $"{baked.row.Desc} ({current}/{required})";
			if (_lastDesc == text)
			{
				return;
			}

			_lastDesc = text;
			_descText.text = text;
		}

		private void applyProgress(int current, int required)
		{
			if (_progress == null)
			{
				return;
			}

			float ratio = (required > 0) ? Mathf.Clamp01((float)current / required) : 0f;
			if (Mathf.Approximately(_lastRatio, ratio) == true)
			{
				return;
			}

			_lastRatio = ratio;
			_progress.value = ratio;
		}

		private void applyMetState(bool met)
		{
			int metFlag = met ? 1 : 0;
			if (_lastMet == metFlag)
			{
				return;
			}

			_lastMet = metFlag;

			if (_completeText != null)
			{
				_completeText.gameObject.SetActive(met);
			}

			if (_border != null)
			{
				_border.color = met ? MetColor : BorderNormalColor;
			}

			if (_questInfoBorder != null)
			{
				_questInfoBorder.color = met ? MetColor : QuestInfoBorderNormalColor;
			}
		}

		// CompleteType=UI 인 퀘스트만 눌러서 수령한다. Auto 는 QuestTracker 가 이미 처리했다.
		private void onFrameClicked()
		{
			QuestBook book = Account.Instance.Quests;
			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();
			if (baked == null || baked.row.CompleteType != QuestCompleteType.UI)
			{
				return;
			}

			book.TryComplete(baked.row.ID);
		}

		// 퀘스트 목록 팝업을 연다. Frame 은 수령 전용이라 열기는 이 버튼이 맡는다.
		private void onQuestInfoClicked()
		{
			UIManager.Instance.ShowQuestListPopupAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		// ── 접기 / 펼치기 ─────────────────────────────────────────────

		private void onFoldClicked()
		{
			if (_isSliding) { return; }

			applyState(!_isExpanded, true);
		}

		// 펼침/접힘 상태를 적용한다. animate 가 false 면 즉시 반영.
		private void applyState(bool expanded, bool animate)
		{
			_isExpanded = expanded;

			if (_closeImage != null)
			{
				_closeImage.SetActive(expanded);
			}

			if (_openIcon != null)
			{
				_openIcon.gameObject.SetActive(!expanded);
			}

			if (_frame == null) { return; }

			float targetX = expanded ? _expandedX : _foldedX;
			_frame.DOKill();

			if (animate == false)
			{
				_frame.anchoredPosition = new Vector2(targetX, _frame.anchoredPosition.y);
				return;
			}

			_isSliding = true;
			if (_foldButton != null)
			{
				_foldButton.interactable = false;
			}

			_frame.DOAnchorPosX(targetX, SlideDuration).SetEase(Ease.OutCubic).OnComplete(onSlideComplete);
		}

		private void onSlideComplete()
		{
			_isSliding = false;

			if (_foldButton != null)
			{
				_foldButton.interactable = true;
			}
		}
	}
}
