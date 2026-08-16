using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Stat
    {
        public class Row {
            public Stat ID { get; set; } = Stat.None;
            public string Name { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public StatCategory Category { get; set; } = StatCategory.None;
            public StatValueTypes ValueType { get; set; } = StatValueTypes.None;
            public float MinValue { get; set; } = 0f;
            public float MaxValue { get; set; } = 0f;
        }

        public const string Filename = "edt_stat.bytes";
        public const TableType Type = TableType.TableStat;
        static Dictionary<Stat, Row> _all = new Dictionary<Stat, Row>();

        public static Row Get( Stat id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Stat, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Stat)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Icon = reader.ReadString();
                row.Category = (StatCategory)reader.ReadInt32();
                row.ValueType = (StatValueTypes)reader.ReadInt32();
                row.MinValue = reader.ReadSingle();
                row.MaxValue = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
