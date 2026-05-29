using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DungeonList
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public DungeonType DungeonType { get; set; } = DungeonType.None;
            public int TicketItemID { get; set; } = 0;
            public int TicketCost { get; set; } = 0;
            public string UnlockCondition { get; set; } = string.Empty;
            public string MapPrefab { get; set; } = string.Empty;
        }

        public const string Filename = "edt_dungeonlist.bytes";
        public const TableType Type = TableType.TableDungeonList;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.DungeonType = (DungeonType)reader.ReadInt32();
                row.TicketItemID = reader.ReadInt32();
                row.TicketCost = reader.ReadInt32();
                row.UnlockCondition = reader.ReadString();
                row.MapPrefab = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
