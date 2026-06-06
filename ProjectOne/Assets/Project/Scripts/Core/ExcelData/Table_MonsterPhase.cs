using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MonsterPhase
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public int PhaseCount { get; set; } = 0;
            public int Phase1_Hp { get; set; } = 0;
            public int Phase1_SkillSet { get; set; } = 0;
            public SkillInfo Phase1_to_2_TransitionSkill { get; set; } = SkillInfo.None;
            public int Phase2_Hp { get; set; } = 0;
            public int Phase2_SkillSet { get; set; } = 0;
            public SkillInfo Phase2_to_3_TransitionSkill { get; set; } = SkillInfo.None;
            public int Phase3_Hp { get; set; } = 0;
            public int Phase3_SkillSet { get; set; } = 0;
            public SkillInfo Phase3_to_4_TransitionSkill { get; set; } = SkillInfo.None;
            public int Phase4_Hp { get; set; } = 0;
            public int Phase4_SkillSet { get; set; } = 0;
        }

        public const string Filename = "edt_monsterphase.bytes";
        public const TableType Type = TableType.TableMonsterPhase;
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
                row.PhaseCount = reader.ReadInt32();
                row.Phase1_Hp = reader.ReadInt32();
                row.Phase1_SkillSet = reader.ReadInt32();
                row.Phase1_to_2_TransitionSkill = (SkillInfo)reader.ReadInt32();
                row.Phase2_Hp = reader.ReadInt32();
                row.Phase2_SkillSet = reader.ReadInt32();
                row.Phase2_to_3_TransitionSkill = (SkillInfo)reader.ReadInt32();
                row.Phase3_Hp = reader.ReadInt32();
                row.Phase3_SkillSet = reader.ReadInt32();
                row.Phase3_to_4_TransitionSkill = (SkillInfo)reader.ReadInt32();
                row.Phase4_Hp = reader.ReadInt32();
                row.Phase4_SkillSet = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
