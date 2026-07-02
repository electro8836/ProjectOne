using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Material
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public MaterialType MaterialType { get; set; } = MaterialType.None;
            public MaterialGradeType MaterialGrade { get; set; } = MaterialGradeType.None;
            public string Navigation { get; set; } = string.Empty;
        }

        public const string Filename = "edt_material.bytes";
        public const TableType Type = TableType.TableMaterial;
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
                row.MaterialType = (MaterialType)reader.ReadInt32();
                row.MaterialGrade = (MaterialGradeType)reader.ReadInt32();
                row.Navigation = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
