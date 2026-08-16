using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_NpcDialog
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int NpcID { get; set; } = 0;
            public int QuestID { get; set; } = 0;
            public DialogTriggerType TriggerType { get; set; } = DialogTriggerType.None;
            public string Text { get; set; } = string.Empty;
        }

        public const string Filename = "edt_npcdialog.bytes";
        public const TableType Type = TableType.TableNpcDialog;
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
                row.NpcID = reader.ReadInt32();
                row.QuestID = reader.ReadInt32();
                row.TriggerType = (DialogTriggerType)reader.ReadInt32();
                row.Text = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
