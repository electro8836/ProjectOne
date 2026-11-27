using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ForgeRecipe
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int SourceItemID { get; set; } = 0;
            public int ResultItemID { get; set; } = 0;
            public int ResultCount { get; set; } = 0;
            public ItemGradeType ResultGrade { get; set; } = ItemGradeType.None;
            public int Mat1_ID { get; set; } = 0;
            public int Mat1_Count { get; set; } = 0;
            public int Mat2_ID { get; set; } = 0;
            public int Mat2_Count { get; set; } = 0;
            public int Mat3_ID { get; set; } = 0;
            public int Mat3_Count { get; set; } = 0;
            public int Mat4_ID { get; set; } = 0;
            public int Mat4_Count { get; set; } = 0;
            public Currency Cur1_ID { get; set; } = Currency.None;
            public int Cur1_Count { get; set; } = 0;
            public Currency Cur2_ID { get; set; } = Currency.None;
            public int Cur2_Count { get; set; } = 0;
            public int SuccessRate { get; set; } = 0;
            public int FailBonus { get; set; } = 0;
        }

        public const string Filename = "edt_forgerecipe.bytes";
        public const TableType Type = TableType.TableForgeRecipe;
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
                row.SourceItemID = reader.ReadInt32();
                row.ResultItemID = reader.ReadInt32();
                row.ResultCount = reader.ReadInt32();
                row.ResultGrade = (ItemGradeType)reader.ReadInt32();
                row.Mat1_ID = reader.ReadInt32();
                row.Mat1_Count = reader.ReadInt32();
                row.Mat2_ID = reader.ReadInt32();
                row.Mat2_Count = reader.ReadInt32();
                row.Mat3_ID = reader.ReadInt32();
                row.Mat3_Count = reader.ReadInt32();
                row.Mat4_ID = reader.ReadInt32();
                row.Mat4_Count = reader.ReadInt32();
                row.Cur1_ID = (Currency)reader.ReadInt32();
                row.Cur1_Count = reader.ReadInt32();
                row.Cur2_ID = (Currency)reader.ReadInt32();
                row.Cur2_Count = reader.ReadInt32();
                row.SuccessRate = reader.ReadInt32();
                row.FailBonus = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
