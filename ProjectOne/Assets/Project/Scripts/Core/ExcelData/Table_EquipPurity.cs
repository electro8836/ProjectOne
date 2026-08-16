using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipPurity
    {
        public class Row {
            public EquipPurity ID { get; set; } = EquipPurity.None;
            public string DisplayName { get; set; } = string.Empty;
            public float OptionMultiplier { get; set; } = 0f;
            public int AssignWeight { get; set; } = 0;
            public float CalcRate { get; set; } = 0f;
            public string ColorHex { get; set; } = string.Empty;
        }

        public const string Filename = "edt_equippurity.bytes";
        public const TableType Type = TableType.TableEquipPurity;
        static Dictionary<EquipPurity, Row> _all = new Dictionary<EquipPurity, Row>();

        public static Row Get( EquipPurity id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<EquipPurity, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (EquipPurity)reader.ReadInt32();
                row.DisplayName = reader.ReadString();
                row.OptionMultiplier = reader.ReadSingle();
                row.AssignWeight = reader.ReadInt32();
                row.CalcRate = reader.ReadSingle();
                row.ColorHex = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
