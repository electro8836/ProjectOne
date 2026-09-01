using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Skill
    {
        public class Row {
            public Skill ID { get; set; } = Skill.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public SkillCategoryTypes SkillCategory { get; set; } = SkillCategoryTypes.None;
            public string AnimName { get; set; } = string.Empty;
            public float AnimLength { get; set; } = 0f;
            public SkillCastingTypes CastingType { get; set; } = SkillCastingTypes.None;
            public float CastingParam { get; set; } = 0f;
            public SkillApplyTarget ApplyTarget { get; set; } = SkillApplyTarget.None;
            public SkillScanTypes ScanType { get; set; } = SkillScanTypes.None;
            public float ScanRange { get; set; } = 0f;
            public float ScanParam { get; set; } = 0f;
            public float Cooldown { get; set; } = 0f;
            public SkillEffect EffectID_01 { get; set; } = SkillEffect.None;
            public SkillEffect EffectID_02 { get; set; } = SkillEffect.None;
            public string SkillVFX { get; set; } = string.Empty;
            public bool SkillVFXFacing { get; set; } = false;
            public float SkillVFXOffset { get; set; } = 0f;
            public string SkillSFX { get; set; } = string.Empty;
            public float BreakDamage { get; set; } = 0f;
        }

        public const string Filename = "edt_skill.bytes";
        public const TableType Type = TableType.TableSkill;
        static Dictionary<Skill, Row> _all = new Dictionary<Skill, Row>();

        public static Row Get( Skill id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Skill, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Skill)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.SkillCategory = (SkillCategoryTypes)reader.ReadInt32();
                row.AnimName = reader.ReadString();
                row.AnimLength = reader.ReadSingle();
                row.CastingType = (SkillCastingTypes)reader.ReadInt32();
                row.CastingParam = reader.ReadSingle();
                row.ApplyTarget = (SkillApplyTarget)reader.ReadInt32();
                row.ScanType = (SkillScanTypes)reader.ReadInt32();
                row.ScanRange = reader.ReadSingle();
                row.ScanParam = reader.ReadSingle();
                row.Cooldown = reader.ReadSingle();
                row.EffectID_01 = (SkillEffect)reader.ReadInt32();
                row.EffectID_02 = (SkillEffect)reader.ReadInt32();
                row.SkillVFX = reader.ReadString();
                row.SkillVFXFacing = reader.ReadBoolean();
                row.SkillVFXOffset = reader.ReadSingle();
                row.SkillSFX = reader.ReadString();
                row.BreakDamage = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
