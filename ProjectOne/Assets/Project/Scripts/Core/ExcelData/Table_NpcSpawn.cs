using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_NpcSpawn
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int NpcID { get; set; } = 0;
            public int MapID { get; set; } = 0;
            public int SpawnPointID { get; set; } = 0;
            public int SpawnStartQuestID { get; set; } = 0;
            public bool UseSpawnEnd { get; set; } = false;
            public int SpawnEndQuestID { get; set; } = 0;
        }

        public const string Filename = "edt_npcspawn.bytes";
        public const TableType Type = TableType.TableNpcSpawn;
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
                row.MapID = reader.ReadInt32();
                row.SpawnPointID = reader.ReadInt32();
                row.SpawnStartQuestID = reader.ReadInt32();
                row.UseSpawnEnd = reader.ReadBoolean();
                row.SpawnEndQuestID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
