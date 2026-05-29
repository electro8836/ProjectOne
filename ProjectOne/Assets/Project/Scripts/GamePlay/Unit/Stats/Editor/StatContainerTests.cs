using UnityEngine;
using UnityEditor;
using EDT;

namespace ProjectOne.Unit.Stats.EditorTests
{
	// StatContainer / Vitals 동작 검증
	// 메뉴: Tools > ProjectOne > Run Stat Tests
	// 결과는 콘솔에 Debug.Log/LogError로 출력
	public static class StatContainerTests
	{
		static int _passed;
		static int _failed;

		[MenuItem("Tools/ProjectOne/Run Stat Tests")]
		public static void RunAll()
		{
			_passed = 0;
			_failed = 0;

			Test_SetBase();
			Test_AddModifier_Add();
			Test_AddModifier_Ratio();
			Test_AddModifier_Amp();
			Test_RemoveModifier_RevertsValue();
			Test_PartialQueries();
			Test_AddBase_Accumulates();
			Test_AddModifier_RejectsBaseAndFinal();
			Test_DirtyOnlyOnTouchedGroup();
			Test_UnsetGroup_ReturnsZero();
			Test_Vitals_InitAndClamp();
			Test_Vitals_ZeroDetection();
			Test_Vitals_RespectsUpdatedMax();

			string summary = $"[StatContainerTests] {_passed} passed, {_failed} failed";
			if (_failed == 0)
			{
				Debug.Log(summary);
			}
			else
			{
				Debug.LogError(summary);
			}
		}

		// ===== StatContainer =====

		static void Test_SetBase()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			AssertApprox("SetBase → GetStat(ATK)", 100f, c.GetStat(StatTypes.ATK));
			AssertApprox("SetBase → GetStat(ATK_Base)", 100f, c.GetStat(StatTypes.ATK_Base));
		}

