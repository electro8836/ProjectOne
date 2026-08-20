using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.Reward;
using ProjectOne.Skill;
using ProjectOne.Unit;
using ProjectOne.UserData;

namespace ProjectOne.Consumables
{
	public enum ConsumableUseResult
	{
		Success = 0,
		NoData,			// Consumable 행이 없거나 파라미터 해석 실패
		NotOwned,		// 인벤토리에 없다
		OnCooldown,
		NoTarget,		// 시전할 히어로가 없다 (마을·로딩 중)
		Failed			// 효과 자체가 실패 (스킬포인트 상한 등)
	}

	// 소모품 사용의 단일 진입점. UI 버튼은 Use() 하나만 부르면 된다.
	//
	// 순서가 중요하다 — 효과를 먼저 굴리고 성공했을 때만 차감한다.
	// 지식의 서가 상한에 막혔는데 아이템이 사라지면 복구할 방법이 없다.
	public static class ConsumableUser
	{
		// Reward 지급 결과 버퍼 — 사용은 메인 스레드 단일 경로라 재사용해도 안전하다.
		private static readonly List<GrantedReward> _granted = new List<GrantedReward>(16);

		// pointTarget 은 ConsumeEffect.SkillPoint 전용이다. 대상 마스터리는 테이블이 아니라
		// 열려 있는 스킬 트리 화면이 정한다 (설계 5.3). None 이면 현재 장착 무기로 폴백한다.
		public static ConsumableUseResult Use(int itemId, WeaponMastery pointTarget = WeaponMastery.None)
		{
			ConsumableCatalog.BakedConsumable baked = ConsumableCatalog.Get(itemId);
			if (baked == null || baked.isValid == false)
			{
				Debug.LogError($"[ConsumableUser] 사용할 수 없는 소모품입니다 — Item:{itemId}");
				return finish(itemId, ConsumableUseResult.NoData);
			}

			if (Account.Instance.Inventory.GetCount(itemId) <= 0)
			{
				return finish(itemId, ConsumableUseResult.NotOwned);
			}

			if (ConsumableCooldown.IsReady(itemId, baked.cooldownGroup) == false)
			{
				return finish(itemId, ConsumableUseResult.OnCooldown);
			}

			ConsumableUseResult result = apply(baked, pointTarget);
			if (result != ConsumableUseResult.Success)
			{
				// 효과가 실패하면 차감도 쿨다운도 없다.
				return finish(itemId, result);
			}

			Account.Instance.Inventory.TrySpend(itemId, 1);
			ConsumableCooldown.Begin(itemId, baked.cooldownGroup, baked.cooldown);
			return finish(itemId, ConsumableUseResult.Success);
		}

		// ── 타입별 실행 ───────────────────────────────────────────────

		private static ConsumableUseResult apply(ConsumableCatalog.BakedConsumable baked, WeaponMastery pointTarget)
		{
			switch (baked.effect)
			{
				case ConsumeEffect.Skill:
					return applySkill(baked);

				case ConsumeEffect.Reward:
					return applyReward(baked);

				case ConsumeEffect.SkillPoint:
					return applySkillPoint(baked, pointTarget);
			}

			return ConsumableUseResult.NoData;
		}

		// 시전자는 사용자 자신이다. 대상 탐색·효과 적용은 전부 스킬 시스템이 처리한다 (설계 5.1).
		//
		// SkillContainer 에 등록하지 않는다 — 소모품 스킬은 보유 스킬이 아니고 쿨다운은 아이템이 갖는다.
		// 공속 사이클도 타지 않으므로 useSpeed 는 1 고정이다.
		private static ConsumableUseResult applySkill(ConsumableCatalog.BakedConsumable baked)
		{
			UnitBase hero = findHero();
			if (hero == null)
			{
				return ConsumableUseResult.NoTarget;
			}

			SkillExecutor.Execute(baked.skillId, hero, 1f);
			return ConsumableUseResult.Success;
		}

		// 보상 그룹의 규칙(확률·수량·등급 추첨)은 전부 보상 시스템이 소유한다 (설계 5.2).
		// Grant 가 버퍼를 비우지 않고 누적하므로 반복 횟수가 자연히 성립한다 (STEP 10 규약).
		private static ConsumableUseResult applyReward(ConsumableCatalog.BakedConsumable baked)
		{
			_granted.Clear();

			for (int i = 0; i < baked.repeatCount; i++)
			{
				RewardGranter.Grant(baked.rewardGroupId, RewardContext.ConsumableUse, _granted);
			}

			return ConsumableUseResult.Success;
		}

		// 상한(SkillPoint_Item.MaxPoint)은 런타임 카운터가 막는다 (설계 5.3).
		private static ConsumableUseResult applySkillPoint(ConsumableCatalog.BakedConsumable baked, WeaponMastery pointTarget)
		{
			MasteryBook book = Account.Instance.Mastery;
			if (book == null)
			{
				return ConsumableUseResult.Failed;
			}

			WeaponMastery target = pointTarget;
			if (target == WeaponMastery.None)
			{
				Table_WeaponMastery.Row current = book.CurrentMastery;
				if (current == null)
				{
					// 무기를 안 들었으면 포인트를 줄 트리가 없다.
					return ConsumableUseResult.NoTarget;
				}

				target = current.ID;
			}

			if (book.TryUseItemPoint(target, baked.pointAmount) == false)
			{
				return ConsumableUseResult.Failed;
			}

			return ConsumableUseResult.Success;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static UnitBase findHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				if (heroes[i] != null && heroes[i].IsDead == false)
				{
					return heroes[i];
				}
			}

			return null;
		}

		private static ConsumableUseResult finish(int itemId, ConsumableUseResult result)
		{
			EventManager.Instance.Publish(new ConsumableUsedEvent(itemId, result));
			return result;
		}
	}
}
