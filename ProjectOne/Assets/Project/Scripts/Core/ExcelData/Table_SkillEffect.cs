using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillEffect
    {
        public class Row {
            public SkillEffect ID { get; set; } = SkillEffect.None;
            public SkillApplyTarget ApplyTarget { get; set; } = SkillApplyTarget.None;
            public SkillDamageType DamageType { get; set; } = SkillDamageType.None;
            public string EffectVFX { get; set; } = string.Empty;
            public string EffectSFX { get; set; } = string.Empty;
            public SkillEffectTypes EffectType { get; set; } = SkillEffectTypes.None;
            public string EffectParam_1 { get; set; } = string.Empty;
            public string EffectParam_2 { get; set; } = string.Empty;
            public string EffectParam_3 { get; set; } = string.Empty;
            public string EffectParam_4 { get; set; } = string.Empty;
            public string EffectParam_5 { get; set; } = string.Empty;
            public string EffectParam_6 { get; set; } = string.Empty;
            public string EffectParam_7 { get; set; } = string.Empty;
        }

        public const string Filename = "edt_skilleffect.bytes";
        public const TableType Type = TableType.TableSkillEffect;
        static Dictionary<SkillEffect, Row> _all = new Dictionary<SkillEffect, Row>();

        public static Row Get( SkillEffect id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<SkillEffect, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (SkillEffect)reader.ReadInt32();
                row.ApplyTarget = (SkillApplyTarget)reader.ReadInt32();
                row.DamageType = (SkillDamageType)reader.ReadInt32();
                row.EffectVFX = reader.ReadString();
                row.EffectSFX = reader.ReadString();
                row.EffectType = (SkillEffectTypes)reader.ReadInt32();
                row.EffectParam_1 = reader.ReadString();
                row.EffectParam_2 = reader.ReadString();
                row.EffectParam_3 = reader.ReadString();
                row.EffectParam_4 = reader.ReadString();
                row.EffectParam_5 = reader.ReadString();
                row.EffectParam_6 = reader.ReadString();
                row.EffectParam_7 = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
