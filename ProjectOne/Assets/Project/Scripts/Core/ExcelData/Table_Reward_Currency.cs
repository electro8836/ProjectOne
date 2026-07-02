using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Reward_Currency
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int CurrencySourceID { get; set; } = 0;
            public CurrencyInfo CurrencyType { get; set; } = CurrencyInfo.None;
            public int MinCount { get; set; } = 0;
            public int MaxCount { get; set; } = 0;
        }

        public const string Filename = "edt_reward_currency.bytes";
        public const TableType Type = TableType.TableReward_Currency;
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
                row.CurrencySourceID = reader.ReadInt32();
                row.CurrencyType = (CurrencyInfo)reader.ReadInt32();
                row.MinCount = reader.ReadInt32();
                row.MaxCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
