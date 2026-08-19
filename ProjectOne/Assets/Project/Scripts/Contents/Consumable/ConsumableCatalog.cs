using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;
using UnityEngine;
using ProjectOne.Reward;

namespace ProjectOne.Consumables
{
	// 소모품 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// 설계의 핵심 판단은 "효과 시스템을 새로 만들지 않는다" 이다 (설계 2장).
	// 포션·비약·폭탄이 전부 SkillEffect 로 표현되므로 소모품은 "무엇을 발동하는가"만 갖고,
	// 실제 효과는 스킬 / 보상 / 마스터리 세 시스템에 위임한다.
	//
	// EffectParam_1/2 는 자유 형식 문자열이라 사용할 때마다 파싱하지 않고 Build 시점에 굽는다.
	// RewardCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class ConsumableCatalog
	{
		// 파싱이 끝난 소모품 1개. 타입별로 쓰는 필드가 다르다 — 슬롯의 뜻은 설계 4.2 표가 전부다.
		public sealed class BakedConsumable
		{
			public int itemId;
			public ConsumeEffect effect;

			// ConsumeEffect.Skill
			public EDT.Skill skillId;

			// ConsumeEffect.Reward
			public int rewardGroupId;
			public int repeatCount;			// 비었으면 1

			// ConsumeEffect.SkillPoint
			public EDT.SkillPoint pointSource;
			public int pointAmount;

			public int cooldownGroup;
			public float cooldown;

			// 파싱에 실패한 행은 사용 대상에서 제외한다. 경고는 Build 가 이미 냈다.
			public bool isValid;
		}

		private static readonly Dictionary<int, BakedConsumable> _byItem = new Dictionary<int, BakedConsumable>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byItem.Clear();

			Dictionary<int, Table_Consumable.Row> all = Table_Consumable.All();
			Dictionary<int, Table_Consumable.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedConsumable baked = bake(e.Current.Value);
				if (baked != null)
				{
					_byItem[baked.itemId] = baked;
				}
			}

			_built = true;
			Debug.Log($"[ConsumableCatalog] 구축 완료 — 소모품:{_byItem.Count} / 전체 행 {all.Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static BakedConsumable Get(int itemId)
		{
			if (_built == false)
			{
				Debug.LogError("[ConsumableCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				return null;
			}

			BakedConsumable baked;
			_byItem.TryGetValue(itemId, out baked);
			return baked;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static BakedConsumable bake(Table_Consumable.Row row)
		{
			if (row == null || row.ID <= 0)
			{
				return null;
			}

			BakedConsumable baked = new BakedConsumable();
			baked.itemId = row.ID;
			baked.effect = row.ConsumeEffect;
			baked.cooldownGroup = row.CooldownGroup;
			baked.cooldown = row.Cooldown;
			baked.repeatCount = 1;
			baked.isValid = true;

			switch (row.ConsumeEffect)
			{
				case ConsumeEffect.Skill:
					baked.skillId = parseEnum<EDT.Skill>(row.EffectParam_1);
					baked.isValid = baked.skillId != EDT.Skill.None;
					break;

				case ConsumeEffect.Reward:
					baked.rewardGroupId = parseInt(row.EffectParam_1, 0);
					baked.repeatCount = parseInt(row.EffectParam_2, 1);
					if (baked.repeatCount <= 0)
					{
						baked.repeatCount = 1;
					}

					baked.isValid = baked.rewardGroupId > 0;
					break;

				case ConsumeEffect.SkillPoint:
					baked.pointSource = parseEnum<EDT.SkillPoint>(row.EffectParam_1);
					baked.pointAmount = parseInt(row.EffectParam_2, 1);
					if (baked.pointAmount <= 0)
					{
						baked.pointAmount = 1;
					}

					baked.isValid = baked.pointSource != EDT.SkillPoint.None;
					break;

				default:
					baked.isValid = false;
					break;
			}

			return baked;
		}

		private static T parseEnum<T>(string text) where T : struct
		{
			if (string.IsNullOrEmpty(text) == true)
			{
				return default(T);
			}

			T value;
			if (Enum.TryParse<T>(text.Trim(), false, out value) == true)
			{
				return value;
			}

			return default(T);
		}

		private static int parseInt(string text, int fallback)
		{
			if (string.IsNullOrEmpty(text) == true)
			{
				return fallback;
			}

			int value;
			if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == true)
			{
				return value;
			}

			return fallback;
		}

		// ── 검증 ──────────────────────────────────────────────────────
		//
		// 경고 목록이 곧 채워야 할 엑셀 작업 지시서다 (STEP 8·10과 같은 방식).
		// 컨버터로의 승격은 STEP 15.

		private static void validate()
		{
			int issues = 0;
			issues += validateRows();
			issues += validateItemSide();
			issues += validateCooldownGroups();

			if (issues > 0)
			{
				Debug.LogWarning($"[ConsumableCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		private static int validateRows()
		{
			int issues = 0;

			Dictionary<int, BakedConsumable>.Enumerator e = _byItem.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedConsumable baked = e.Current.Value;

				Table_Item.Row item = Table_Item.Get(baked.itemId);
				if (item == null)
				{
					Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 에 대응하는 Item 행이 없습니다.");
					issues++;
				}
				else if (item.MainCategory != ItemMainCategory.Consumable)
				{
					Debug.LogWarning($"[ConsumableCatalog] Item {baked.itemId} 의 MainCategory 가 {item.MainCategory} 입니다 — Consumable 이어야 합니다.");
					issues++;
				}
				else if (item.MaxStack == 1)
				{
					// 스택 불가 소모품은 대개 실수다 (설계 8장).
					Debug.LogWarning($"[ConsumableCatalog] Item {baked.itemId} 의 MaxStack 이 1입니다 — 소모품은 대개 스택됩니다.");
					issues++;
				}

				if (baked.effect == ConsumeEffect.None)
				{
					Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 ConsumeEffect 가 비었습니다.");
					issues++;
					continue;
				}

				issues += validateParams(baked);
			}

			return issues;
		}

		private static int validateParams(BakedConsumable baked)
		{
			int issues = 0;

			switch (baked.effect)
			{
				case ConsumeEffect.Skill:
					if (baked.skillId == EDT.Skill.None)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 EffectParam_1 이 Skill 로 해석되지 않습니다.");
						issues++;
						break;
					}

					Table_Skill.Row skill = Table_Skill.Get(baked.skillId);
					if (skill == null)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 스킬 {baked.skillId} 이 Skill 테이블에 없습니다.");
						issues++;
						break;
					}

					// 조건 발동형은 아이템으로 켤 수 없다. 이 검증이 없으면 눌러도 아무 일이 안 일어나고
					// 에러도 안 난다 (설계 5.1).
					if (skill.CastingType != SkillCastingTypes.Instant && skill.CastingType != SkillCastingTypes.Casting)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 스킬 {baked.skillId} 이 {skill.CastingType} 입니다 — Instant 또는 Casting 만 허용됩니다.");
						issues++;
					}

					break;

				case ConsumeEffect.Reward:
					if (baked.rewardGroupId <= 0)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 EffectParam_1 이 Reward.GroupID 로 해석되지 않습니다.");
						issues++;
						break;
					}

					if (RewardCatalog.GetGroup(baked.rewardGroupId).Count == 0)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 보상 그룹 {baked.rewardGroupId} 에 Reward 행이 없습니다.");
						issues++;
					}

					break;

				case ConsumeEffect.SkillPoint:
					if (baked.pointSource == EDT.SkillPoint.None)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 EffectParam_1 이 SkillPoint 로 해석되지 않습니다.");
						issues++;
						break;
					}

