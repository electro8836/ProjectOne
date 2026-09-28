using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RiftDungeon
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int Wave { get; set; } = 0;
            public int MapID { get; set; } = 0;
            public int[] MonsterSpawnGroupIDs { get; set; } = Array.Empty<int>();
            public int MonsterLevel { get; set; } = 0;
            public Currency RewardCurrency { get; set; } = Currency.None;
            public int RewardCount { get; set; } = 0;
        }

        public const string Filename = "edt_riftdungeon.bytes";
        public const TableType Type = TableType.TableRiftDungeon;
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
                row.Wave = reader.ReadInt32();
                row.MapID = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.MonsterSpawnGroupIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.MonsterSpawnGroupIDs[_i] = reader.ReadInt32(); }
                row.MonsterLevel = reader.ReadInt32();
                row.RewardCurrency = (Currency)reader.ReadInt32();
                row.RewardCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
