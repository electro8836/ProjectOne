using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Reward_Material
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int MaterialSourceID { get; set; } = 0;
            public MaterialType MaterialType { get; set; } = MaterialType.None;
            public MaterialGradeType MaterialGrade { get; set; } = MaterialGradeType.None;
            public int Weight { get; set; } = 0;
        }

        public const string Filename = "edt_reward_material.bytes";
        public const TableType Type = TableType.TableReward_Material;
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
                row.MaterialSourceID = reader.ReadInt32();
                row.MaterialType = (MaterialType)reader.ReadInt32();
                row.MaterialGrade = (MaterialGradeType)reader.ReadInt32();
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
