using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Stage
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public MapModeType ModeType { get; set; } = MapModeType.None;
            public int ClearValue { get; set; } = 0;
            public int LimitTime { get; set; } = 0;
            public int MapID { get; set; } = 0;
            public int[] MapObjectIDs { get; set; } = Array.Empty<int>();
            public int[] MonsterSpawnGroupIDs { get; set; } = Array.Empty<int>();
            public int DropObjectGroupID { get; set; } = 0;
            public int[] StageRewardIDs { get; set; } = Array.Empty<int>();
        }

        public const string Filename = "edt_stage.bytes";
        public const TableType Type = TableType.TableStage;
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
                row.ModeType = (MapModeType)reader.ReadInt32();
                row.ClearValue = reader.ReadInt32();
                row.LimitTime = reader.ReadInt32();
                row.MapID = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.MapObjectIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.MapObjectIDs[_i] = reader.ReadInt32(); }
                { int _n = reader.ReadInt32(); row.MonsterSpawnGroupIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.MonsterSpawnGroupIDs[_i] = reader.ReadInt32(); }
                row.DropObjectGroupID = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.StageRewardIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.StageRewardIDs[_i] = reader.ReadInt32(); }
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
