using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_LevelupCost
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int MaterialID_01 { get; set; } = 0;
            public int MaterialValue_01 { get; set; } = 0;
            public int MaterialID_02 { get; set; } = 0;
            public int MaterialValue_02 { get; set; } = 0;
            public CurrencyInfo CurrencyType_01 { get; set; } = CurrencyInfo.None;
            public int CurrencyValue_01 { get; set; } = 0;
            public CurrencyInfo CurrencyType_02 { get; set; } = CurrencyInfo.None;
            public int CurrencyValue_02 { get; set; } = 0;
        }

        public const string Filename = "edt_levelupcost.bytes";
        public const TableType Type = TableType.TableLevelupCost;
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
                row.MaterialID_01 = reader.ReadInt32();
                row.MaterialValue_01 = reader.ReadInt32();
                row.MaterialID_02 = reader.ReadInt32();
                row.MaterialValue_02 = reader.ReadInt32();
                row.CurrencyType_01 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyValue_01 = reader.ReadInt32();
                row.CurrencyType_02 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyValue_02 = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
