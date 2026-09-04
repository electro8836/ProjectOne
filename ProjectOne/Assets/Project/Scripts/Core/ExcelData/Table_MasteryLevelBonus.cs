using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MasteryLevelBonus
    {
        public class Row {
            public int ID { get; set; } = 0;
            public Option LevelBonusOption { get; set; } = Option.None;
            public float LevelBonusPerLevel { get; set; } = 0f;
        }

        public const string Filename = "edt_masterylevelbonus.bytes";
        public const TableType Type = TableType.TableMasteryLevelBonus;
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
                row.LevelBonusOption = (Option)reader.ReadInt32();
                row.LevelBonusPerLevel = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
