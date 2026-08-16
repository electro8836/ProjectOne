using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipOption
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public ItemGradeType ItemGrade { get; set; } = ItemGradeType.None;
            public Option Opt1_ID { get; set; } = Option.None;
            public float Opt1_Val { get; set; } = 0f;
            public float Opt1_Step { get; set; } = 0f;
            public Option Opt2_ID { get; set; } = Option.None;
            public float Opt2_Val { get; set; } = 0f;
            public float Opt2_Step { get; set; } = 0f;
            public Option Opt3_ID { get; set; } = Option.None;
            public float Opt3_Val { get; set; } = 0f;
            public float Opt3_Step { get; set; } = 0f;
            public Option Opt4_ID { get; set; } = Option.None;
            public float Opt4_Val { get; set; } = 0f;
            public float Opt4_Step { get; set; } = 0f;
            public Option UnlockOpt_ID { get; set; } = Option.None;
            public float UnlockOpt_MinVal { get; set; } = 0f;
            public float UnlockOpt_MaxVal { get; set; } = 0f;
        }

        public const string Filename = "edt_equipoption.bytes";
        public const TableType Type = TableType.TableEquipOption;
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
                row.GroupID = reader.ReadInt32();
                row.ItemGrade = (ItemGradeType)reader.ReadInt32();
                row.Opt1_ID = (Option)reader.ReadInt32();
                row.Opt1_Val = reader.ReadSingle();
                row.Opt1_Step = reader.ReadSingle();
                row.Opt2_ID = (Option)reader.ReadInt32();
                row.Opt2_Val = reader.ReadSingle();
                row.Opt2_Step = reader.ReadSingle();
                row.Opt3_ID = (Option)reader.ReadInt32();
                row.Opt3_Val = reader.ReadSingle();
                row.Opt3_Step = reader.ReadSingle();
                row.Opt4_ID = (Option)reader.ReadInt32();
                row.Opt4_Val = reader.ReadSingle();
                row.Opt4_Step = reader.ReadSingle();
                row.UnlockOpt_ID = (Option)reader.ReadInt32();
                row.UnlockOpt_MinVal = reader.ReadSingle();
                row.UnlockOpt_MaxVal = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
