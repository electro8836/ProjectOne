using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Reward
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public RewardTypes RewardType { get; set; } = RewardTypes.None;
            public float Probability { get; set; } = 0f;
            public int MinCount { get; set; } = 0;
            public int MaxCount { get; set; } = 0;
            public int[] SourceIDs { get; set; } = Array.Empty<int>();
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.RewardType = (RewardTypes)reader.ReadInt32();
                row.Probability = reader.ReadSingle();
                row.MinCount = reader.ReadInt32();
                row.MaxCount = reader.ReadInt32();
                { int _n = reader.ReadInt32(); row.SourceIDs = new int[_n]; for(int _i=0;_i<_n;_i++) row.SourceIDs[_i] = reader.ReadInt32(); }
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