					Table_SkillPoint.Row point = Table_SkillPoint.Get(baked.pointSource);
					if (point == null)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 {baked.pointSource} 이 SkillPoint 테이블에 없습니다.");
						issues++;
						break;
					}

					// SourceType=Item 인 출처만 지목할 수 있다 (설계 5.3).
					if (point.SourceType != SkillPointSourceType.Item)
					{
						Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 가 지목한 {baked.pointSource} 의 SourceType 이 {point.SourceType} 입니다 — Item 이어야 합니다.");
						issues++;
					}

					break;
			}

			return issues;
		}

		// MainCategory=Consumable 인데 Consumable 행이 없는 아이템 — 사용 버튼이 죽는다.
		private static int validateItemSide()
		{
			int issues = 0;

			Dictionary<int, Table_Item.Row> items = Table_Item.All();
			Dictionary<int, Table_Item.Row>.Enumerator e = items.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Item.Row item = e.Current.Value;
				if (item.MainCategory != ItemMainCategory.Consumable)
				{
					continue;
				}

				if (_byItem.ContainsKey(item.ID) == false)
				{
					Debug.LogWarning($"[ConsumableCatalog] Item {item.ID}({item.Name}) 는 Consumable 인데 Consumable 행이 없습니다.");
					issues++;
				}
			}

			return issues;
		}

		// 같은 그룹인데 쿨다운 값이 다르면 어느 쪽이 적용될지 데이터만 보고 알 수 없다 (설계 8장).
		private static int validateCooldownGroups()
		{
			int issues = 0;

			Dictionary<int, float> byGroup = new Dictionary<int, float>();
			Dictionary<int, BakedConsumable>.Enumerator e = _byItem.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedConsumable baked = e.Current.Value;
				if (baked.cooldownGroup <= 0)
				{
					continue;
				}

				float known;
				if (byGroup.TryGetValue(baked.cooldownGroup, out known) == false)
				{
					byGroup[baked.cooldownGroup] = baked.cooldown;
					continue;
				}

				if (known != baked.cooldown)
				{
					Debug.LogWarning($"[ConsumableCatalog] CooldownGroup {baked.cooldownGroup} 의 Cooldown 값이 서로 다릅니다 ({known} vs {baked.cooldown}).");
					issues++;
				}
			}

			return issues;
		}
	}
}
