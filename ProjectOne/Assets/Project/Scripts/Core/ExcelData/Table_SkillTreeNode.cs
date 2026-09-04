using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillTreeNode
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public int NodePos_Row { get; set; } = 0;
            public int NodePos_Column { get; set; } = 0;
            public int PrevNodeID { get; set; } = 0;
            public int RequireTreePoint { get; set; } = 0;
            public Option Option { get; set; } = Option.None;
            public float BaseValue { get; set; } = 0f;
            public float PerLevelValue { get; set; } = 0f;
            public int MaxLevel { get; set; } = 0;
        }

        public const string Filename = "edt_skilltreenode.bytes";
        public const TableType Type = TableType.TableSkillTreeNode;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.NodePos_Row = reader.ReadInt32();
                row.NodePos_Column = reader.ReadInt32();
                row.PrevNodeID = reader.ReadInt32();
                row.RequireTreePoint = reader.ReadInt32();
                row.Option = (Option)reader.ReadInt32();
                row.BaseValue = reader.ReadSingle();
                row.PerLevelValue = reader.ReadSingle();
                row.MaxLevel = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
