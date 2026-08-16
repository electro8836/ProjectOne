using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_EquipEnhanceTier
    {
        public class Row {
            public EquipEnhanceTier ID { get; set; } = EquipEnhanceTier.None;
            public ItemGradeType Grade { get; set; } = ItemGradeType.None;
            public int MinLevel { get; set; } = 0;
            public int MaxLevel { get; set; } = 0;
        }

        public const string Filename = "edt_equipenhancetier.bytes";
        public const TableType Type = TableType.TableEquipEnhanceTier;
        static Dictionary<EquipEnhanceTier, Row> _all = new Dictionary<EquipEnhanceTier, Row>();

        public static Row Get( EquipEnhanceTier id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<EquipEnhanceTier, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (EquipEnhanceTier)reader.ReadInt32();
                row.Grade = (ItemGradeType)reader.ReadInt32();
                row.MinLevel = reader.ReadInt32();
                row.MaxLevel = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
