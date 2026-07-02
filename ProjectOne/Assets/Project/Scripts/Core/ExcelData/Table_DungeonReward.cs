using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DungeonReward
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int DungeonID { get; set; } = 0;
            public int StageRound { get; set; } = 0;
            public int[] StageRollRewardIDs { get; set; } = Array.Empty<int>();
            public int[] StageFixRewardIDs { get; set; } = Array.Empty<int>();
        }

        public const string Filename = "edt_dungeonreward.bytes";
        public const TableType Type = TableType.TableDungeonReward;
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
                row.DungeonID = reader.ReadInt32();
                row.StageRound = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.StageRollRewardIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.StageRollRewardIDs[_i] = reader.ReadInt32(); }
                { int _n = reader.ReadInt32(); row.StageFixRewardIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.StageFixRewardIDs[_i] = reader.ReadInt32(); }
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
