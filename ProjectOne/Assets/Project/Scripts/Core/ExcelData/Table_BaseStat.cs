using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_BaseStat
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int ATK_Base { get; set; } = 0;
            public int MATK_Base { get; set; } = 0;
            public int CritRate_Base { get; set; } = 0;
            public int CritDam_Base { get; set; } = 0;
            public int MaxHP_Base { get; set; } = 0;
            public int DEF_Base { get; set; } = 0;
            public int MDEF_Base { get; set; } = 0;
            public int HpRecovery_Base { get; set; } = 0;
            public int AtkSpeed_Base { get; set; } = 0;
            public int MoveSpeed_Base { get; set; } = 0;
            public int Accuracy_Base { get; set; } = 0;
            public int BreakGage_Base { get; set; } = 0;
            public int BreakDamage_Base { get; set; } = 0;
            public int BreakRecovery_Base { get; set; } = 0;
            public int Evasion_Base { get; set; } = 0;
            public int Block_Base { get; set; } = 0;
        }

        public const string Filename = "edt_basestat.bytes";
        public const TableType Type = TableType.TableBaseStat;
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
                row.ATK_Base = reader.ReadInt32();
                row.MATK_Base = reader.ReadInt32();
                row.CritRate_Base = reader.ReadInt32();
                row.CritDam_Base = reader.ReadInt32();
                row.MaxHP_Base = reader.ReadInt32();
                row.DEF_Base = reader.ReadInt32();
                row.MDEF_Base = reader.ReadInt32();
                row.HpRecovery_Base = reader.ReadInt32();
                row.AtkSpeed_Base = reader.ReadInt32();
                row.MoveSpeed_Base = reader.ReadInt32();
                row.Accuracy_Base = reader.ReadInt32();
                row.BreakGage_Base = reader.ReadInt32();
                row.BreakDamage_Base = reader.ReadInt32();
                row.BreakRecovery_Base = reader.ReadInt32();
                row.Evasion_Base = reader.ReadInt32();
                row.Block_Base = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
