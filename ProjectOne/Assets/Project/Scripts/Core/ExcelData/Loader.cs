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

            #region Table - Character
            {
                CurrentFile = Table_Character.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Character._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - LevelExp
            {
                CurrentFile = Table_LevelExp.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_LevelExp._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - UnlockCondition
            {
                CurrentFile = Table_UnlockCondition.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_UnlockCondition._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - LevelupCost
            {
                CurrentFile = Table_LevelupCost.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_LevelupCost._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - CharacterTrait
            {
                CurrentFile = Table_CharacterTrait.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_CharacterTrait._parser ) == false ) {
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

            #region Table - CurrencyInfo
            {
                CurrentFile = Table_CurrencyInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_CurrencyInfo._parser ) == false ) {
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

            #region Table - Enchant
            {
                CurrentFile = Table_Enchant.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Enchant._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Craft
            {
                CurrentFile = Table_Craft.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Craft._parser ) == false ) {
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

            #region Table - MapInfo
            {
                CurrentFile = Table_MapInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MapInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - MonsterSpawnInfo
            {
                CurrentFile = Table_MonsterSpawnInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MonsterSpawnInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - Material
            {
                CurrentFile = Table_Material.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_Material._parser ) == false ) {
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

            #region Table - MonsterPhase
            {
                CurrentFile = Table_MonsterPhase.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_MonsterPhase._parser ) == false ) {
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

            #region Table - SkillInfo
            {
                CurrentFile = Table_SkillInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillInfo._parser ) == false ) {
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

            #region Table - BuffInfo
            {
                CurrentFile = Table_BuffInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_BuffInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkillSet
            {
                CurrentFile = Table_SkillSet.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkillSet._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - SkinInfo
            {
                CurrentFile = Table_SkinInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_SkinInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - StatInfo
            {
                CurrentFile = Table_StatInfo.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_StatInfo._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - BaseStat
            {
                CurrentFile = Table_BaseStat.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_BaseStat._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - LevelupStat
            {
                CurrentFile = Table_LevelupStat.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_LevelupStat._parser ) == false ) {
                    return false;
                }
                if( callback != null ) { callback( CurrentFile ); }
            }
            #endregion

            #region Table - UIWidgetPath
            {
                CurrentFile = Table_UIWidgetPath.Filename;
                reader = open_file_functor( CurrentFile );
                if( Load( reader, Table_UIWidgetPath._parser ) == false ) {
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
