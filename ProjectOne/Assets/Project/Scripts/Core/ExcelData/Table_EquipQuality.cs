using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipQuality
    {
        public class Row {
            public int ID { get; set; } = 0;
            public float MinValue { get; set; } = 0f;
            public float MaxValue { get; set; } = 0f;
            public int AssignWeight { get; set; } = 0;
            public float CalcRate { get; set; } = 0f;
        }

        public const string Filename = "edt_equipquality.bytes";
        public const TableType Type = TableType.TableEquipQuality;
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
                row.MinValue = reader.ReadSingle();
                row.MaxValue = reader.ReadSingle();
                row.AssignWeight = reader.ReadInt32();
                row.CalcRate = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
