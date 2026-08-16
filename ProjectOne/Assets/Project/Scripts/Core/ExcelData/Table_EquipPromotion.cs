using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipPromotion
    {
        public class Row {
            public int ID { get; set; } = 0;
            public EquipSlotTypes Category { get; set; } = EquipSlotTypes.None;
            public ItemGradeType FromGrade { get; set; } = ItemGradeType.None;
            public ItemGradeType ToGrade { get; set; } = ItemGradeType.None;
            public int CostItemID_1 { get; set; } = 0;
            public int CostItemCnt_1 { get; set; } = 0;
            public int CostItemID_2 { get; set; } = 0;
            public int CostItemCnt_2 { get; set; } = 0;
            public int CostItemID_3 { get; set; } = 0;
            public int CostItemCnt_3 { get; set; } = 0;
            public int CostItemID_4 { get; set; } = 0;
            public int CostItemCnt_4 { get; set; } = 0;
            public Currency CostCurrencyID { get; set; } = Currency.None;
            public int CostCurrencyValue { get; set; } = 0;
        }

        public const string Filename = "edt_equippromotion.bytes";
        public const TableType Type = TableType.TableEquipPromotion;
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
                row.Category = (EquipSlotTypes)reader.ReadInt32();
                row.FromGrade = (ItemGradeType)reader.ReadInt32();
                row.ToGrade = (ItemGradeType)reader.ReadInt32();
                row.CostItemID_1 = reader.ReadInt32();
                row.CostItemCnt_1 = reader.ReadInt32();
                row.CostItemID_2 = reader.ReadInt32();
                row.CostItemCnt_2 = reader.ReadInt32();
                row.CostItemID_3 = reader.ReadInt32();
                row.CostItemCnt_3 = reader.ReadInt32();
                row.CostItemID_4 = reader.ReadInt32();
                row.CostItemCnt_4 = reader.ReadInt32();
                row.CostCurrencyID = (Currency)reader.ReadInt32();
                row.CostCurrencyValue = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
