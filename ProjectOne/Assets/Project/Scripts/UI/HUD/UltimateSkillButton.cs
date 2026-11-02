using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.Skill;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 궁극기 버튼 — 마스터리 궁극기 칸에 낀 스킬을 수동으로 시전한다.
	//
	// BuffInfo · HeroInfo 와 같은 결의 자율 컴포넌트다(MVP 를 쓰지 않는다). 자기 이벤트를 직접 구독한다.
	//
	// 궁극기가 없으면(미해금·칸 비움·무기 미착용) 버튼을 숨긴다. GameObject 를 끄면 이 컴포넌트의
	// Update 도 멈춰 다시 켤 수 없으므로 CanvasGroup 으로 숨긴다. MainHud 의 맥락 묶음이 이 오브젝트를
	// 켜고 끄는 것과도 겹치지 않는다.
	//
	// 보유 스킬은 무기·슬롯이 바뀔 때 다시 등록되는데 따로 알림이 없어 주기 폴링으로 읽는다.
	// 화면에 쓰는 것은 값이 바뀌었을 때뿐이다 — 남은 시간 텍스트는 정수 초가 바뀔 때(1초 주기)만 갱신된다.
	[RequireComponent(typeof(CanvasGroup))]
	public class UltimateSkillButton : MonoBehaviour
	{
		private const float RefreshInterval = 0.1f;

		[SerializeField] private UIButton _button;			// SkillButton
		[SerializeField] private Image _icon;				// IconMask/Icon — 원형 마스크 안에 있어 네모 아이콘이 잘린다
		[SerializeField] private GameObject _cooltime;		// Cooltime
		[SerializeField] private TMP_Text _timeText;		// Cooltime/TimeText

		private CanvasGroup _canvasGroup;
		private Action<UnitSpawnedEvent> _onUnitSpawned;

		// 추적 중인 히어로. 씬마다 새로 스폰되고 부활 때도 다시 통지되므로 갈아끼운다.
		private UnitBase _hero;

		private IntervalTimer _timer;

		// 지금 표시 중인 궁극기. None 이면 버튼이 숨어 있다.
		private EDT.Skill _skill = EDT.Skill.None;
		private bool _visible = true;

		// 표시 중인 남은 시간(정수 초). 0 이면 쿨타임 표시가 꺼져 있다.
		private int _shownSeconds = -1;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_canvasGroup = GetComponent<CanvasGroup>();

			_onUnitSpawned = onUnitSpawned;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);

			_button.OnClickEvent += onClicked;

			setVisible(false);
			setCooltime(0);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			_button.OnClickEvent -= onClicked;
			_hero = null;

			releaseIcon();
		}

		private void Update()
		{
			if (_timer.Tick(Time.deltaTime, RefreshInterval) <= 0)
			{
				return;
			}

			refresh();
		}

		// ── 갱신 ──────────────────────────────────────────────────────

		private void refresh()
		{
			SkillContainer container = (_hero != null) ? _hero.SkillContainer : null;
			EDT.Skill skill = findUltimate(container);

			if (skill != _skill)
			{
				_skill = skill;
				_shownSeconds = -1;

				if (skill != EDT.Skill.None)
				{
					Table_Skill.Row row = Table_Skill.Get(skill);
					setIcon((row != null) ? row.Icon : string.Empty, this.GetCancellationTokenOnDestroy()).Forget();
				}
			}

			setVisible(skill != EDT.Skill.None);
			if (skill == EDT.Skill.None)
			{
				return;
			}

			// 올림 — 0.3초 남았을 때 0 으로 보이면 눌러도 안 나가는 것처럼 보인다.
			float remaining = container.GetRemainingCooldown(skill);
			setCooltime(Mathf.CeilToInt(remaining));
		}

		// 보유 스킬 중 궁극기. 궁극기 칸은 하나라 처음 찾은 것이 전부다.
		private static EDT.Skill findUltimate(SkillContainer container)
		{
			if (container == null)
			{
				return EDT.Skill.None;
			}

			IReadOnlyList<EDT.Skill> all = container.GetAll();
			for (int i = 0; i < all.Count; i++)
			{
				Table_Skill.Row row = Table_Skill.Get(all[i]);
				if (row != null && row.SkillCategory == SkillCategoryTypes.Ultimate)
				{
					return all[i];
				}
			}

			return EDT.Skill.None;
		}

		private void setVisible(bool visible)
		{
			if (_visible == visible)
			{
				return;
			}

			_visible = visible;
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void setCooltime(int seconds)
		{
			if (_shownSeconds == seconds)
			{
				return;
			}

			_shownSeconds = seconds;

			bool cooling = seconds > 0;
			_cooltime.SetActive(cooling);
			if (cooling == true)
			{
				_timeText.text = seconds.ToString();
			}
		}

		// ── 입력 ──────────────────────────────────────────────────────

		// 시전 가능 여부(쿨타임·기절 등)는 TryCast 가 판단한다. 실패하면 아무 일도 일어나지 않는다.
		private void onClicked()
		{
			if (_hero == null || _hero.SkillContainer == null || _skill == EDT.Skill.None)
			{
				return;
			}

			if (_hero.SkillContainer.TryCast(_skill) == true)
			{
				refresh();
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero)
			{
				return;
			}

			_hero = e.Unit;
		}

		// ── 아이콘 ────────────────────────────────────────────────────

		// BuffSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_icon.sprite = null;
				_icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
				_icon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}
