using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Character
    {
        public class Row {
            public int ID { get; set; } = 0;
            public CharacterTypes CharacterClass { get; set; } = CharacterTypes.None;
            public int ClassGrade { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public int SkinID { get; set; } = 0;
            public int BaseStatID { get; set; } = 0;
            public int SkillSetID { get; set; } = 0;
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
                row.CharacterClass = (CharacterTypes)reader.ReadInt32();
                row.ClassGrade = reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Path = reader.ReadString();
                row.SkinID = reader.ReadInt32();
                row.BaseStatID = reader.ReadInt32();
                row.SkillSetID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
