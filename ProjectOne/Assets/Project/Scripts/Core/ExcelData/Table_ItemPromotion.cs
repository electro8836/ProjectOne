using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ItemPromotion
    {
        public class Row {
            public int ID { get; set; } = 0;
            public EquipSlotTypes EquipmentType { get; set; } = EquipSlotTypes.None;
            public ItemGradeType FromGrade { get; set; } = ItemGradeType.None;
            public ItemGradeType ToGrade { get; set; } = ItemGradeType.None;
            public Currency ReqCurrency_1 { get; set; } = Currency.None;
            public int ReqCost_1 { get; set; } = 0;
            public Currency ReqCurrency_2 { get; set; } = Currency.None;
            public int ReqCost_2 { get; set; } = 0;
            public int ReqGoldCount { get; set; } = 0;
        }

        public const string Filename = "edt_itempromotion.bytes";
        public const TableType Type = TableType.TableItemPromotion;
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
                row.EquipmentType = (EquipSlotTypes)reader.ReadInt32();
                row.FromGrade = (ItemGradeType)reader.ReadInt32();
                row.ToGrade = (ItemGradeType)reader.ReadInt32();
                row.ReqCurrency_1 = (Currency)reader.ReadInt32();
                row.ReqCost_1 = reader.ReadInt32();
                row.ReqCurrency_2 = (Currency)reader.ReadInt32();
                row.ReqCost_2 = reader.ReadInt32();
                row.ReqGoldCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
