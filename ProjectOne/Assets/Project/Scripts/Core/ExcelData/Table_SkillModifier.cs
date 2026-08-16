using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillModifier
    {
        public class Row {
            public SkillModifier ID { get; set; } = SkillModifier.None;
            public SkillModifierScope Scope { get; set; } = SkillModifierScope.None;
            public Skill Scope_Skill { get; set; } = Skill.None;
            public SkillEffect Scope_Effect { get; set; } = SkillEffect.None;
            public Projectile Scope_Projectile { get; set; } = Projectile.None;
            public Summon Scope_Summon { get; set; } = Summon.None;
            public Buff Scope_Buff { get; set; } = Buff.None;
            public string ParamKey { get; set; } = string.Empty;
            public ModifierOperator Operator { get; set; } = ModifierOperator.None;
            public float Value { get; set; } = 0f;
            public string RefValue { get; set; } = string.Empty;
        }

        public const string Filename = "edt_skillmodifier.bytes";
        public const TableType Type = TableType.TableSkillModifier;
        static Dictionary<SkillModifier, Row> _all = new Dictionary<SkillModifier, Row>();

        public static Row Get( SkillModifier id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<SkillModifier, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (SkillModifier)reader.ReadInt32();
                row.Scope = (SkillModifierScope)reader.ReadInt32();
                row.Scope_Skill = (Skill)reader.ReadInt32();
                row.Scope_Effect = (SkillEffect)reader.ReadInt32();
                row.Scope_Projectile = (Projectile)reader.ReadInt32();
                row.Scope_Summon = (Summon)reader.ReadInt32();
                row.Scope_Buff = (Buff)reader.ReadInt32();
                row.ParamKey = reader.ReadString();
                row.Operator = (ModifierOperator)reader.ReadInt32();
                row.Value = reader.ReadSingle();
                row.RefValue = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
