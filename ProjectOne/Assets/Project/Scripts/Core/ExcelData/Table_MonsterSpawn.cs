using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MonsterSpawn
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public int MonsterID { get; set; } = 0;
            public string SpawnPoint { get; set; } = string.Empty;
            public int SpawnAllCount { get; set; } = 0;
            public int SpawnStepCount { get; set; } = 0;
            public float SpawnStepDelay { get; set; } = 0f;
        }

        public const string Filename = "edt_monsterspawn.bytes";
        public const TableType Type = TableType.TableMonsterSpawn;
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
                row.MonsterID = reader.ReadInt32();
                row.SpawnPoint = reader.ReadString();
                row.SpawnAllCount = reader.ReadInt32();
                row.SpawnStepCount = reader.ReadInt32();
                row.SpawnStepDelay = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
