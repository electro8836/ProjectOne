using System;
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
	// 캐릭터 디테일 팝업 View(MVP). UIManager.ShowCharacterDetailPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 레벨업/특성 결정은 CharacterDetailPresenter 가 한다.
	// (클래스명을 CharacterDetailPopup 으로 유지 — 프리펩이 이 스크립트 GUID 를 참조.)
	public class CharacterDetailPopup : UIScreen, IView
	{
		[Header("등급 색상 대상")]
		[SerializeField] private Image _background;		// Background
		[SerializeField] private Image _topGradient;	// Background/TopGradient
		[SerializeField] private Image _topGlow;		// Background/TopGlow

		[Header("헤더")]
		[SerializeField] private Image _classIcon;		// ClassType/Icon
		[SerializeField] private TMP_Text _nameText;	// TopTitle/NameText
		[SerializeField] private TMP_Text _gradeText;	// TopTitle/GradeText
		[SerializeField] private Image[] _stars;		// Grade_Stars/Star1~5
		[SerializeField] private Image _characterImage;	// Image_Character

		[Header("레벨")]
		[SerializeField] private Slider _expSlider;		// Level (Slider)
		[SerializeField] private TMP_Text _levelText;	// Level/Level/LevelText
		[SerializeField] private TMP_Text _expText;		// Level/ExpText

		[Header("스탯 (Group_Stat_01~04)")]
		[SerializeField] private CharacterStatSlot[] _statSlots;

		[Header("특성")]
		[SerializeField] private Transform _skillGrid;				// Skill_ScrollRect/Viewport/Content/SkillGrid
		[SerializeField] private CharacterSkillSlot _skillSlotPrefab;	// Prefab_CharacterSkillSlot

		[Header("레벨업 비용 (Req_01~04)")]
		[SerializeField] private LevelupCostSlot[] _costSlots;

		[Header("버튼")]
		[SerializeField] private UIButton _levelupButton;	// Levelup/Button_Levelup
		[SerializeField] private UIButton _returnButton;	// Button_Return
		[SerializeField] private UIButton _selectButton;	// Button_Select

		[Header("데이터")]
		[SerializeField] private ClassIconTable _classIcons;
		[SerializeField] private CharacterGradeColorTable _gradeColors;

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnLevelupClicked;
		public event Action OnReturnClicked;
		public event Action OnSelectClicked;
		public event Action<int> OnTraitSlotClicked;

		private readonly CharacterDetailPresenter _presenter = new CharacterDetailPresenter();
		private UniTaskCompletionSource<bool> _tcs;

		// 첫 렌더(모든 아이콘 로드)가 끝날 때까지 숨겼다가 한 번에 보여주기 위한 그룹
		private CanvasGroup _canvasGroup;

		// 캐릭터 대형 아이콘 주소 캐시 (변경 시에만 Release/Acquire — refcount 누수 방지)
		private readonly string[] _charIconAddr = new string[1];

		// 특성 슬롯은 매 렌더마다 재생성 — 이전 것 정리용
		private readonly List<CharacterSkillSlot> _traitSlots = new List<CharacterSkillSlot>();

		private void Awake()
		{
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			// 로드 완료 전까지 숨김 — Reveal 에서 보여준다.
			setVisible(false);

			_levelupButton.OnClickEvent += onLevelupClicked;
			_returnButton.OnClickEvent += onReturnClicked;
			_selectButton.OnClickEvent += onSelectClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_levelupButton.OnClickEvent -= onLevelupClicked;
			_returnButton.OnClickEvent -= onReturnClicked;
			_selectButton.OnClickEvent -= onSelectClicked;

			releaseTrackedIcons();
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(int characterId, CancellationToken ct)
		{
			return _presenter.ShowAsync(characterId, ct);
		}

		// Presenter 가 첫 렌더(모든 아이콘 로드)를 끝낸 뒤 호출 — 채워진 상태로 한 번에 표시.
		public void Reveal()
		{
			setVisible(true);
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// 등급 색상 + 클래스 아이콘 + 이름/등급/별.
		public void SetHeader(Table_Character.Row row)
		{
			CharacterGradeColorTable.GradeColor gc = _gradeColors.Get(row.Grade);
			_background.color = gc.bgMask;
			_topGradient.color = gc.gradient;
			_topGlow.color = gc.glow;

			_classIcon.sprite = _classIcons.Get(row.CharacterType);
			_nameText.text = row.Name;
			_gradeText.text = row.Grade.ToString();

			applyStars(row.Grade);
		}

		public async UniTask SetCharacterIconAsync(string address, CancellationToken ct)
		{
			await loadIconAsync(_characterImage, _charIconAddr, 0, address, ct);
		}

		// 레벨/경험치 — 슬라이더는 현재/필요 비율(수동 레벨업이라 현재가 필요를 초과할 수 있어 1로 클램프).
		public void SetLevelExp(int level, int curExp, int reqExp)
		{
			_levelText.text = level.ToString();

			if (reqExp <= 0)
			{
				_expText.text = "MAX";
				_expSlider.value = 1f;
				return;
			}

			_expText.text = curExp.ToString() + "/" + reqExp.ToString();
			_expSlider.value = Mathf.Clamp01((float)curExp / reqExp);
		}

		// 스탯 — 데이터 개수만큼 슬롯 활성, 나머지 비활성. 각 슬롯 아이콘 로드를 병렬로 진행한다.
		public UniTask BindStatsAsync(IReadOnlyList<StatRowData> stats, CancellationToken ct)
		{
			List<UniTask> tasks = new List<UniTask>();
			for (int i = 0; i < _statSlots.Length; i++)
			{
				bool active = i < stats.Count;
				_statSlots[i].gameObject.SetActive(active);
				if (active == false)
				{
					continue;
				}

				StatRowData data = stats[i];
				tasks.Add(_statSlots[i].Bind(data.iconAddress, data.name, data.value, ct));
			}

			return UniTask.WhenAll(tasks);
		}

		// 특성 — 매 렌더마다 슬롯을 새로 만든다(개수가 적어 풀링 불필요). 아이콘 로드를 병렬로 진행한다.
		public UniTask BindTraitsAsync(IReadOnlyList<TraitSlotData> traits)
		{
			for (int i = 0; i < _traitSlots.Count; i++)
			{
				_traitSlots[i].OnClicked -= onTraitSlotClicked;
				Destroy(_traitSlots[i].gameObject);
			}

			_traitSlots.Clear();

			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			List<UniTask> tasks = new List<UniTask>();
			for (int i = 0; i < traits.Count; i++)
			{
				TraitSlotData data = traits[i];
				CharacterSkillSlot slot = Instantiate(_skillSlotPrefab, _skillGrid);
				slot.OnClicked += onTraitSlotClicked;
				_traitSlots.Add(slot);
				tasks.Add(slot.Bind(data.traitGroupId, data.trait, data.charLevel, data.skillId, ct));
			}

			return UniTask.WhenAll(tasks);
		}

		// 레벨업 비용 — 데이터 개수만큼 슬롯 활성, 나머지 비활성. 각 슬롯 아이콘 로드를 병렬로 진행한다.
		public UniTask BindCostAsync(IReadOnlyList<LevelupCostData> costs, CancellationToken ct)
		{
			List<UniTask> tasks = new List<UniTask>();
			for (int i = 0; i < _costSlots.Length; i++)
			{
				bool active = i < costs.Count;
				_costSlots[i].gameObject.SetActive(active);
				if (active == false)
				{
					continue;
				}

				LevelupCostData data = costs[i];
				tasks.Add(_costSlots[i].Bind(data.iconAddress, data.text, ct));
			}

			return UniTask.WhenAll(tasks);
		}

		public void SetLevelupInteractable(bool interactable)
		{
			_levelupButton.interactable = interactable;
		}

		// 선택 버튼 표시 — 이미 선택된 캐릭터면 숨긴다.
		public void SetSelectVisible(bool visible)
		{
			_selectButton.gameObject.SetActive(visible);
		}

		// 닫힘 대기 — Presenter 의 ShowAsync 가 마지막에 await 한다.
		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 입력(Return)으로 닫힘을 확정한다.
		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 등급만큼 별을 활성화한다.
		private void applyStars(CharacterGrade grade)
		{
			int count = (int)grade;
			for (int i = 0; i < _stars.Length; i++)
			{
				_stars[i].gameObject.SetActive(i < count);
			}
		}

		private void onLevelupClicked()
		{
			if (OnLevelupClicked != null) { OnLevelupClicked.Invoke(); }
		}

		private void onReturnClicked()
		{
			if (OnReturnClicked != null) { OnReturnClicked.Invoke(); }
		}

		private void onSelectClicked()
		{
			if (OnSelectClicked != null) { OnSelectClicked.Invoke(); }
		}

		private void onTraitSlotClicked(int traitGroupId)
		{
			if (OnTraitSlotClicked != null) { OnTraitSlotClicked.Invoke(traitGroupId); }
		}

		// 고정 노드 아이콘 로드 — 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		private async UniTask loadIconAsync(Image target, string[] cache, int index, string address, CancellationToken ct)
		{
			if (cache[index] == address)
			{
				return;
			}

			releaseTrackedIcon(cache, index);
			cache[index] = address;

			if (string.IsNullOrEmpty(address))
			{
				target.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 캐시 슬롯을 비운다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				target.sprite = atlasSprite;
				cache[index] = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 요청됐으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (cache[index] != address)
			{
				return;
			}

			if (icon != null)
			{
				target.sprite = icon;
			}
		}

		private void releaseTrackedIcon(string[] cache, int index)
		{
			if (!string.IsNullOrEmpty(cache[index]) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(cache[index]);
				cache[index] = null;
			}
		}

		private void releaseTrackedIcons()
		{
			if (!ResourceManager.HasInstance)
			{
				return;
			}

			releaseTrackedIcon(_charIconAddr, 0);
		}
	}
}
