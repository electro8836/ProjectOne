using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_MasterySkill
    {
        public class Row {
            public int ID { get; set; } = 0;
            public WeaponMastery WeaponMastery { get; set; } = WeaponMastery.None;
            public int UnlockLevel { get; set; } = 0;
            public Skill SkillID { get; set; } = Skill.None;
        }

        public const string Filename = "edt_masteryskill.bytes";
        public const TableType Type = TableType.TableMasterySkill;
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
                row.WeaponMastery = (WeaponMastery)reader.ReadInt32();
                row.UnlockLevel = reader.ReadInt32();
                row.SkillID = (Skill)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
