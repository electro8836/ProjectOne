using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillPoint
    {
        public class Row {
            public SkillPoint ID { get; set; } = SkillPoint.None;
            public int MaxPoint { get; set; } = 0;
            public string Desc { get; set; } = string.Empty;
            public bool IsShared { get; set; } = false;
            public SkillPointSourceType SourceType { get; set; } = SkillPointSourceType.None;
        }

        public const string Filename = "edt_skillpoint.bytes";
        public const TableType Type = TableType.TableSkillPoint;
        static Dictionary<SkillPoint, Row> _all = new Dictionary<SkillPoint, Row>();

        public static Row Get( SkillPoint id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<SkillPoint, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (SkillPoint)reader.ReadInt32();
                row.MaxPoint = reader.ReadInt32();
                row.Desc = reader.ReadString();
                row.IsShared = reader.ReadBoolean();
                row.SourceType = (SkillPointSourceType)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
