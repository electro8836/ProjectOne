using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_AuraInfo
    {
        public class Row {
            public AuraInfo ID { get; set; } = AuraInfo.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string AttachedVFV { get; set; } = string.Empty;
            public SkillEffect Effect_1 { get; set; } = SkillEffect.None;
            public float Effect1_Interval { get; set; } = 0f;
            public SkillEffect Effect_2 { get; set; } = SkillEffect.None;
            public float Effect2_Interval { get; set; } = 0f;
            public SkillEffect Effect_3 { get; set; } = SkillEffect.None;
            public float Effect3_Interval { get; set; } = 0f;
            public SkillEffect Effect_4 { get; set; } = SkillEffect.None;
            public float Effect4_Interval { get; set; } = 0f;
        }

        public const string Filename = "edt_aurainfo.bytes";
        public const TableType Type = TableType.TableAuraInfo;
        static Dictionary<AuraInfo, Row> _all = new Dictionary<AuraInfo, Row>();

        public static Row Get( AuraInfo id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<AuraInfo, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (AuraInfo)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.AttachedVFV = reader.ReadString();
                row.Effect_1 = (SkillEffect)reader.ReadInt32();
                row.Effect1_Interval = reader.ReadSingle();
                row.Effect_2 = (SkillEffect)reader.ReadInt32();
                row.Effect2_Interval = reader.ReadSingle();
                row.Effect_3 = (SkillEffect)reader.ReadInt32();
                row.Effect3_Interval = reader.ReadSingle();
                row.Effect_4 = (SkillEffect)reader.ReadInt32();
                row.Effect4_Interval = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
