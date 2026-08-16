using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Option
    {
        public class Row {
            public Option ID { get; set; } = Option.None;
            public OptionTypes OptionType { get; set; } = OptionTypes.None;
            public string OptionTarget { get; set; } = string.Empty;
        }

        public const string Filename = "edt_option.bytes";
        public const TableType Type = TableType.TableOption;
        static Dictionary<Option, Row> _all = new Dictionary<Option, Row>();

        public static Row Get( Option id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Option, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Option)reader.ReadInt32();
                row.OptionType = (OptionTypes)reader.ReadInt32();
                row.OptionTarget = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
