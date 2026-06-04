using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Buff;

namespace ProjectOne.Editor
{
	// 유닛 런타임 상태(스탯/스킬/버프) 인스펙터 표시 — 에디터 전용, 선택한 유닛 1개만.
	// 데이터는 UnitFactory.ComposeUnit 이 런타임에 주입하므로 Play 모드에서만 채워진다.
	[CustomEditor(typeof(UnitBase), editorForChildClasses: true)]
	public class UnitBaseEditor : UnityEditor.Editor
	{
		static bool _statsFoldout = true;
		static bool _skillsFoldout = true;
		static bool _buffsFoldout = true;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			UnitBase unit = target as UnitBase;
			if (unit == null)
			{
				return;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("── 런타임 정보 ──", EditorStyles.boldLabel);

			if (Application.isPlaying == false)
			{
				EditorGUILayout.HelpBox("재생 중에만 런타임 정보가 표시됩니다.", MessageType.Info);
				return;
			}

			DrawStatsSection(unit);
			DrawSkillsSection(unit);
			DrawBuffsSection(unit);
		}

		// Play 중 쿨다운/버프 잔여시간/HP 등이 실시간 갱신되도록 (선택된 인스펙터 1개만 매 프레임 다시 그림)
		public override bool RequiresConstantRepaint()
		{
			return Application.isPlaying;
		}

		void DrawStatsSection(UnitBase unit)
		{
			_statsFoldout = EditorGUILayout.Foldout(_statsFoldout, "스탯", toggleOnLabelClick: true);
			if (_statsFoldout == false)
			{
				return;
			}

			StatContainer stats = unit.Stats;
			Vitals vitals = unit.Vitals;
			if (stats == null)
			{
				EditorGUILayout.LabelField("스탯 없음 (미초기화)");
				return;
			}

			EditorGUI.indentLevel++;

			// 현재 바이탈 — 현재값 / 최댓값
			if (vitals != null)
			{
				EditorGUILayout.LabelField("HP", $"{vitals.Hp:0.##} / {stats.GetStat(StatInfo.MaxHP):0.##}");
				EditorGUILayout.LabelField("Break", $"{vitals.BreakGage:0.##} / {stats.GetStat(StatInfo.BreakGage):0.##}");
			}

			EditorGUILayout.LabelField("IsDead", unit.IsDead.ToString());
			EditorGUILayout.Space();

			// 최종 스탯만 나열 (구성요소 _Base/_Add/_Ratio/_Amp 는 제외)
			Array values = Enum.GetValues(typeof(StatInfo));
			for (int i = 0; i < values.Length; i++)
			{
				StatInfo stat = (StatInfo)values.GetValue(i);
				if (IsFinalStat(stat) == false)
				{
					continue;
				}

				EditorGUILayout.LabelField(stat.ToString(), stats.GetStat(stat).ToString("0.##"));
			}

			EditorGUI.indentLevel--;
		}

		void DrawSkillsSection(UnitBase unit)
		{
			_skillsFoldout = EditorGUILayout.Foldout(_skillsFoldout, "보유 스킬", toggleOnLabelClick: true);
			if (_skillsFoldout == false)
			{
				return;
			}

			SkillContainer skills = unit.SkillContainer;
			if (skills == null)
			{
				EditorGUILayout.LabelField("스킬 컨테이너 없음");
				return;
			}

			EditorGUI.indentLevel++;

			IReadOnlyList<SkillInfo> all = skills.GetAll();
			if (all == null || all.Count == 0)
			{
				EditorGUILayout.LabelField("없음");
			}
			else
			{
				for (int i = 0; i < all.Count; i++)
				{
					SkillInfo id = all[i];
					string state = skills.IsOnCooldown(id) ? $"CD {skills.GetRemainingCooldown(id):0.0}s" : "Ready";
					EditorGUILayout.LabelField(id.ToString(), state);
				}
			}

			EditorGUI.indentLevel--;
		}

		void DrawBuffsSection(UnitBase unit)
		{
			_buffsFoldout = EditorGUILayout.Foldout(_buffsFoldout, "적용 중 버프", toggleOnLabelClick: true);
			if (_buffsFoldout == false)
			{
				return;
			}

			BuffContainer buffs = unit.BuffContainer;
			if (buffs == null)
			{
				EditorGUILayout.LabelField("버프 컨테이너 없음");
				return;
			}

			EditorGUI.indentLevel++;

			IReadOnlyList<BuffInfo> active = buffs.GetActive();
			if (active == null || active.Count == 0)
			{
				EditorGUILayout.LabelField("없음");
			}
			else
			{
				for (int i = 0; i < active.Count; i++)
				{
					BuffInfo id = active[i];
					BuffRuntime rt = buffs.GetRuntime(id);
					if (rt == null)
					{
						EditorGUILayout.LabelField(id.ToString(), "?");
						continue;
					}

					string tag = rt.IsDebuff ? "디버프" : "버프";
					string remain = rt.IsInfinite ? "∞" : $"{rt.RemainingDuration:0.0}s";
					if (rt.IntervalSec > 0f)
					{
						remain += $" (주기 {rt.IntervalSec:0.0}s)";
					}

					EditorGUILayout.LabelField($"[{tag}] {id}", remain);
				}
			}

			EditorGUI.indentLevel--;
		}

		// StatContainer.BuildPartMap 과 동일한 분류: 접미사를 뗀 그룹명이 다른 StatInfo 면 구성요소(part)로 제외.
		static bool IsFinalStat(StatInfo t)
		{
			if (t == StatInfo.None)
			{
				return false;
			}

			string n = t.ToString();
			string group = null;
			if (n.EndsWith("_Base"))
			{
				group = n.Substring(0, n.Length - 5);
			}
			else if (n.EndsWith("_Add"))
			{
				group = n.Substring(0, n.Length - 4);
			}
			else if (n.EndsWith("_Ratio"))
			{
				group = n.Substring(0, n.Length - 6);
			}
			else if (n.EndsWith("_Amp"))
			{
				group = n.Substring(0, n.Length - 4);
			}

			if (group == null)
			{
				return true;
			}

			return Enum.TryParse(group, out StatInfo _) == false;
		}
	}
}
