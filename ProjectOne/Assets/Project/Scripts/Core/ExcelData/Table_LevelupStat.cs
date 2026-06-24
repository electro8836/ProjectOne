using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_LevelupStat
    {
        public class Row {
            public int ID { get; set; } = 0;
            public StatInfo StatID_1 { get; set; } = StatInfo.None;
            public float StatValue_1 { get; set; } = 0f;
            public StatInfo StatID_2 { get; set; } = StatInfo.None;
            public float StatValue_2 { get; set; } = 0f;
            public StatInfo StatID_3 { get; set; } = StatInfo.None;
            public float StatValue_3 { get; set; } = 0f;
            public StatInfo StatID_4 { get; set; } = StatInfo.None;
            public float StatValue_4 { get; set; } = 0f;
        }

        public const string Filename = "edt_levelupstat.bytes";
        public const TableType Type = TableType.TableLevelupStat;
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
                row.StatID_1 = (StatInfo)reader.ReadInt32();
                row.StatValue_1 = reader.ReadSingle();
                row.StatID_2 = (StatInfo)reader.ReadInt32();
                row.StatValue_2 = reader.ReadSingle();
                row.StatID_3 = (StatInfo)reader.ReadInt32();
                row.StatValue_3 = reader.ReadSingle();
                row.StatID_4 = (StatInfo)reader.ReadInt32();
                row.StatValue_4 = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
