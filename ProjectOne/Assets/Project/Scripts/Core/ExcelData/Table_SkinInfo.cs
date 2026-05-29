using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkinInfo
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public CharacterTypes ReqCharacterType { get; set; } = CharacterTypes.None;
            public string Path { get; set; } = string.Empty;
            public StatInfo CollectionStatType { get; set; } = StatInfo.None;
            public int CollectionStatValue { get; set; } = 0;
        }

        public const string Filename = "edt_skininfo.bytes";
        public const TableType Type = TableType.TableSkinInfo;
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
                row.ReqCharacterType = (CharacterTypes)reader.ReadInt32();
                row.Path = reader.ReadString();
                row.CollectionStatType = (StatInfo)reader.ReadInt32();
                row.CollectionStatValue = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
