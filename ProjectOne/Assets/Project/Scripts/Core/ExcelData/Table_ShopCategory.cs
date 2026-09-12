using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_ShopCategory
    {
        public class Row {
            public ShopCategory ID { get; set; } = ShopCategory.None;
            public string Name { get; set; } = string.Empty;
            public int Order { get; set; } = 0;
            public bool IsEnable { get; set; } = false;
        }

        public const string Filename = "edt_shopcategory.bytes";
        public const TableType Type = TableType.TableShopCategory;
        static Dictionary<ShopCategory, Row> _all = new Dictionary<ShopCategory, Row>();

        public static Row Get( ShopCategory id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<ShopCategory, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (ShopCategory)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Order = reader.ReadInt32();
                row.IsEnable = reader.ReadBoolean();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
