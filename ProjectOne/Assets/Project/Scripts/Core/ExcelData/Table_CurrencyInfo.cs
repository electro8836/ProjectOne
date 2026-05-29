using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_CurrencyInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public CurrencyType CurrencyType { get; set; } = CurrencyType.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public int RegenInterval { get; set; } = 0;
            public int RegenAmount { get; set; } = 0;
            public int RegenMax { get; set; } = 0;
        }

        public const string Filename = "edt_currencyinfo.bytes";
        public const TableType Type = TableType.TableCurrencyInfo;
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
                row.CurrencyType = (CurrencyType)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.RegenInterval = reader.ReadInt32();
                row.RegenAmount = reader.ReadInt32();
                row.RegenMax = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
