using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipGradeWeight
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Desc { get; set; } = string.Empty;
            public int Normal { get; set; } = 0;
            public int Magic { get; set; } = 0;
            public int Rare { get; set; } = 0;
            public int Epic { get; set; } = 0;
            public int Legendary { get; set; } = 0;
            public int Mythic { get; set; } = 0;
        }

        public const string Filename = "edt_equipgradeweight.bytes";
        public const TableType Type = TableType.TableEquipGradeWeight;
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
                row.Desc = reader.ReadString();
                row.Normal = reader.ReadInt32();
                row.Magic = reader.ReadInt32();
                row.Rare = reader.ReadInt32();
                row.Epic = reader.ReadInt32();
                row.Legendary = reader.ReadInt32();
                row.Mythic = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
