using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Reward
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public RewardType RewardType { get; set; } = RewardType.None;
            public string TargetID { get; set; } = string.Empty;
            public int EquipGradeWeightID { get; set; } = 0;
            public int MinCount { get; set; } = 0;
            public int MaxCount { get; set; } = 0;
            public float Chance { get; set; } = 0f;
        }

        public const string Filename = "edt_reward.bytes";
        public const TableType Type = TableType.TableReward;
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
                row.GroupID = reader.ReadInt32();
                row.RewardType = (RewardType)reader.ReadInt32();
                row.TargetID = reader.ReadString();
                row.EquipGradeWeightID = reader.ReadInt32();
                row.MinCount = reader.ReadInt32();
                row.MaxCount = reader.ReadInt32();
                row.Chance = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
