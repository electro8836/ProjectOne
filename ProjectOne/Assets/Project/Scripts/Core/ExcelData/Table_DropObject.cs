using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DropObject
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public int GroupID { get; set; } = 0;
            public DropObjectType DropObjectType { get; set; } = DropObjectType.None;
            public string Path { get; set; } = string.Empty;
            public MonsterTypes MonsterType { get; set; } = MonsterTypes.None;
            public float DropChance { get; set; } = 0f;
            public int MinCount { get; set; } = 0;
            public int MaxCount { get; set; } = 0;
        }

        public const string Filename = "edt_dropobject.bytes";
        public const TableType Type = TableType.TableDropObject;
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
                row.GroupID = reader.ReadInt32();
                row.DropObjectType = (DropObjectType)reader.ReadInt32();
                row.Path = reader.ReadString();
                row.MonsterType = (MonsterTypes)reader.ReadInt32();
                row.DropChance = reader.ReadSingle();
                row.MinCount = reader.ReadInt32();
                row.MaxCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
