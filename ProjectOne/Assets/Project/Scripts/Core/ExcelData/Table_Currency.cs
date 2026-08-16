using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Currency
    {
        public class Row {
            public Currency ID { get; set; } = Currency.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
        }

        public const string Filename = "edt_currency.bytes";
        public const TableType Type = TableType.TableCurrency;
        static Dictionary<Currency, Row> _all = new Dictionary<Currency, Row>();

        public static Row Get( Currency id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Currency, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Currency)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
