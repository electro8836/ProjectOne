using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Pet
    {
        public class Row {
            public Pet ID { get; set; } = Pet.None;
            public string Name { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
            public string Image { get; set; } = string.Empty;
            public ItemGradeType Grade { get; set; } = ItemGradeType.None;
            public Option Opt_ID { get; set; } = Option.None;
            public float Opt_Val { get; set; } = 0f;
            public float Opt_Step { get; set; } = 0f;
            public string EquipText { get; set; } = string.Empty;
        }

        public const string Filename = "edt_pet.bytes";
        public const TableType Type = TableType.TablePet;
        static Dictionary<Pet, Row> _all = new Dictionary<Pet, Row>();

        public static Row Get( Pet id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Pet, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Pet)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Model = reader.ReadString();
                row.Image = reader.ReadString();
                row.Grade = (ItemGradeType)reader.ReadInt32();
                row.Opt_ID = (Option)reader.ReadInt32();
                row.Opt_Val = reader.ReadSingle();
                row.Opt_Step = reader.ReadSingle();
                row.EquipText = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
