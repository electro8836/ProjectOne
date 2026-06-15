using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MonsterSpawnInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int MonsterID_01 { get; set; } = 0;
            public int MonsterCount_01 { get; set; } = 0;
            public int IntervalCount_01 { get; set; } = 0;
            public float IntervalTime_01 { get; set; } = 0f;
            public int MonsterID_02 { get; set; } = 0;
            public int MonsterCount_02 { get; set; } = 0;
            public int IntervalCount_02 { get; set; } = 0;
            public float IntervalTime_02 { get; set; } = 0f;
            public int MonsterID_03 { get; set; } = 0;
            public int MonsterCount_03 { get; set; } = 0;
            public int IntervalCount_03 { get; set; } = 0;
            public float IntervalTime_03 { get; set; } = 0f;
        }

        public const string Filename = "edt_monsterspawninfo.bytes";
        public const TableType Type = TableType.TableMonsterSpawnInfo;
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
                row.MonsterID_01 = reader.ReadInt32();
                row.MonsterCount_01 = reader.ReadInt32();
                row.IntervalCount_01 = reader.ReadInt32();
                row.IntervalTime_01 = reader.ReadSingle();
                row.MonsterID_02 = reader.ReadInt32();
                row.MonsterCount_02 = reader.ReadInt32();
                row.IntervalCount_02 = reader.ReadInt32();
                row.IntervalTime_02 = reader.ReadSingle();
                row.MonsterID_03 = reader.ReadInt32();
                row.MonsterCount_03 = reader.ReadInt32();
                row.IntervalCount_03 = reader.ReadInt32();
                row.IntervalTime_03 = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
