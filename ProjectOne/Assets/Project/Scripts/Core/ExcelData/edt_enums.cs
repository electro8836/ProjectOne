using System;
using System.Collections.Generic;

namespace EDT {

    public enum BuffStackPolicy {
        None,
        Refresh,
        Independent,
        Extend,
        Ignore,
    }

    public enum ActionBlockType {
        None,
        Move,
        Turn,
        Attack,
        Cast,
    }

    public enum ConsumeEffect {
        None,
        Skill,
        Reward,
        SkillPoint,
    }

    public enum CostumeType {
        None,
        Weapon,
        Body,
    }

    public enum EquipSlotTypes {
        None,
        Weapon,
        Helmet,
        Armor,
        Gloves,
        Boots,
        Ring,
        Amulet,
        Relic,
    }

    public enum WeaponType {
        None,
        DualBlades,
        RuneSword,
        CrossBow,
        DualPistols,
    }

    public enum GachaTypes {
        None,
        Equipment,
        Skill,
    }

    public enum ItemMainCategory {
        None,
        Equipment,
        Material,
        Consumable,
        Collection,
    }

    public enum ItemSubCategory {
        None,
        Weapon,
        Armor,
        Accessory,
        Relic,
        Crafting,
        Enhance,
        Usable,
        Box,
        SkillBook,
        Costume,
    }

    public enum ItemGradeType {
        None,
        Normal,
        Magic,
        Rare,
        Epic,
        Legendary,
        Mythic,
    }

    public enum DropTier {
        None,
        Tier_1,
        Tier_2,
        Tier_3,
    }

    public enum MapType {
        None,
        Town,
        Field,
        Dungeon,
    }

    public enum WeaponRangeType {
        None,
        Melee,
        Ranged,
    }

    public enum SkillPointSourceType {
        None,
        MasteryLevel,
        Item,
        Achievement,
    }

    public enum MonsterType {
        None,
        Normal,
        Elite,
        Boss,
    }

    public enum MonsterAIType {
        None,
        Melee,
        Ranged,
        Stationary,
        Boss,
    }

    public enum AggroType {
        None,
        Aggressive,
        Neutral,
    }

    public enum NpcType {
        None,
        Shop,
        Blacksmith,
        Storage,
        Portal,
    }

    public enum DialogTriggerType {
        None,
        Default,
        QuestAccept,
        QuestProgress,
        QuestComplete,
    }

    public enum OptionTypes {
        None,
        Stat,
        SkillGrant,
        SkillLevel,
        Modifier,
    }

    public enum QuestCategory {
        None,
        Main,
        Sub,
    }

    public enum QuestAcceptType {
        None,
        Auto,
        Npc,
        UI,
    }

    public enum QuestCompleteType {
        None,
        Auto,
        Npc,
        UI,
    }

    public enum QuestTargetType {
        None,
        KillMonster,
        EquipItemGrade,
        EquipItemLevel,
        Talk,
        DungeonClear,
        ReachLevel,
    }

    public enum RewardType {
        None,
        Item,
        ItemPool,
        Currency,
    }

    public enum SkillCategoryTypes {
        None,
        Normal,
        Active,
        Passive,
    }

    public enum SkillCastingTypes {
        None,
        Instant,
        Casting,
        OnHit,
        OnCombo,
        OnCrit,
        OnDamaged,
        OnLowHP,
        OnKill,
        Aura,
        Passive,
    }

    public enum SkillApplyTarget {
        None,
        Self,
        Enemy,
        Friendly,
        All,
    }

    public enum SkillScanTypes {
        None,
        Circle,
        Sector,
        Line,
        Target,
    }

    public enum SkillEffectTypes {
        None,
        Damage,
        Heal,
        Buff,
        StatChange,
        Projectile,
        Summon,
        Force,
        CooldownReduce,
        BuffConsume,
    }

    public enum SkillEffectOrigin {
        None,
        Caster,
        Target,
        Attacker,
        Victim,
        Owner,
        Location,
    }

    public enum SkillModifierScope {
        None,
        Skill,
        Effect,
        Projectile,
        Summon,
        Buff,
    }

    public enum ModifierOperator {
        None,
        Replace,
        Append,
        Set,
        Add,
        Ratio,
    }

    public enum ParamDefValueTypes {
        None,
        Float,
        Int,
        Bool,
        SkillID,
        EffectID,
        ProjectileID,
        SummonID,
        BuffID,
        StatID,
        StatDetailID,
        ForceType,
    }

    public enum ForceType {
        None,
        Push,
        Pull,
    }

    public enum StatCategory {
        None,
        Offensive,
        Defensive,
        Utility,
    }

