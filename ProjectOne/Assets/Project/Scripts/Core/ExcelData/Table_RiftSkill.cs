using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RiftSkill
    {
        public class Row {
            public int ID { get; set; } = 0;
            public Skill SkillID { get; set; } = Skill.None;
            public int ReqGauge { get; set; } = 0;
            public int MaxGauge { get; set; } = 0;
        }

        public const string Filename = "edt_riftskill.bytes";
        public const TableType Type = TableType.TableRiftSkill;
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
                row.SkillID = (Skill)reader.ReadInt32();
                row.ReqGauge = reader.ReadInt32();
                row.MaxGauge = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
