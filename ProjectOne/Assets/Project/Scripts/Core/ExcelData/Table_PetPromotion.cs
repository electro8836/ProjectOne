using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_PetPromotion
    {
        public class Row {
            public int ID { get; set; } = 0;
            public ItemGradeType FromGrade { get; set; } = ItemGradeType.None;
            public ItemGradeType ToGrade { get; set; } = ItemGradeType.None;
            public Currency CostCurrencyID { get; set; } = Currency.None;
            public int CostCurrencyValue { get; set; } = 0;
        }

        public const string Filename = "edt_petpromotion.bytes";
        public const TableType Type = TableType.TablePetPromotion;
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
                row.FromGrade = (ItemGradeType)reader.ReadInt32();
                row.ToGrade = (ItemGradeType)reader.ReadInt32();
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
