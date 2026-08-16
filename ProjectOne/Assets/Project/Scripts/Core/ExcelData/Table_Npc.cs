using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Npc
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public NpcType NpcType { get; set; } = NpcType.None;
            public int FunctionID { get; set; } = 0;
            public string PrefabName { get; set; } = string.Empty;
            public string Portrait { get; set; } = string.Empty;
        }

        public const string Filename = "edt_npc.bytes";
        public const TableType Type = TableType.TableNpc;
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
                row.Title = reader.ReadString();
                row.NpcType = (NpcType)reader.ReadInt32();
                row.FunctionID = reader.ReadInt32();
                row.PrefabName = reader.ReadString();
                row.Portrait = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
