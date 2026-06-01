using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_BuffInfo
    {
        public class Row {
            public BuffInfo ID { get; set; } = BuffInfo.None;
            public string Name { get; set; } = string.Empty;
            public bool IsDebuff { get; set; } = false;
            public string RootVFX { get; set; } = string.Empty;
            public string RootSFX { get; set; } = string.Empty;
            public SkillEffect Effect { get; set; } = SkillEffect.None;
            public SkillEffect IntervalEffect { get; set; } = SkillEffect.None;
        }

        public const string Filename = "edt_buffinfo.bytes";
        public const TableType Type = TableType.TableBuffInfo;
        static Dictionary<BuffInfo, Row> _all = new Dictionary<BuffInfo, Row>();

        public static Row Get( BuffInfo id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<BuffInfo, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (BuffInfo)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.IsDebuff = reader.ReadBoolean();
                row.RootVFX = reader.ReadString();
                row.RootSFX = reader.ReadString();
                row.Effect = (SkillEffect)reader.ReadInt32();
                row.IntervalEffect = (SkillEffect)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
