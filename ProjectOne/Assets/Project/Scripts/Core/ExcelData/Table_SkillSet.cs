using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillSet
    {
        public class Row {
            public int ID { get; set; } = 0;
            public SkillInfo BaseAttackSkill { get; set; } = SkillInfo.None;
            public SkillInfo Skill_1 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_2 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_3 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_4 { get; set; } = SkillInfo.None;
        }

        public const string Filename = "edt_skillset.bytes";
        public const TableType Type = TableType.TableSkillSet;
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
                row.BaseAttackSkill = (SkillInfo)reader.ReadInt32();
                row.Skill_1 = (SkillInfo)reader.ReadInt32();
                row.Skill_2 = (SkillInfo)reader.ReadInt32();
                row.Skill_3 = (SkillInfo)reader.ReadInt32();
                row.Skill_4 = (SkillInfo)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
