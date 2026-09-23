using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ItemTransfer
    {
        public class Row {
            public int ID { get; set; } = 0;
            public EquipSlotTypes EquipmentType { get; set; } = EquipSlotTypes.None;
            public ItemGradeType Grade { get; set; } = ItemGradeType.None;
            public Currency ReqCurrency { get; set; } = Currency.None;
            public int ReqCost { get; set; } = 0;
        }

        public const string Filename = "edt_itemtransfer.bytes";
        public const TableType Type = TableType.TableItemTransfer;
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
                row.Grade = (ItemGradeType)reader.ReadInt32();
                row.ReqCurrency = (Currency)reader.ReadInt32();
                row.ReqCost = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
