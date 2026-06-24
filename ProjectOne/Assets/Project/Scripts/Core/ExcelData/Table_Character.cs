using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Character
    {
        public class Row {
            public int ID { get; set; } = 0;
            public CharacterTypes CharacterType { get; set; } = CharacterTypes.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public CharacterGrade Grade { get; set; } = CharacterGrade.None;
            public string Path { get; set; } = string.Empty;
            public int SkinID { get; set; } = 0;
            public int UnlockConditionID { get; set; } = 0;
            public int BaseStatID { get; set; } = 0;
            public int LevelupStatID { get; set; } = 0;
            public int TraitGroup_1 { get; set; } = 0;
            public int TraitGroup_2 { get; set; } = 0;
            public int TraitGroup_3 { get; set; } = 0;
            public int TraitGroup_4 { get; set; } = 0;
            public int TraitGroup_5 { get; set; } = 0;
        }

        public const string Filename = "edt_character.bytes";
        public const TableType Type = TableType.TableCharacter;
        static Dictionary<int, Row> _all = new Dictionary<int, Row>();

        public static Row Get( int id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<int, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = reader.ReadInt32();
                row.CharacterType = (CharacterTypes)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.Grade = (CharacterGrade)reader.ReadInt32();
                row.Path = reader.ReadString();
                row.SkinID = reader.ReadInt32();
                row.UnlockConditionID = reader.ReadInt32();
                row.BaseStatID = reader.ReadInt32();
                row.LevelupStatID = reader.ReadInt32();
                row.TraitGroup_1 = reader.ReadInt32();
                row.TraitGroup_2 = reader.ReadInt32();
                row.TraitGroup_3 = reader.ReadInt32();
                row.TraitGroup_4 = reader.ReadInt32();
                row.TraitGroup_5 = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
