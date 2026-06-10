using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_CurrencyInfo
    {
        public class Row {
            public CurrencyInfo ID { get; set; } = CurrencyInfo.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public bool Regenable { get; set; } = false;
            public int RegenInterval { get; set; } = 0;
            public int RegenAmount { get; set; } = 0;
            public int RegenMax { get; set; } = 0;
            public int NavigationLink { get; set; } = 0;
        }

        public const string Filename = "edt_currencyinfo.bytes";
        public const TableType Type = TableType.TableCurrencyInfo;
        static Dictionary<CurrencyInfo, Row> _all = new Dictionary<CurrencyInfo, Row>();

        public static Row Get( CurrencyInfo id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<CurrencyInfo, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (CurrencyInfo)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.Regenable = reader.ReadBoolean();
                row.RegenInterval = reader.ReadInt32();
                row.RegenAmount = reader.ReadInt32();
                row.RegenMax = reader.ReadInt32();
                row.NavigationLink = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
