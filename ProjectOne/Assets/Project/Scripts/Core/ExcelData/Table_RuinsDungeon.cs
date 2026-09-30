using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RuinsDungeon
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int Stage { get; set; } = 0;
            public int MapID { get; set; } = 0;
            public int[] MonsterSpawnGroupIDs { get; set; } = Array.Empty<int>();
            public int MonsterLevel { get; set; } = 0;
            public int ChestCount { get; set; } = 0;
            public int[] ChestRewardGroupIDs { get; set; } = Array.Empty<int>();
            public int CorePhaseGroupID { get; set; } = 0;
            public DungeonChestGrade ChestGrade { get; set; } = DungeonChestGrade.None;
        }

        public const string Filename = "edt_ruinsdungeon.bytes";
        public const TableType Type = TableType.TableRuinsDungeon;
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
                row.Stage = reader.ReadInt32();
                row.MapID = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.MonsterSpawnGroupIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.MonsterSpawnGroupIDs[_i] = reader.ReadInt32(); }
                row.MonsterLevel = reader.ReadInt32();
                row.ChestCount = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.ChestRewardGroupIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.ChestRewardGroupIDs[_i] = reader.ReadInt32(); }
                row.CorePhaseGroupID = reader.ReadInt32();
                row.ChestGrade = (DungeonChestGrade)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
