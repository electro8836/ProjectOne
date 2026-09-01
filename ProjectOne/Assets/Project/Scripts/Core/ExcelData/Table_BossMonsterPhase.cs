using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_BossMonsterPhase
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int MonsterID { get; set; } = 0;
            public int PhaseOrder { get; set; } = 0;
            public float HpThreshold { get; set; } = 0f;
            public int SkillSetGroupID { get; set; } = 0;
            public Skill PhaseSkillID { get; set; } = Skill.None;
            public int GimmickCount { get; set; } = 0;
            public int GimmickRequired { get; set; } = 0;
            public float GimmickRadius { get; set; } = 0f;
        }

        public const string Filename = "edt_bossmonsterphase.bytes";
        public const TableType Type = TableType.TableBossMonsterPhase;
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
                row.MonsterID = reader.ReadInt32();
                row.PhaseOrder = reader.ReadInt32();
                row.HpThreshold = reader.ReadSingle();
                row.SkillSetGroupID = reader.ReadInt32();
                row.PhaseSkillID = (Skill)reader.ReadInt32();
                row.GimmickCount = reader.ReadInt32();
                row.GimmickRequired = reader.ReadInt32();
                row.GimmickRadius = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
