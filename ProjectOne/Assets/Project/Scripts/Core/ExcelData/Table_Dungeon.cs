using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Dungeon
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string RecommandedLvText { get; set; } = string.Empty;
            public int TotalStageCount { get; set; } = 0;
            public int StageGroupID_1 { get; set; } = 0;
            public int StageGroupID_2 { get; set; } = 0;
            public int StageGroupID_3 { get; set; } = 0;
            public int StageGroupID_4 { get; set; } = 0;
            public int StageGroupID_5 { get; set; } = 0;
            public int ExtraStageGroupID { get; set; } = 0;
            public int ClearExp { get; set; } = 0;
            public int ExtraClearExp { get; set; } = 0;
        }

        public const string Filename = "edt_dungeon.bytes";
        public const TableType Type = TableType.TableDungeon;
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
                row.Icon = reader.ReadString();
                row.RecommandedLvText = reader.ReadString();
                row.TotalStageCount = reader.ReadInt32();
                row.StageGroupID_1 = reader.ReadInt32();
                row.StageGroupID_2 = reader.ReadInt32();
                row.StageGroupID_3 = reader.ReadInt32();
                row.StageGroupID_4 = reader.ReadInt32();
                row.StageGroupID_5 = reader.ReadInt32();
                row.ExtraStageGroupID = reader.ReadInt32();
                row.ClearExp = reader.ReadInt32();
                row.ExtraClearExp = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
