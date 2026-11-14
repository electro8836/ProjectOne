using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EDT;
using ProjectOne.Boot;

namespace ProjectOne.Dev.Editor
{
	// DevTester 마스터리 항목 — 스킬 칸 드롭다운에 그 무기·그 칸 분류의 스킬만 띄운다.
	// 패시브 칸은 그 무기의 패시브만, 전술 칸은 액티브만, 코어 칸은 궁극기만 고를 수 있다.
	[CustomPropertyDrawer(typeof(DevTester.DevMastery))]
	public class DevMasteryDrawer : PropertyDrawer
	{
		private static readonly string[] SlotFields = { "passive", "tactical1", "tactical2", "tactical3", "tactical4", "core" };
		private static readonly string[] SlotLabels = { "패시브", "전술 1", "전술 2", "전술 3", "전술 4", "코어" };
		private static readonly SkillCategoryTypes[] SlotCategories =
		{
			SkillCategoryTypes.Passive, SkillCategoryTypes.Active, SkillCategoryTypes.Active,
			SkillCategoryTypes.Active, SkillCategoryTypes.Active, SkillCategoryTypes.Ultimate,
		};

		private const float Gap = 2f;
		private const int RowCount = 10; // 접기 + 무기 + 레벨 + 머리글 + 6칸

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float line = EditorGUIUtility.singleLineHeight + Gap;
			if (property.isExpanded == false)
			{
				return line;
			}

			return line * RowCount;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);
			float line = EditorGUIUtility.singleLineHeight;
			Rect row = new Rect(position.x, position.y, position.width, line);

			property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
			if (property.isExpanded == false)
			{
				EditorGUI.EndProperty();
				return;
			}

			EditorGUI.indentLevel++;
			SerializedProperty masteryProp = property.FindPropertyRelative("mastery");
			SerializedProperty levelProp = property.FindPropertyRelative("level");

			row.y += line + Gap;
			EditorGUI.PropertyField(row, masteryProp);
			row.y += line + Gap;
			EditorGUI.PropertyField(row, levelProp);
			row.y += line + Gap;
			EditorGUI.LabelField(row, "스킬 칸 (전부 None = 자동 장착)", EditorStyles.boldLabel);

			WeaponMastery mastery = (WeaponMastery)masteryProp.intValue;
			int level = levelProp.intValue;
			for (int i = 0; i < SlotFields.Length; i++)
			{
				row.y += line + Gap;
				drawSlot(row, property.FindPropertyRelative(SlotFields[i]), SlotLabels[i], mastery, SlotCategories[i], level);
			}

