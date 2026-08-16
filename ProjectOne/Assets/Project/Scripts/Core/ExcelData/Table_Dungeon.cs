using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Dungeon
    {
        public class Row {
            public Dungeon ID { get; set; } = Dungeon.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public int DefaultEnterCount { get; set; } = 0;
            public int MaxEnterCount { get; set; } = 0;
            public Currency RevivalCostType { get; set; } = Currency.None;
            public int RevivalCost { get; set; } = 0;
            public int RevivalCostStep { get; set; } = 0;
            public int MaxRevivalCount { get; set; } = 0;
        }

        public const string Filename = "edt_dungeon.bytes";
        public const TableType Type = TableType.TableDungeon;
        static Dictionary<Dungeon, Row> _all = new Dictionary<Dungeon, Row>();

        public static Row Get( Dungeon id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Dungeon, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Dungeon)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.DefaultEnterCount = reader.ReadInt32();
                row.MaxEnterCount = reader.ReadInt32();
                row.RevivalCostType = (Currency)reader.ReadInt32();
                row.RevivalCost = reader.ReadInt32();
                row.RevivalCostStep = reader.ReadInt32();
                row.MaxRevivalCount = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
