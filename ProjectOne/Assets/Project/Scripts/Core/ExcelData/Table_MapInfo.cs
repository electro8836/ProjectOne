using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MapInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public BattleType BattleType { get; set; } = BattleType.None;
            public string MapPrefab { get; set; } = string.Empty;
            public string BGM { get; set; } = string.Empty;
            public int SpawnCount { get; set; } = 0;
            public int SpawnInfo_01 { get; set; } = 0;
            public int SpawnInfo_02 { get; set; } = 0;
            public int SpawnInfo_03 { get; set; } = 0;
            public int SpawnInfo_04 { get; set; } = 0;
            public int SpawnInfo_05 { get; set; } = 0;
            public int ClearRewardID { get; set; } = 0;
        }

        public const string Filename = "edt_mapinfo.bytes";
        public const TableType Type = TableType.TableMapInfo;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.BattleType = (BattleType)reader.ReadInt32();
                row.MapPrefab = reader.ReadString();
                row.BGM = reader.ReadString();
                row.SpawnCount = reader.ReadInt32();
                row.SpawnInfo_01 = reader.ReadInt32();
                row.SpawnInfo_02 = reader.ReadInt32();
                row.SpawnInfo_03 = reader.ReadInt32();
                row.SpawnInfo_04 = reader.ReadInt32();
                row.SpawnInfo_05 = reader.ReadInt32();
                row.ClearRewardID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
