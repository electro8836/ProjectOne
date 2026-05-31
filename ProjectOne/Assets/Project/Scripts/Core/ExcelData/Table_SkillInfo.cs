using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillInfo
    {
        public class Row {
            public SkillInfo ID { get; set; } = SkillInfo.None;
            public string Name { get; set; } = string.Empty;
            public float CooltimeSec { get; set; } = 0f;
            public SkillCastingTypes CastingType { get; set; } = SkillCastingTypes.None;
            public int CastingParam { get; set; } = 0;
            public SkillScanType ScanType { get; set; } = SkillScanType.None;
            public float ScanParam1 { get; set; } = 0f;
            public float ScanParam2 { get; set; } = 0f;
            public string MotionName { get; set; } = string.Empty;
            public float MotionEffectTime { get; set; } = 0f;
            public bool IsOnHitTrigger { get; set; } = false;
            public string SkillVFX { get; set; } = string.Empty;
            public SkillEffect StartEffect { get; set; } = SkillEffect.None;
            public SkillEffect Effect_0 { get; set; } = SkillEffect.None;
            public SkillEffect Effect_1 { get; set; } = SkillEffect.None;
            public SkillEffect Effect_2 { get; set; } = SkillEffect.None;
            public SkillEffect Effect_3 { get; set; } = SkillEffect.None;
            public SkillEffect Effect_4 { get; set; } = SkillEffect.None;
            public SkillEffect FinishEffect { get; set; } = SkillEffect.None;
        }

        public const string Filename = "edt_skillinfo.bytes";
        public const TableType Type = TableType.TableSkillInfo;
        static Dictionary<SkillInfo, Row> _all = new Dictionary<SkillInfo, Row>();

        public static Row Get( SkillInfo id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<SkillInfo, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (SkillInfo)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.CooltimeSec = reader.ReadSingle();
                row.CastingType = (SkillCastingTypes)reader.ReadInt32();
                row.CastingParam = reader.ReadInt32();
                row.ScanType = (SkillScanType)reader.ReadInt32();
                row.ScanParam1 = reader.ReadSingle();
                row.ScanParam2 = reader.ReadSingle();
                row.MotionName = reader.ReadString();
                row.MotionEffectTime = reader.ReadSingle();
                row.IsOnHitTrigger = reader.ReadBoolean();
                row.SkillVFX = reader.ReadString();
                row.StartEffect = (SkillEffect)reader.ReadInt32();
                row.Effect_0 = (SkillEffect)reader.ReadInt32();
                row.Effect_1 = (SkillEffect)reader.ReadInt32();
                row.Effect_2 = (SkillEffect)reader.ReadInt32();
                row.Effect_3 = (SkillEffect)reader.ReadInt32();
                row.Effect_4 = (SkillEffect)reader.ReadInt32();
                row.FinishEffect = (SkillEffect)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
