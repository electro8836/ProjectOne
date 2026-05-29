using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DungeonStage
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int DungeonListID { get; set; } = 0;
            public int Step { get; set; } = 0;
            public float LimitTime { get; set; } = 0f;
            public int[] NormalMonsterIDs { get; set; } = Array.Empty<int>();
            public int[] EliteMonsterIDs { get; set; } = Array.Empty<int>();
            public int BossMonsterID { get; set; } = 0;
            public int SpawnCount { get; set; } = 0;
            public float StatMultiplier { get; set; } = 0f;
            public int[] RewardID { get; set; } = Array.Empty<int>();
        }

        public const string Filename = "edt_dungeonstage.bytes";
        public const TableType Type = TableType.TableDungeonStage;
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
                row.DungeonListID = reader.ReadInt32();
                row.Step = reader.ReadInt32();
                row.LimitTime = reader.ReadSingle();
                { int _n = reader.ReadInt32(); row.NormalMonsterIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.NormalMonsterIDs[_i] = reader.ReadInt32(); }
                { int _n = reader.ReadInt32(); row.EliteMonsterIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.EliteMonsterIDs[_i] = reader.ReadInt32(); }
                row.BossMonsterID = reader.ReadInt32();
                row.SpawnCount = reader.ReadInt32();
                row.StatMultiplier = reader.ReadSingle();
                { int _n = reader.ReadInt32(); row.RewardID = new int[_n]; for(int _i=0;_i<_n;_i++) row.RewardID[_i] = reader.ReadInt32(); }
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
