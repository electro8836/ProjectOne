using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_UnlockCondition
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public CharacterUnlockState UnlockState { get; set; } = CharacterUnlockState.None;
            public CurrencyInfo CurrencyType_1 { get; set; } = CurrencyInfo.None;
            public int CurrencyCost_1 { get; set; } = 0;
            public CurrencyInfo CurrencyType_2 { get; set; } = CurrencyInfo.None;
            public int CurrencyCost_2 { get; set; } = 0;
        }

        public const string Filename = "edt_unlockcondition.bytes";
        public const TableType Type = TableType.TableUnlockCondition;
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
                row.Name = reader.ReadString();
                row.UnlockState = (CharacterUnlockState)reader.ReadInt32();
                row.CurrencyType_1 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyCost_1 = reader.ReadInt32();
                row.CurrencyType_2 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyCost_2 = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
