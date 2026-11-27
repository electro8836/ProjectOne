using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ForgeReforge
    {
        public class Row {
            public int ID { get; set; } = 0;
            public ItemGradeType Grade { get; set; } = ItemGradeType.None;
            public float MinRise { get; set; } = 0f;
            public float MaxRise { get; set; } = 0f;
            public Currency Cur1_ID { get; set; } = Currency.None;
            public int Cur1_Count { get; set; } = 0;
            public Currency Cur2_ID { get; set; } = Currency.None;
            public int Cur2_Count { get; set; } = 0;
        }

        public const string Filename = "edt_forgereforge.bytes";
        public const TableType Type = TableType.TableForgeReforge;
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
                row.Grade = (ItemGradeType)reader.ReadInt32();
                row.MinRise = reader.ReadSingle();
                row.MaxRise = reader.ReadSingle();
                row.Cur1_ID = (Currency)reader.ReadInt32();
                row.Cur1_Count = reader.ReadInt32();
                row.Cur2_ID = (Currency)reader.ReadInt32();
                row.Cur2_Count = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
