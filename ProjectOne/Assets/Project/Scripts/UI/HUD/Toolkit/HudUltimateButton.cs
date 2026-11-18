using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Skill;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 메인 HUD 궁극기 버튼 — 마스터리 궁극기 칸에 낀 스킬을 수동으로 시전한다.
	//
	// 궁극기가 없으면(미해금·칸 비움·무기 미착용) 버튼을 숨긴다.
	// 보유 스킬은 무기·슬롯이 바뀔 때 다시 등록되는데 알림이 없어 0.1초마다 폴링한다.
	// 남은 시간 글자는 정수 초가 바뀔 때만 쓴다.
	public sealed class HudUltimateButton
	{
		private const float RefreshInterval = 0.1f;

		private readonly Button _button;
		private readonly VisualElement _icon;
		private readonly Label _time;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ct;

		private UnitBase _hero;
		private IntervalTimer _timer;

		// 지금 표시 중인 궁극기. None 이면 버튼이 숨어 있다.
		private EDT.Skill _skill = EDT.Skill.None;
		private bool _visible;
		// 표시 중인 남은 시간(정수 초). 0 이면 쿨타임 표시가 꺼져 있다.
		private int _shownSeconds = -1;

		public HudUltimateButton(VisualElement root, ToolkitIconLoader icons, CancellationToken ct)
		{
			_button = root.Q<Button>("ultimate");
			_icon = root.Q("ultimate-icon");
			_time = root.Q<Label>("ultimate-time");
			_icons = icons;
			_ct = ct;

			_button.clicked += onClicked;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);

			setCooltime(0);
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			_hero = null;
		}

		public void Tick(float deltaTime)
		{
			if (_timer.Tick(deltaTime, RefreshInterval) <= 0)
			{
				return;
			}

			refresh();
		}

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
					_icons.SetAsync(_icon, (row != null) ? row.Icon : string.Empty, _ct).Forget();
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
			_button.EnableInClassList("ultimate--shown", visible);
		}

		private void setCooltime(int seconds)
		{
			if (_shownSeconds == seconds)
			{
				return;
			}

			_shownSeconds = seconds;
			bool cooling = seconds > 0;
			_button.EnableInClassList("ultimate--cooling", cooling);
			if (cooling == true)
			{
				_time.text = seconds.ToString();
			}
		}

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

		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero)
			{
				return;
			}

			_hero = e.Unit;
		}
	}
}
