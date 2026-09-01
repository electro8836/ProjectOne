using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public class Loader
    {
        // filename만 받는 functor - 경로는 호출부에서 클로저로 처리
        public delegate bool Parser( BinaryReader reader, ref string error );
        public delegate BinaryReader OpenFileFunctor( string filename );
        public delegate void Callback( string filename );

        public string Error { get; private set; }
        public string CurrentFile { get; private set; }

        public bool LoadAll( OpenFileFunctor open_file_functor, Callback callback )
        {
            BinaryReader reader = null;

            #region Table - Buff
            {
                CurrentFile = Table_Buff.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Buff._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - CharacterStat
            {
                CurrentFile = Table_CharacterStat.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_CharacterStat._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - CharacterLevelExp
            {
                CurrentFile = Table_CharacterLevelExp.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_CharacterLevelExp._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Consumable
            {
                CurrentFile = Table_Consumable.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Consumable._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Costume
            {
                CurrentFile = Table_Costume.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Costume._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Currency
            {
                CurrentFile = Table_Currency.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Currency._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Equipment
            {
                CurrentFile = Table_Equipment.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Equipment._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipOption
            {
                CurrentFile = Table_EquipOption.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipOption._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipEnhance
            {
                CurrentFile = Table_EquipEnhance.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipEnhance._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipEnhanceTier
            {
                CurrentFile = Table_EquipEnhanceTier.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipEnhanceTier._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipPromotion
            {
                CurrentFile = Table_EquipPromotion.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipPromotion._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipQuality
            {
                CurrentFile = Table_EquipQuality.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipQuality._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipPurity
            {
                CurrentFile = Table_EquipPurity.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipPurity._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - EquipGradeWeight
            {
                CurrentFile = Table_EquipGradeWeight.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_EquipGradeWeight._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - GachaInfo
            {
                CurrentFile = Table_GachaInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_GachaInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Gacha_Equipment
            {
                CurrentFile = Table_Gacha_Equipment.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Gacha_Equipment._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Item
            {
                CurrentFile = Table_Item.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Item._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Map
            {
                CurrentFile = Table_Map.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Map._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Act
            {
                CurrentFile = Table_Act.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Act._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Field
            {
                CurrentFile = Table_Field.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Field._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Dungeon
            {
                CurrentFile = Table_Dungeon.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Dungeon._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - DungeonStage
            {
                CurrentFile = Table_DungeonStage.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_DungeonStage._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - WeaponMastery
            {
                CurrentFile = Table_WeaponMastery.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_WeaponMastery._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillTreeNode
            {
                CurrentFile = Table_SkillTreeNode.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillTreeNode._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillPoint
            {
                CurrentFile = Table_SkillPoint.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillPoint._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - MasteryLevelExp
            {
                CurrentFile = Table_MasteryLevelExp.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MasteryLevelExp._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Monster
            {
                CurrentFile = Table_Monster.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Monster._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - MonsterStat
            {
                CurrentFile = Table_MonsterStat.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MonsterStat._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - MonsterSpawn
            {
                CurrentFile = Table_MonsterSpawn.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MonsterSpawn._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - BossMonsterPhase
            {
                CurrentFile = Table_BossMonsterPhase.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_BossMonsterPhase._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - MonsterSkillSet
            {
                CurrentFile = Table_MonsterSkillSet.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MonsterSkillSet._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Npc
            {
                CurrentFile = Table_Npc.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Npc._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - NpcSpawn
            {
                CurrentFile = Table_NpcSpawn.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_NpcSpawn._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - NpcDialog
            {
                CurrentFile = Table_NpcDialog.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_NpcDialog._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Option
            {
                CurrentFile = Table_Option.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Option._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Projectile
            {
                CurrentFile = Table_Projectile.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Projectile._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Quest
            {
                CurrentFile = Table_Quest.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Quest._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Reward
            {
                CurrentFile = Table_Reward.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Reward._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - RewardItemPool
            {
                CurrentFile = Table_RewardItemPool.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_RewardItemPool._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Skill
            {
                CurrentFile = Table_Skill.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Skill._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillEffect
            {
                CurrentFile = Table_SkillEffect.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillEffect._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillModifier
            {
                CurrentFile = Table_SkillModifier.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillModifier._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillParamDef
            {
                CurrentFile = Table_SkillParamDef.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillParamDef._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Stat
            {
                CurrentFile = Table_Stat.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Stat._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - StatDetail
            {
                CurrentFile = Table_StatDetail.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_StatDetail._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Summon
            {
                CurrentFile = Table_Summon.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Summon._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            return true;
        }

        public bool Load( BinaryReader reader, Parser parser )
        {
            if( reader == null ) {
                Error = "EDT BinaryReader is null.";
                return false;
            }
            try {
                int rowCount = reader.ReadInt32();
                for( int i = 0; i < rowCount; i++ ) {
                    string error = string.Empty;
                    if( parser( reader, ref error ) == false ) {
                        Error = error;
                        reader.Close();
                        return false;
                    }
                }
                reader.Close();
            } catch( Exception e ) {
                Error = e.Message;
                return false;
            }
            return true;
        }
    }
}
