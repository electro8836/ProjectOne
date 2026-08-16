using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DungeonStage
    {
        public class Row {
            public int ID { get; set; } = 0;
            public Dungeon DungeonType { get; set; } = Dungeon.None;
            public int Stage { get; set; } = 0;
            public int MapID { get; set; } = 0;
            public int MonsterSpawnGroupIDs { get; set; } = 0;
            public int MonsterLevel { get; set; } = 0;
            public int RewardExp { get; set; } = 0;
            public int RewardGroupID { get; set; } = 0;
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
                row.DungeonType = (Dungeon)reader.ReadInt32();
                row.Stage = reader.ReadInt32();
                row.MapID = reader.ReadInt32();
                row.MonsterSpawnGroupIDs = reader.ReadInt32();
                row.MonsterLevel = reader.ReadInt32();
                row.RewardExp = reader.ReadInt32();
                row.RewardGroupID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
