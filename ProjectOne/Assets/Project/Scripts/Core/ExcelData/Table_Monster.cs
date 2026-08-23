using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Monster
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public MonsterType MonsterType { get; set; } = MonsterType.None;
            public string PrefabPath { get; set; } = string.Empty;
            public int StatGroupID { get; set; } = 0;
            public MonsterAIType AIType { get; set; } = MonsterAIType.None;
            public AggroType AggroType { get; set; } = AggroType.None;
            public float DetectRange { get; set; } = 0f;
            public float LeashRange { get; set; } = 0f;
            public Skill[] SkillIDs { get; set; } = Array.Empty<Skill>();
            public int BaseExp { get; set; } = 0;
            public int PerLevelExp { get; set; } = 0;
            public int RewardGroupID { get; set; } = 0;
        }

        public const string Filename = "edt_monster.bytes";
        public const TableType Type = TableType.TableMonster;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.MonsterType = (MonsterType)reader.ReadInt32();
                row.PrefabPath = reader.ReadString();
                row.StatGroupID = reader.ReadInt32();
                row.AIType = (MonsterAIType)reader.ReadInt32();
                row.AggroType = (AggroType)reader.ReadInt32();
                row.DetectRange = reader.ReadSingle();
                row.LeashRange = reader.ReadSingle();
                { int _n = reader.ReadInt32(); row.SkillIDs = new Skill[_n]; for(int _i=0;_i<_n;_i++) row.SkillIDs[_i] = (Skill)reader.ReadInt32(); }
                row.BaseExp = reader.ReadInt32();
                row.PerLevelExp = reader.ReadInt32();
                row.RewardGroupID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
