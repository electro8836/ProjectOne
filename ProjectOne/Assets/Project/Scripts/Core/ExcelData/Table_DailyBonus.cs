using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_DailyBonus
    {
        public class Row {
            public int ID { get; set; } = 0;
            public DailyBonusType DailyBonusType { get; set; } = DailyBonusType.None;
            public int DayCount { get; set; } = 0;
            public int RewardGroupID { get; set; } = 0;
            public bool IsDisabled { get; set; } = false;
        }

        public const string Filename = "edt_dailybonus.bytes";
        public const TableType Type = TableType.TableDailyBonus;
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
                row.DailyBonusType = (DailyBonusType)reader.ReadInt32();
                row.DayCount = reader.ReadInt32();
                row.RewardGroupID = reader.ReadInt32();
                row.IsDisabled = reader.ReadBoolean();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
