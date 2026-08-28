using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Items;
using ProjectOne.Mastery;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 무기 마스터리를 Hero 에 적용/제거하는 Aspect (마스터리 설계 8.3 의 [2][3] 계층).
	//
	// 세 가지를 공급한다.
	//   1. LevelBonusOption   전 마스터리 합산. **무기와 무관하게 항상 적용된다** (설계 4.4)
	//   2. 스킬 트리 Stat 옵션 현재 마스터리의 투자 노드만. 무기를 바꾸면 통째로 교체된다
	//   3. 보유 스킬          NormalAttackSkill + 트리에서 SkillGrant 로 습득한 것
	//
	// 무기를 착용하지 않으면 2·3 이 전부 사라지고 1만 남는다 (설계 4.3).
	// Modifier 타입 옵션은 스탯 식에 들어가지 않고 SkillResolver 가 가져간다.
	public sealed class MasteryAspect : IHeroAspect
	{
		const string Source = "Mastery";

		public HeroAspectStage Stage => HeroAspectStage.Skill;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null || hero.Stats == null)
			{
				return;
			}

			MasteryBook book = Account.Instance.Mastery;

			applyLevelBonuses(hero, book);

			Table_WeaponMastery.Row mastery = book.CurrentMastery;

			// 애니메이터 오버라이드는 조기 반환 앞에서 끝낸다 — 무기를 벗은 경우도 되돌려야 한다.
			applyAnimController(hero, mastery);

			if (mastery == null)
			{
				// 무기 미착용 — 기본공격도 트리도 없다. 공격 자체가 불가능하다 (설계 4.3).
				return;
			}

			registerSkill(hero, mastery.NormalAttackSkill);
			registerDevSkills(hero, mastery);
			applyTree(hero, book, mastery);
		}

		public void RemoveFrom(Hero hero)
		{
			if (hero == null)
			{
				return;
			}

			// 스탯과 스킬을 함께 지운다.
			// SkillContainer.Unregister 는 Passive 가 남긴 StatChange 를 되돌리지 않으므로,
			// 스탯 쪽을 따로 지우지 않으면 무기를 바꿔도 이전 무기의 패시브 스탯이 남는다.
			if (hero.Stats != null)
			{
				hero.Stats.RemoveAllFromSource(Source);
			}

			if (hero.SkillContainer != null)
			{
				hero.SkillContainer.RemoveAllFromSource(Source);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 무기별 애니메이터 오버라이드 교체.
		//
		// AC_Root 의 ATTACK/SKILL 은 빈 클립(EMPTY_*)이며, 실제 모션은 AOC 가 갈아끼운다.
		// 이 교체가 없으면 트리거는 들어가지만 화면에는 아무것도 나오지 않는다.
		//
		// 되돌리기를 RemoveFrom 이 아니라 여기서 하는 이유 — Reapply 는 RemoveFrom → ApplyTo 를
		// 연달아 부르므로, 양쪽에서 건드리면 한 사이클에 컨트롤러가 두 번 바뀌고 트리거 리셋도 중복된다.
		private void applyAnimController(Hero hero, Table_WeaponMastery.Row mastery)
		{
			UnitAnimator animator = hero.GetComponent<UnitAnimator>();
			if (animator == null)
			{
				return;
			}

			if (mastery == null)
			{
				animator.RestoreBaseController();
				return;
			}

			RuntimeAnimatorController controller = MasteryCatalog.GetAnimController(mastery.AnimControllerName);
			if (controller == null)
			{
				// 이름이 비었거나 프리로드 누락 — GetAnimController 가 이미 로그를 남겼다.
				animator.RestoreBaseController();
				return;
			}

			animator.SetController(controller);
		}

		// 전 마스터리의 레벨 보너스를 합산한다. 양손검을 키우면 석궁을 들어도 그 공격력이 유지된다.
		private void applyLevelBonuses(Hero hero, MasteryBook book)
		{
			Dictionary<WeaponMastery, Table_WeaponMastery.Row> all = MasteryCatalog.All();
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_WeaponMastery.Row row = e.Current.Value;
				if (row.ID == WeaponMastery.None || row.LevelBonusOption == Option.None)
				{
					continue;
				}

				// 한 번도 들지 않은 무기는 항목이 없다 — 조회 때문에 항목이 생기지 않도록 Find 를 쓴다.
				MasteryProgress progress = book.Find(row.ID);
				if (progress == null)
				{
					continue;
				}

				float value = row.LevelBonusPerLevel * progress.Level;
				if (value == 0f)
				{
					continue;
				}

				applyStatOption(hero, row.LevelBonusOption, value, true);
			}
		}

		// 현재 마스터리의 투자 노드. 다른 마스터리의 트리는 참여하지 않는다 (설계 8.3 [3]).
		private void applyTree(Hero hero, MasteryBook book, Table_WeaponMastery.Row mastery)
		{
			MasteryProgress progress = book.Find(mastery.ID);
			if (progress == null)
			{
				return;
			}

			IReadOnlyList<Table_SkillTreeNode.Row> nodes = MasteryCatalog.GetNodes(mastery.SkillTreeNodeGroupID);
			for (int i = 0; i < nodes.Count; i++)
			{
				Table_SkillTreeNode.Row node = nodes[i];
				int level = progress.GetNodeLevel(node.ID);
				if (level <= 0)
				{
					continue;
				}

				applyNode(hero, node, MasteryProgress.GetNodeValue(node, level));
			}
		}

		private void applyNode(Hero hero, Table_SkillTreeNode.Row node, float value)
		{
			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(node.Option, out entry) == false)
			{
				Debug.LogWarning($"[MasteryAspect] 정의되지 않은 옵션 — node:{node.ID} option:{node.Option}");
				return;
			}

			switch (entry.type)
			{
				case OptionTypes.Stat:
					hero.Stats.AddModifier(entry.statDetail, value, Source);
					break;

				case OptionTypes.SkillGrant:
					registerSkill(hero, entry.skill);
					break;

				case OptionTypes.SkillLevel:
				case OptionTypes.Modifier:
					// 스킬 레벨·변형은 SkillResolver 가 가져간다 — 스탯 식에 들어가지 않는다.
					break;
			}
		}

		// LevelBonusOption 은 Stat 타입만 허용한다 (설계 9장 No.8).
		// 모디파이어를 붙이면 들고 있지도 않은 무기의 스킬이 변형된다.
		private void applyStatOption(Hero hero, Option option, float value, bool statOnly)
		{
			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(option, out entry) == false)
			{
				Debug.LogWarning($"[MasteryAspect] 정의되지 않은 옵션: {option}");
				return;
			}

			if (entry.type != OptionTypes.Stat)
			{
				if (statOnly == true)
				{
					Debug.LogError($"[MasteryAspect] 레벨 보너스는 Stat 옵션만 가능합니다: {option} (type={entry.type})");
				}

				return;
			}

			hero.Stats.AddModifier(entry.statDetail, value, Source);
		}

		// ── 개발용 (제거 예정) ────────────────────────────────────────
		//
		// 스킬 트리로 습득하기 전에 스킬을 굴려 보기 위한 임시 통로다.
		// Table_WeaponMastery.Dev_SkillIDs 를 그대로 보유 스킬로 등록한다.
		//
		// 걷어낼 때 지울 것 — 이 메서드, ApplyTo 의 호출 한 줄, Mastery.xlsx 의 Dev_SkillIDs 컬럼.
		// 그 전까지는 테이블 칸을 비우는 것만으로 무력화된다.
		// 등록 출처가 정규 경로와 같은 Source 라 RemoveFrom 이 함께 정리한다.
		private void registerDevSkills(Hero hero, Table_WeaponMastery.Row mastery)
		{
			if (mastery.Dev_SkillIDs == null)
			{
				return;
			}

			for (int i = 0; i < mastery.Dev_SkillIDs.Length; i++)
			{
				registerSkill(hero, mastery.Dev_SkillIDs[i]);
			}
		}

		// ──────────────────────────────────────────────────────────────

		private void registerSkill(Hero hero, EDT.Skill skill)
		{
			if (skill == EDT.Skill.None || hero.SkillContainer == null)
			{
				return;
			}

			hero.SkillContainer.Register(skill, Source);
		}
	}
}
