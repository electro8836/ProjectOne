using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_StatDetail
    {
        public class Row {
            public StatDetail ID { get; set; } = StatDetail.None;
            public Stat StatID { get; set; } = Stat.None;
            public StatDetailTypes StatDetailType { get; set; } = StatDetailTypes.None;
            public StatValueTypes StatValueType { get; set; } = StatValueTypes.None;
            public string DisplayFormat { get; set; } = string.Empty;
        }

        public const string Filename = "edt_statdetail.bytes";
        public const TableType Type = TableType.TableStatDetail;
        static Dictionary<StatDetail, Row> _all = new Dictionary<StatDetail, Row>();

        public static Row Get( StatDetail id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<StatDetail, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (StatDetail)reader.ReadInt32();
                row.StatID = (Stat)reader.ReadInt32();
                row.StatDetailType = (StatDetailTypes)reader.ReadInt32();
                row.StatValueType = (StatValueTypes)reader.ReadInt32();
                row.DisplayFormat = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
