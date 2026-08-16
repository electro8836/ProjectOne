using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Summon
    {
        public class Row {
            public Summon ID { get; set; } = Summon.None;
            public string PrefabPath { get; set; } = string.Empty;
            public bool ScaleByRadius { get; set; } = false;
            public SummonAIType AIType { get; set; } = SummonAIType.None;
            public bool DieWithOwner { get; set; } = false;
            public float ReturnDistance { get; set; } = 0f;
            public Skill SkillID_1 { get; set; } = Skill.None;
            public Skill SkillID_2 { get; set; } = Skill.None;
            public float ActionInterval { get; set; } = 0f;
            public float FollowDistance { get; set; } = 0f;
            public Stat InheritStatType_1 { get; set; } = Stat.None;
            public float InheritStatRatio_1 { get; set; } = 0f;
            public Stat InheritStatType_2 { get; set; } = Stat.None;
            public float InheritStatRatio_2 { get; set; } = 0f;
            public int MaxCount { get; set; } = 0;
        }

        public const string Filename = "edt_summon.bytes";
        public const TableType Type = TableType.TableSummon;
        static Dictionary<Summon, Row> _all = new Dictionary<Summon, Row>();

        public static Row Get( Summon id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Summon, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Summon)reader.ReadInt32();
                row.PrefabPath = reader.ReadString();
                row.ScaleByRadius = reader.ReadBoolean();
                row.AIType = (SummonAIType)reader.ReadInt32();
                row.DieWithOwner = reader.ReadBoolean();
                row.ReturnDistance = reader.ReadSingle();
                row.SkillID_1 = (Skill)reader.ReadInt32();
                row.SkillID_2 = (Skill)reader.ReadInt32();
                row.ActionInterval = reader.ReadSingle();
                row.FollowDistance = reader.ReadSingle();
                row.InheritStatType_1 = (Stat)reader.ReadInt32();
                row.InheritStatRatio_1 = reader.ReadSingle();
                row.InheritStatType_2 = (Stat)reader.ReadInt32();
                row.InheritStatRatio_2 = reader.ReadSingle();
                row.MaxCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
