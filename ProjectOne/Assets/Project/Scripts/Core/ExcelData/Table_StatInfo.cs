using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_StatInfo
    {
        public class Row {
            public StatInfo ID { get; set; } = StatInfo.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public int MinValue { get; set; } = 0;
            public int MaxValue { get; set; } = 0;
            public bool IsRatio { get; set; } = false;
        }

        public const string Filename = "edt_statinfo.bytes";
        public const TableType Type = TableType.TableStatInfo;
        static Dictionary<StatInfo, Row> _all = new Dictionary<StatInfo, Row>();

        public static Row Get( StatInfo id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<StatInfo, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (StatInfo)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.MinValue = reader.ReadInt32();
                row.MaxValue = reader.ReadInt32();
                row.IsRatio = reader.ReadBoolean();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
