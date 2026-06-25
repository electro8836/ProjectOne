using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillCardUpgrade
    {
        public class Row {
            public int ID { get; set; } = 0;
            public SkillGroupGrade SourceGrade { get; set; } = SkillGroupGrade.None;
            public SkillGroupGrade TargetGrade { get; set; } = SkillGroupGrade.None;
            public int RequiredCount { get; set; } = 0;
        }

        public const string Filename = "edt_skillcardupgrade.bytes";
        public const TableType Type = TableType.TableSkillCardUpgrade;
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
                row.SourceGrade = (SkillGroupGrade)reader.ReadInt32();
                row.TargetGrade = (SkillGroupGrade)reader.ReadInt32();
                row.RequiredCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
