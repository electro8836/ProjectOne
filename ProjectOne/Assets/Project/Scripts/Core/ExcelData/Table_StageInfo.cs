using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_StageInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int StageNum { get; set; } = 0;
            public int StageStep { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string MapPrefab { get; set; } = string.Empty;
            public string BGM { get; set; } = string.Empty;
            public int[] NormalMonsterIDs { get; set; } = Array.Empty<int>();
            public int[] EliteMonsterIDs { get; set; } = Array.Empty<int>();
            public int BossMonsterID { get; set; } = 0;
            public int ReqKillCount { get; set; } = 0;
            public float StepStatMultiplier { get; set; } = 0f;
            public int ClearRewardID { get; set; } = 0;
        }

        public const string Filename = "edt_stageinfo.bytes";
        public const TableType Type = TableType.TableStageInfo;
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
                row.StageNum = reader.ReadInt32();
                row.StageStep = reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.MapPrefab = reader.ReadString();
                row.BGM = reader.ReadString();
                { int _n = reader.ReadInt32(); row.NormalMonsterIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.NormalMonsterIDs[_i] = reader.ReadInt32(); }
                { int _n = reader.ReadInt32(); row.EliteMonsterIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.EliteMonsterIDs[_i] = reader.ReadInt32(); }
                row.BossMonsterID = reader.ReadInt32();
                row.ReqKillCount = reader.ReadInt32();
                row.StepStatMultiplier = reader.ReadSingle();
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
