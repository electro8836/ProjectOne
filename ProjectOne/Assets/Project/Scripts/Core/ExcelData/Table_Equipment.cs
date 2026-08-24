using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Equipment
    {
        public class Row {
            public int ID { get; set; } = 0;
            public EquipSlotTypes EquipSlotType { get; set; } = EquipSlotTypes.None;
            public WeaponType WeaponType { get; set; } = WeaponType.None;
            public ItemGradeType MaxGrade { get; set; } = ItemGradeType.None;
            public int EquipOptionGroupID { get; set; } = 0;
            public string WeaponSetAddress { get; set; } = string.Empty;
        }

        public const string Filename = "edt_equipment.bytes";
        public const TableType Type = TableType.TableEquipment;
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
                row.EquipSlotType = (EquipSlotTypes)reader.ReadInt32();
                row.WeaponType = (WeaponType)reader.ReadInt32();
                row.MaxGrade = (ItemGradeType)reader.ReadInt32();
                row.EquipOptionGroupID = reader.ReadInt32();
                row.WeaponSetAddress = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
