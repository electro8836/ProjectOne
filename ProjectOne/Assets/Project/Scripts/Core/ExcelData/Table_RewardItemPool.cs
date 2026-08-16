using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RewardItemPool
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int PoolID { get; set; } = 0;
            public DropTier DropTier { get; set; } = DropTier.None;
            public ItemMainCategory MainCategory { get; set; } = ItemMainCategory.None;
            public ItemSubCategory SubCategory { get; set; } = ItemSubCategory.None;
            public int Weight { get; set; } = 0;
        }

        public const string Filename = "edt_rewarditempool.bytes";
        public const TableType Type = TableType.TableRewardItemPool;
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
                row.PoolID = reader.ReadInt32();
                row.DropTier = (DropTier)reader.ReadInt32();
                row.MainCategory = (ItemMainCategory)reader.ReadInt32();
                row.SubCategory = (ItemSubCategory)reader.ReadInt32();
                row.Weight = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
