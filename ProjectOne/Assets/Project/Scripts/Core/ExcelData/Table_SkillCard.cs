using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillCard
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public SkillCardGrade GroupGrade { get; set; } = SkillCardGrade.None;
            public SkillInfo Skill_1 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_2 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_3 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_4 { get; set; } = SkillInfo.None;
            public SkillInfo Skill_5 { get; set; } = SkillInfo.None;
            public SkillInfo UltimateSkill { get; set; } = SkillInfo.None;
        }

        public const string Filename = "edt_skillcard.bytes";
        public const TableType Type = TableType.TableSkillCard;
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
                row.Icon = reader.ReadString();
                row.GroupGrade = (SkillCardGrade)reader.ReadInt32();
                row.Skill_1 = (SkillInfo)reader.ReadInt32();
                row.Skill_2 = (SkillInfo)reader.ReadInt32();
                row.Skill_3 = (SkillInfo)reader.ReadInt32();
                row.Skill_4 = (SkillInfo)reader.ReadInt32();
                row.Skill_5 = (SkillInfo)reader.ReadInt32();
                row.UltimateSkill = (SkillInfo)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