    public enum StatDetailTypes {
        None,
        Base,
        Add,
        Ratio,
        Amp,
    }

    public enum StatValueTypes {
        None,
        Flat,
        Percent,
    }

    public enum SummonAIType {
        None,
        Stationary,
        Follow,
        Chase,
        Wander,
        Orbit,
    }

    public enum Buff {
        None,
        BUFF_Stagger,
        BUFF_Stun,
        BUFF_DamageTakenUp,
        BUFF_CombatRune,
    }

    public enum Currency {
        None,
        Gold,
        Dia,
        Ruby,
    }

    public enum EquipEnhanceTier {
        None,
        Tier_1,
        Tier_2,
        Tier_3,
        Tier_4,
        Tier_5,
        Tier_6,
    }

    public enum EquipPurity {
        None,
        Purity_1,
        Purity_2,
        Purity_3,
        Purity_4,
        Purity_5,
    }

    public enum Dungeon {
        None,
        Gold,
        Exp,
    }

    public enum WeaponMastery {
        None,
        Mastery_DualBlades,
        Mastery_RuneSword,
        Mastery_CrossBow,
        Mastery_DualPistols,
    }

    public enum SkillPoint {
        None,
        SkillPoint_Level,
        SkillPoint_Item,
        SkillPoint_Achievement,
    }

    public enum Option {
        None,
        OPT_ATK_ADD,
        OPT_ATK_RATIO,
        OPT_ATK_AMP,
        OPT_DAMAGE_BONUS_ADD,
        OPT_MELEE_DAMAGE_BONUS_ADD,
        OPT_RANGE_DAMAGE_BONUS_ADD,
        OPT_ATK_SPEED_ADD,
        OPT_CRIT_RATE_ADD,
        OPT_CRIT_DAMAGE_ADD,
        OPT_FINAL_DAMAGE_AMP_ADD,
        OPT_BOSS_DAMAGE_BONUS_ADD,
        OPT_DEF_PEN_ADD,
        OPT_MAX_HP_ADD,
        OPT_MAX_HP_RATIO,
        OPT_MAX_HP_AMP,
        OPT_HP_REGEN_ADD,
        OPT_HP_REGEN_RATIO,
        OPT_HP_REGEN_AMP,
        OPT_DEF_ADD,
        OPT_DEF_RATIO,
        OPT_DEF_AMP,
        OPT_DAMAGE_REDUCTION_ADD,
        OPT_LIFE_ON_HIT_ADD,
        OPT_LIFE_ON_HIT_RATIO,
        OPT_LIFE_ON_HIT_AMP,
        OPT_EVASION_RATE_ADD,
        OPT_BLOCK_RATE_ADD,
        OPT_MOVE_SPEED_ADD,
        OPT_EXP_BONUS_ADD,
        OPT_PICKUP_RANGE_ADD,
        OPT_GOLD_DROP_BONUS_ADD,
        OPT_DAMAGE_TAKEN_AMP_ADD,
    }

    public enum Projectile {
        None,
        PJT_CrossBow_Bolt,
        PJT_Monster_Arrow,
    }

    public enum Skill {
        None,
        Skill_DualBlades_Attack,
        Skill_DualBlades_Active_01,
        Skill_DualBlades_Active_02,
        Skill_DualBlades_Passive_01,
        Skill_RuneSword_Attack,
        Skill_RuneSword_Active_01,
        Skill_RuneSword_Passive_01,
        Skill_CrossBow_Attack,
        Skill_CrossBow_Active_01,
        Skill_CrossBow_Passive_01,
        Skill_DualPistols_Attack,
        Skill_DualPistols_Active_01,
        Skill_DualPistols_Passive_01,
        Skill_Monster_MeleeAttack_01,
        Skill_Monster_RangeAttack_01,
        Skill_Monster_CastAttack_01,
    }

    public enum SkillEffect {
        None,
        SE_DualBlades_Attack_Damage,
        SE_RuneSword_Attack_Damage,
        SE_CrossBow_Attack_Projectile,
        SE_CrossBow_Attack_Damage,
        SE_DualPistol_Attack_Damage,
        SE_Monster_MeleeAttack_Damage,
        SE_Monster_RangeAttack_Projectile,
        SE_Monster_RangeAttack_Damage,
        SE_Monster_CastAttack_Damage,
        SE_Common_Stagger_Buff,
        SE_Boss_Break_Stun_Buff,
        SE_Boss_Break_DamageTakenUp_Buff,
        SE_Debuff_DamageTakenUp_StatChange,
        SE_DualBlades_Active_01_Damage,
        SE_DualBlades_Active_02_Damage,
        SE_BuffRune_AtkUp,
        SE_BuffRune_DefUp,
    }

