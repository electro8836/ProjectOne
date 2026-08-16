using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillEffect
    {
        public class Row {
            public SkillEffect ID { get; set; } = SkillEffect.None;
            public SkillEffectTypes EffectType { get; set; } = SkillEffectTypes.None;
            public SkillEffectOrigin EffectOrigin { get; set; } = SkillEffectOrigin.None;
            public float EffectTime { get; set; } = 0f;
            public string EffectParam_1 { get; set; } = string.Empty;
            public string EffectParam_2 { get; set; } = string.Empty;
            public string EffectParam_3 { get; set; } = string.Empty;
            public string EffectParam_4 { get; set; } = string.Empty;
            public string EffectParam_5 { get; set; } = string.Empty;
            public SkillEffect[] ChainEffectIDs { get; set; } = Array.Empty<SkillEffect>();
            public bool OnHitTrigger { get; set; } = false;
            public string EffectVFX { get; set; } = string.Empty;
            public string EffectSFX { get; set; } = string.Empty;
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
                row.EffectType = (SkillEffectTypes)reader.ReadInt32();
                row.EffectOrigin = (SkillEffectOrigin)reader.ReadInt32();
                row.EffectTime = reader.ReadSingle();
                row.EffectParam_1 = reader.ReadString();
                row.EffectParam_2 = reader.ReadString();
                row.EffectParam_3 = reader.ReadString();
                row.EffectParam_4 = reader.ReadString();
                row.EffectParam_5 = reader.ReadString();
                { int _n = reader.ReadInt32(); row.ChainEffectIDs = new SkillEffect[_n]; for(int _i=0;_i<_n;_i++) row.ChainEffectIDs[_i] = (SkillEffect)reader.ReadInt32(); }
                row.OnHitTrigger = reader.ReadBoolean();
                row.EffectVFX = reader.ReadString();
                row.EffectSFX = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
