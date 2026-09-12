using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ShopGoods
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int GroupID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Discount { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public GoodsType GoodsType { get; set; } = GoodsType.None;
            public int RewardGroupID { get; set; } = 0;
            public PriceType PriceType { get; set; } = PriceType.None;
            public string PriceParam { get; set; } = string.Empty;
            public int Price { get; set; } = 0;
            public int MaxPurchaseCount { get; set; } = 0;
            public bool UseDailyReset { get; set; } = false;
        }

        public const string Filename = "edt_shopgoods.bytes";
        public const TableType Type = TableType.TableShopGoods;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Discount = reader.ReadString();
                row.Icon = reader.ReadString();
                row.GoodsType = (GoodsType)reader.ReadInt32();
                row.RewardGroupID = reader.ReadInt32();
                row.PriceType = (PriceType)reader.ReadInt32();
                row.PriceParam = reader.ReadString();
                row.Price = reader.ReadInt32();
                row.MaxPurchaseCount = reader.ReadInt32();
                row.UseDailyReset = reader.ReadBoolean();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