    public enum SkillModifier {
        None,
    }

    public enum Stat {
        None,
        Stat_Atk,
        Stat_AtkSpeed,
        Stat_CritRate,
        Stat_CritDamage,
        Stat_DamageBonus,
        Stat_MeleeDamageBonus,
        Stat_RangeDamageBonus,
        Stat_BossDamageBonus,
        Stat_FinalDamageAmp,
        Stat_DefPen,
        Stat_MaxHp,
        Stat_HpRegen,
        Stat_Def,
        Stat_DamageReduction,
        Stat_LifeOnHit,
        Stat_EvasionRate,
        Stat_BlockRate,
        Stat_MoveSpeed,
        Stat_ExpBonus,
        Stat_PickupRange,
        Stat_GoldDropBonus,
        Stat_DamageTakenAmp,
    }

    public enum StatDetail {
        None,
        StatDetail_Atk_Base,
        StatDetail_Atk_Add,
        StatDetail_Atk_Ratio,
        StatDetail_Atk_Amp,
        StatDetail_AtkSpeed_Base,
        StatDetail_AtkSpeed_Add,
        StatDetail_CritRate_Base,
        StatDetail_CritRate_Add,
        StatDetail_CritDamage_Base,
        StatDetail_CritDamage_Add,
        StatDetail_DamageBonus_Base,
        StatDetail_DamageBonus_Add,
        StatDetail_MeleeDamageBonus_Base,
        StatDetail_MeleeDamageBonus_Add,
        StatDetail_RangeDamageBonus_Base,
        StatDetail_RangeDamageBonus_Add,
        StatDetail_BossDamageBonus_Base,
        StatDetail_BossDamageBonus_Add,
        StatDetail_FinalDamageAmp_Base,
        StatDetail_FinalDamageAmp_Add,
        StatDetail_DefPen_Base,
        StatDetail_DefPen_Add,
        StatDetail_MaxHp_Base,
        StatDetail_MaxHp_Add,
        StatDetail_MaxHp_Ratio,
        StatDetail_MaxHp_Amp,
        StatDetail_HpRegen_Base,
        StatDetail_HpRegen_Add,
        StatDetail_HpRegen_Ratio,
        StatDetail_HpRegen_Amp,
        StatDetail_Def_Base,
        StatDetail_Def_Add,
        StatDetail_Def_Ratio,
        StatDetail_Def_Amp,
        StatDetail_DamageReduction_Base,
        StatDetail_DamageReduction_Add,
        StatDetail_LifeOnHit_Base,
        StatDetail_LifeOnHit_Add,
        StatDetail_LifeOnHit_Ratio,
        StatDetail_LifeOnHit_Amp,
        StatDetail_EvasionRate_Base,
        StatDetail_EvasionRate_Add,
        StatDetail_BlockRate_Base,
        StatDetail_BlockRate_Add,
        StatDetail_MoveSpeed_Base,
        StatDetail_MoveSpeed_Add,
        StatDetail_ExpBonus_Base,
        StatDetail_ExpBonus_Add,
        StatDetail_PickupRange_Base,
        StatDetail_PickupRange_Add,
        StatDetail_GoldDropBonus_Base,
        StatDetail_GoldDropBonus_Add,
        StatDetail_DamageTakenAmp_Base,
        StatDetail_DamageTakenAmp_Add,
    }

    public enum Summon {
        None,
    }

    public enum TableType {
        None,
        TableBuff,
        TableCharacterStat,
        TableCharacterLevelExp,
        TableConsumable,
        TableCostume,
        TableCurrency,
        TableEquipment,
        TableEquipOption,
        TableEquipEnhance,
        TableEquipEnhanceTier,
        TableEquipPromotion,
        TableEquipQuality,
        TableEquipPurity,
        TableEquipGradeWeight,
        TableGachaInfo,
        TableGacha_Equipment,
        TableItem,
        TableMap,
        TableAct,
        TableField,
        TableDungeon,
        TableDungeonStage,
        TableWeaponMastery,
        TableSkillTreeNode,
        TableSkillPoint,
        TableMasteryLevelExp,
        TableMonster,
        TableMonsterStat,
        TableMonsterSpawn,
        TableNpc,
        TableNpcSpawn,
        TableNpcDialog,
        TableOption,
        TableProjectile,
        TableQuest,
        TableReward,
        TableRewardItemPool,
        TableSkill,
        TableSkillEffect,
        TableSkillModifier,
        TableSkillParamDef,
        TableStat,
        TableStatDetail,
        TableSummon,
    }

}