			EditorGUI.indentLevel--;
			EditorGUI.EndProperty();
		}

		// 칸 하나 — None + 그 무기·분류 스킬(해금 레벨순). 지금 값이 목록 밖이면 표시만 해 두고 고르면 바뀐다.
		private static void drawSlot(Rect rect, SerializedProperty prop, string label, WeaponMastery mastery, SkillCategoryTypes category, int level)
		{
			List<SkillOption> options = DevSkillTable.GetOptions(mastery, category);
			EDT.Skill current = (EDT.Skill)prop.intValue;

			int selected = -1;
			List<string> names = new List<string>();
			names.Add("None");
			if (current == EDT.Skill.None)
			{
				selected = 0;
			}

			for (int i = 0; i < options.Count; i++)
			{
				SkillOption option = options[i];
				string text = option.name + "  (Lv." + option.unlockLevel + ")";
				if (level < option.unlockLevel)
				{
					text += "  — 미해금";
				}

				names.Add(text);
				if (option.skill == current)
				{
					selected = i + 1;
				}
			}

			// 다른 무기·다른 분류 값이 남아 있으면 맨 끝에 표시한다 (적용 시 DevTester 가 빈 칸으로 바꾼다)
			if (selected < 0)
			{
				names.Add("[목록 밖] " + current);
				selected = names.Count - 1;
			}

			int picked = EditorGUI.Popup(rect, label, selected, names.ToArray());
			if (picked == selected)
			{
				return;
			}

			if (picked == 0)
			{
				prop.intValue = (int)EDT.Skill.None;
			}
			else if (picked <= options.Count)
			{
				prop.intValue = (int)options[picked - 1].skill;
			}
		}
	}

	public struct SkillOption
	{
		public EDT.Skill skill;
		public string name;
		public int unlockLevel;
	}

	// 에디터용 스킬 목록 캐시 — .bytes 를 생성된 파서로 읽되, 런타임 정적 테이블은 읽기 전 상태로 되돌린다.
	// (에디터에서 테이블을 채운 채로 두면 이후 Loader.LoadAll 이 중복 키로 실패한다)
	public static class DevSkillTable
	{
		private const string TableDir = "Assets/Project/Data/Tables/";

		private static Dictionary<EDT.Skill, Table_Skill.Row> _skills;
		private static List<Table_MasterySkill.Row> _masterySkills;
		private static readonly Dictionary<long, List<SkillOption>> _options = new Dictionary<long, List<SkillOption>>();
		private static readonly List<SkillOption> _empty = new List<SkillOption>();

		public static List<SkillOption> GetOptions(WeaponMastery mastery, SkillCategoryTypes category)
		{
			if (mastery == WeaponMastery.None)
			{
				return _empty;
			}

			long key = ((long)mastery << 16) | (long)category;
			List<SkillOption> list;
			if (_options.TryGetValue(key, out list) == true)
			{
				return list;
			}

			ensureLoaded();
			list = new List<SkillOption>();
			for (int i = 0; i < _masterySkills.Count; i++)
			{
				Table_MasterySkill.Row row = _masterySkills[i];
				Table_Skill.Row skill;
				if (row.WeaponMastery != mastery || _skills.TryGetValue(row.SkillID, out skill) == false || skill.SkillCategory != category)
				{
					continue;
				}

				SkillOption option = new SkillOption();
				option.skill = row.SkillID;
				option.name = skill.Name;
				option.unlockLevel = row.UnlockLevel;
				list.Add(option);
			}

			list.Sort(compareUnlock);
			_options[key] = list;
			return list;
		}

		private static int compareUnlock(SkillOption a, SkillOption b)
		{
			return a.unlockLevel.CompareTo(b.unlockLevel);
		}

		private static void ensureLoaded()
		{
			if (_skills != null)
			{
				return;
			}

			_skills = new Dictionary<EDT.Skill, Table_Skill.Row>();
			_masterySkills = new List<Table_MasterySkill.Row>();

			// 스킬 테이블 — 기존 내용을 잠시 빼 두고 파싱한 뒤 되돌린다
			Dictionary<EDT.Skill, Table_Skill.Row> skillAll = Table_Skill.All();
			List<KeyValuePair<EDT.Skill, Table_Skill.Row>> skillBackup = new List<KeyValuePair<EDT.Skill, Table_Skill.Row>>(skillAll);
			skillAll.Clear();
			if (parse(Table_Skill.Filename, Table_Skill._parser) == true)
			{
				_skills = new Dictionary<EDT.Skill, Table_Skill.Row>(skillAll);
			}

			skillAll.Clear();
			for (int i = 0; i < skillBackup.Count; i++)
			{
				skillAll.Add(skillBackup[i].Key, skillBackup[i].Value);
			}

			Dictionary<int, Table_MasterySkill.Row> masteryAll = Table_MasterySkill.All();
			List<KeyValuePair<int, Table_MasterySkill.Row>> masteryBackup = new List<KeyValuePair<int, Table_MasterySkill.Row>>(masteryAll);
			masteryAll.Clear();
			if (parse(Table_MasterySkill.Filename, Table_MasterySkill._parser) == true)
			{
				_masterySkills = new List<Table_MasterySkill.Row>(masteryAll.Values);
			}

			masteryAll.Clear();
			for (int i = 0; i < masteryBackup.Count; i++)
			{
				masteryAll.Add(masteryBackup[i].Key, masteryBackup[i].Value);
			}
		}

		private static bool parse(string filename, Loader.Parser parser)
		{
			TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TableDir + filename);
			if (asset == null)
			{
				Debug.LogError("[DevSkillTable] 테이블 파일 없음: " + TableDir + filename);
				return false;
			}

			Loader loader = new Loader();
			bool ok = loader.Load(new BinaryReader(new MemoryStream(asset.bytes)), parser);
			if (ok == false)
			{
				Debug.LogError("[DevSkillTable] 테이블 파싱 실패: " + filename + " / " + loader.Error);
			}

			return ok;
		}

		// 테이블 .bytes 가 다시 들어오면 목록을 새로 만든다
		private class Reimport : AssetPostprocessor
		{
			private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
			{
				for (int i = 0; i < imported.Length; i++)
				{
					if (imported[i].StartsWith(TableDir) == true)
					{
						_skills = null;
						_options.Clear();
						return;
					}
				}
			}
		}
	}
}
