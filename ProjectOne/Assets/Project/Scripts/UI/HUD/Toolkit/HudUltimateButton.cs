using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Settings;
using ProjectOne.Skill;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 메인 HUD 궁극기 칸 — 마스터리 궁극기 칸에 낀 스킬을 수동으로 시전하고, 자동 사용 상태를 보여 준다.
	//
	// 궁극기가 없으면(미해금·칸 비움·무기 미착용) 칸째 숨긴다.
	// 보유 스킬은 무기·슬롯이 바뀔 때 다시 등록되는데 알림이 없어 0.1초마다 폴링한다.
	// 남은 시간 글자는 정수 초가 바뀔 때만 쓰고, 부채꼴 딤은 쿨타임 중에만 매 프레임 다시 그린다.
	// AUTO 칩은 표시 전용이다 — 켜고 끄기는 설정(지금은 칸 위 임시 토글)에서 한다.
	public sealed class HudUltimateButton
	{
		private const float RefreshInterval = 0.1f;
		// 딤 각도가 이만큼(비율) 바뀌어야 다시 그린다 — 약 0.4도.
		private const float RepaintStep = 0.001f;
		private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.78f);

		private readonly VisualElement _slot;
		private readonly Button _button;
		private readonly VisualElement _icon;
		private readonly VisualElement _cooltime;
		private readonly Label _time;
		private readonly Label _auto;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ct;

		private UnitBase _hero;
		private IntervalTimer _timer;

		// 지금 표시 중인 궁극기. None 이면 칸이 숨어 있다.
		private EDT.Skill _skill = EDT.Skill.None;
		private bool _visible;
		// 표시 중인 남은 시간(정수 초). 0 이면 쿨타임 표시가 꺼져 있다.
		private int _shownSeconds = -1;
		// 딤이 덮는 남은 비율(0~1). 0 이면 그리지 않는다.
		private float _remainRatio;

		public HudUltimateButton(VisualElement root, ToolkitIconLoader icons, CancellationToken ct)
		{
			_slot = root.Q("ultimate-slot");
			_button = root.Q<Button>("ultimate");
			_icon = root.Q("ultimate-icon");
			_cooltime = root.Q("ultimate-cooltime");
			_time = root.Q<Label>("ultimate-time");
			_auto = root.Q<Label>("ultimate-auto");
			_icons = icons;
			_ct = ct;

			_button.clicked += onClicked;
			root.Q<Button>("ultimate-auto-toggle").clicked += onAutoToggleClicked;
			_cooltime.generateVisualContent += drawCooltime;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);
			SettingsManager.Instance.UltimateAutoChanged += onUltimateAutoChanged;

			setCooltime(0);
			onUltimateAutoChanged(SettingsManager.Instance.UseUltimateAuto);
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			SettingsManager.Instance.UltimateAutoChanged -= onUltimateAutoChanged;
			_hero = null;
		}

		public void Tick(float deltaTime)
		{
			if (_timer.Tick(deltaTime, RefreshInterval) > 0)
			{
				refresh();
			}

			if (_visible == true)
			{
				updateDim();
			}
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

		// 남은 비율이 눈에 띄게 바뀌었을 때만 다시 그린다 — 쿨타임이 아니면 0 에 머물러 아무것도 하지 않는다.
		private void updateDim()
		{
			float ratio = 0f;
			SkillRuntime runtime = (_hero != null && _hero.SkillContainer != null) ? _hero.SkillContainer.GetRuntime(_skill) : null;
			if (runtime != null && runtime.Cooldown > 0f)
			{
				ratio = Mathf.Clamp01(runtime.RemainingCooldown / runtime.Cooldown);
			}

			if (Mathf.Abs(ratio - _remainRatio) < RepaintStep && (ratio > 0f) == (_remainRatio > 0f))
			{
				return;
			}

			_remainRatio = ratio;
			_cooltime.MarkDirtyRepaint();
		}

		// 남은 비율만큼 부채꼴로 덮는다 — 12시에서 시작해 시계방향으로 밝아진 부분이 늘어난다.
		private void drawCooltime(MeshGenerationContext context)
		{
			if (_remainRatio <= 0f)
			{
				return;
			}

			Rect rect = _cooltime.contentRect;
			Vector2 center = rect.center;
			// 안쪽 원보다 살짝 크게 — 가장자리 안티앨리어싱 틈을 부모 클리핑이 가린다.
			float radius = Mathf.Max(rect.width, rect.height) * 0.5f + 2f;
			float start = -90f + 360f * (1f - _remainRatio);

			Painter2D painter = context.painter2D;
			painter.fillColor = DimColor;
			painter.BeginPath();
			painter.MoveTo(center);
			painter.Arc(center, radius, Angle.Degrees(start), Angle.Degrees(270f));
			painter.ClosePath();
			painter.Fill();
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
			_slot.EnableInClassList("ultimate-slot--shown", visible);
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

		// 설정 화면 등 어디서 바꾸든 칩이 따라온다.
		private void onUltimateAutoChanged(bool enabled)
		{
			_auto.EnableInClassList("ultimate-slot__auto--on", enabled);
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

		// 임시 — 설정 화면이 생기면 그쪽으로 옮긴다.
		private void onAutoToggleClicked()
		{
			SettingsManager.Instance.SetUseUltimateAuto(SettingsManager.Instance.UseUltimateAuto == false);
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