		static void Test_AddModifier_Add()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			c.AddModifier(StatTypes.ATK_Add, 20f);
			AssertApprox("Base+Add → Final", 120f, c.GetStat(StatTypes.ATK));
		}

		static void Test_AddModifier_Ratio()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			c.AddModifier(StatTypes.ATK_Add, 20f);
			c.AddModifier(StatTypes.ATK_Ratio, 0.5f);
			AssertApprox("(Base+Add)*(1+Ratio)", 180f, c.GetStat(StatTypes.ATK));
		}

		static void Test_AddModifier_Amp()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			c.AddModifier(StatTypes.ATK_Add, 20f);
			c.AddModifier(StatTypes.ATK_Ratio, 0.5f);
			c.AddModifier(StatTypes.ATK_Amp, 0.1f);
			AssertApprox("(Base+Add)*(1+Ratio)*(1+Amp)", 198f, c.GetStat(StatTypes.ATK));
		}

		static void Test_RemoveModifier_RevertsValue()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			StatModifier addMod = c.AddModifier(StatTypes.ATK_Add, 20f);
			StatModifier ratMod = c.AddModifier(StatTypes.ATK_Ratio, 0.5f);
			AssertApprox("before remove", 180f, c.GetStat(StatTypes.ATK));

			c.RemoveModifier(ratMod);
			AssertApprox("after remove Ratio", 120f, c.GetStat(StatTypes.ATK));

			c.RemoveModifier(addMod);
			AssertApprox("after remove Add", 100f, c.GetStat(StatTypes.ATK));
		}

		static void Test_PartialQueries()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.ATK_Base, 100f);
			c.AddModifier(StatTypes.ATK_Add, 30f);
			c.AddModifier(StatTypes.ATK_Ratio, 0.2f);
			c.AddModifier(StatTypes.ATK_Amp, 0.1f);

			AssertApprox("GetStat(ATK_Base)",  100f, c.GetStat(StatTypes.ATK_Base));
			AssertApprox("GetStat(ATK_Add)",   30f,  c.GetStat(StatTypes.ATK_Add));
			AssertApprox("GetStat(ATK_Ratio)", 0.2f, c.GetStat(StatTypes.ATK_Ratio));
			AssertApprox("GetStat(ATK_Amp)",   0.1f, c.GetStat(StatTypes.ATK_Amp));
		}

		static void Test_AddBase_Accumulates()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.HP_Base, 500f);
			c.AddBase(StatTypes.HP_Base, 100f);
			c.AddBase(StatTypes.HP_Base, 50f);
			AssertApprox("AddBase 누적", 650f, c.GetStat(StatTypes.HP));
		}

		static void Test_AddModifier_RejectsBaseAndFinal()
		{
			var c = new StatContainer();
			bool threw = false;
			try
			{
				c.AddModifier(StatTypes.ATK_Base, 10f);
			}
			catch (System.ArgumentException)
			{
				threw = true;
			}
			AssertTrue("AddModifier(_Base) → ArgumentException", threw);

			threw = false;
			try
			{
				c.AddModifier(StatTypes.ATK, 10f);
			}
			catch (System.ArgumentException)
			{
				threw = true;
			}
			AssertTrue("AddModifier(final) → ArgumentException", threw);
		}

		static void Test_DirtyOnlyOnTouchedGroup()
		{
			// MoveSpeed 그룹을 먼저 세팅·캐싱 → ATK 그룹 변경 후에도 MoveSpeed 캐시 무효화 안 됨
			var c = new StatContainer();
			c.SetBase(StatTypes.MoveSpeed_Base, 5f);
			c.AddModifier(StatTypes.MoveSpeed_Ratio, 0.2f);
			float moveBefore = c.GetStat(StatTypes.MoveSpeed); // 캐시됨

			c.SetBase(StatTypes.ATK_Base, 200f);
			c.AddModifier(StatTypes.ATK_Add, 10f);

			float moveAfter = c.GetStat(StatTypes.MoveSpeed);
			AssertApprox("MoveSpeed 캐시 영향 없음", moveBefore, moveAfter);
			AssertApprox("ATK 그룹은 정상 계산", 210f, c.GetStat(StatTypes.ATK));
		}

		static void Test_UnsetGroup_ReturnsZero()
		{
			var c = new StatContainer();
			// 한 번도 안 건드린 그룹은 0
			AssertApprox("unset Final = 0", 0f, c.GetStat(StatTypes.ATK));
			AssertApprox("unset Base = 0",  0f, c.GetStat(StatTypes.ATK_Base));
		}

		// ===== Vitals =====

		static void Test_Vitals_InitAndClamp()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.HP_Base, 1000f);
			var v = new Vitals(c);
			v.InitHp();
			AssertApprox("InitHp = Max", 1000f, v.Hp);

			v.ModifyHp(-300f);
			AssertApprox("ModifyHp(-300)", 700f, v.Hp);

			v.ModifyHp(+10000f);
			AssertApprox("clamp to Max", 1000f, v.Hp);

			v.ModifyHp(-99999f);
			AssertApprox("clamp to 0", 0f, v.Hp);
		}

		static void Test_Vitals_ZeroDetection()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.BreakGage_Base, 100f);
			var v = new Vitals(c);
			v.InitBreakGage();
			AssertTrue("IsBreakGageZero false (max)", !v.IsBreakGageZero);
			v.ModifyBreakGage(-100f);
			AssertTrue("IsBreakGageZero true (0)", v.IsBreakGageZero);
		}

		static void Test_Vitals_RespectsUpdatedMax()
		{
			var c = new StatContainer();
			c.SetBase(StatTypes.HP_Base, 1000f);
			var v = new Vitals(c);
			v.InitHp();

			// 현재 1000/1000. MaxHP가 +20% 버프
			c.AddModifier(StatTypes.HP_Ratio, 0.2f);
			AssertApprox("새 MaxHP = 1200", 1200f, c.GetStat(StatTypes.HP));

			// 현재값은 그대로
			AssertApprox("현재값 그대로", 1000f, v.Hp);

			// 추가 회복 시 새 Max로 clamp
			v.ModifyHp(+500f);
			AssertApprox("clamp to 새 Max(1200)", 1200f, v.Hp);
		}

		// ===== Helpers =====

		const float EPS = 0.0001f;

		static void AssertApprox(string label, float expected, float actual)
		{
			if (Mathf.Abs(expected - actual) < EPS)
			{
				_passed++;
			}
			else
			{
				_failed++;
				Debug.LogError($"[FAIL] {label}: expected={expected}, actual={actual}");
			}
		}

		static void AssertTrue(string label, bool cond)
		{
			if (cond)
			{
				_passed++;
			}
			else
			{
				_failed++;
				Debug.LogError($"[FAIL] {label}: condition false");
			}
		}
	}
}
