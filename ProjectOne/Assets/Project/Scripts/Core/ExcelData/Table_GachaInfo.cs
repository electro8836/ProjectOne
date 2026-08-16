using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_GachaInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public GachaTypes GachaType { get; set; } = GachaTypes.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public Currency CurrencyType { get; set; } = Currency.None;
            public int CurrencyCost { get; set; } = 0;
        }

        public const string Filename = "edt_gachainfo.bytes";
        public const TableType Type = TableType.TableGachaInfo;
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
                row.GachaType = (GachaTypes)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.CurrencyType = (Currency)reader.ReadInt32();
                row.CurrencyCost = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
