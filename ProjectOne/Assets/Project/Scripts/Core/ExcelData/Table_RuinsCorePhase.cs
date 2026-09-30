using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RuinsCorePhase
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public int PhaseOrder { get; set; } = 0;
            public float HpThreshold { get; set; } = 0f;
            public int SpawnGroupIndex { get; set; } = 0;
            public int[] HazardIDs { get; set; } = Array.Empty<int>();
        }

        public const string Filename = "edt_ruinscorephase.bytes";
        public const TableType Type = TableType.TableRuinsCorePhase;
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
                row.GroupID = reader.ReadInt32();
                row.PhaseOrder = reader.ReadInt32();
                row.HpThreshold = reader.ReadSingle();
                row.SpawnGroupIndex = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.HazardIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.HazardIDs[_i] = reader.ReadInt32(); }
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
