using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MonsterAI
    {
        public class Row {
            public int ID { get; set; } = 0;
            public MonsterAIType AIType { get; set; } = MonsterAIType.None;
            public AggroType AggroType { get; set; } = AggroType.None;
            public float DetectRange { get; set; } = 0f;
            public float LeashRange { get; set; } = 0f;
        }

        public const string Filename = "edt_monsterai.bytes";
        public const TableType Type = TableType.TableMonsterAI;
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
                row.AIType = (MonsterAIType)reader.ReadInt32();
                row.AggroType = (AggroType)reader.ReadInt32();
                row.DetectRange = reader.ReadSingle();
                row.LeashRange = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
