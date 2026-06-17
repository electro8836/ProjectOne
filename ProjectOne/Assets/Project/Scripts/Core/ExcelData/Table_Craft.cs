using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Craft
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int Result_EquipmentID { get; set; } = 0;
            public CurrencyInfo CurrencyType_01 { get; set; } = CurrencyInfo.None;
            public int CurrencyCost_01 { get; set; } = 0;
            public CurrencyInfo CurrencyType_02 { get; set; } = CurrencyInfo.None;
            public int CurrencyCost_02 { get; set; } = 0;
            public int Material_01_ID { get; set; } = 0;
            public int Material_01_Count { get; set; } = 0;
            public int Material_02_ID { get; set; } = 0;
            public int Material_02_Count { get; set; } = 0;
            public int Material_03_ID { get; set; } = 0;
            public int Material_03_Count { get; set; } = 0;
        }

        public const string Filename = "edt_craft.bytes";
        public const TableType Type = TableType.TableCraft;
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
                row.Result_EquipmentID = reader.ReadInt32();
                row.CurrencyType_01 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyCost_01 = reader.ReadInt32();
                row.CurrencyType_02 = (CurrencyInfo)reader.ReadInt32();
                row.CurrencyCost_02 = reader.ReadInt32();
                row.Material_01_ID = reader.ReadInt32();
                row.Material_01_Count = reader.ReadInt32();
                row.Material_02_ID = reader.ReadInt32();
                row.Material_02_Count = reader.ReadInt32();
                row.Material_03_ID = reader.ReadInt32();
                row.Material_03_Count = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
