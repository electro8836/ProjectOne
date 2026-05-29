using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_UIWidgetPath
    {
        public class Row {
            public UIWidgetPath ID { get; set; } = UIWidgetPath.None;
            public string Path { get; set; } = string.Empty;
            public UIZOrder ZOrder { get; set; } = UIZOrder.None;
            public bool Focusable { get; set; } = false;
        }

        public const string Filename = "edt_uiwidgetpath.bytes";
        public const TableType Type = TableType.TableUIWidgetPath;
        static Dictionary<UIWidgetPath, Row> _all = new Dictionary<UIWidgetPath, Row>();

        public static Row Get( UIWidgetPath id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<UIWidgetPath, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (UIWidgetPath)reader.ReadInt32();
                row.Path = reader.ReadString();
                row.ZOrder = (UIZOrder)reader.ReadInt32();
                row.Focusable = reader.ReadBoolean();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
