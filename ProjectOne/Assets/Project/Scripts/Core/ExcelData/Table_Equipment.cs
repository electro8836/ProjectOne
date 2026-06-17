using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Equipment
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public EquipmentTypes EquipmentType { get; set; } = EquipmentTypes.None;
            public GradeTypes Grade { get; set; } = GradeTypes.None;
            public CharacterTypes BindCharacter { get; set; } = CharacterTypes.None;
            public int EnchantInfo { get; set; } = 0;
            public StatInfo StatOptionType_1 { get; set; } = StatInfo.None;
            public float StatOptionValue_1 { get; set; } = 0f;
            public StatInfo StatOptionType_2 { get; set; } = StatInfo.None;
            public float StatOptionValue_2 { get; set; } = 0f;
            public StatInfo StatOptionType_3 { get; set; } = StatInfo.None;
            public float StatOptionValue_3 { get; set; } = 0f;
            public int SkillOption_1 { get; set; } = 0;
            public int SkillOption_2 { get; set; } = 0;
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
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.EquipmentType = (EquipmentTypes)reader.ReadInt32();
                row.Grade = (GradeTypes)reader.ReadInt32();
                row.BindCharacter = (CharacterTypes)reader.ReadInt32();
                row.EnchantInfo = reader.ReadInt32();
                row.StatOptionType_1 = (StatInfo)reader.ReadInt32();
                row.StatOptionValue_1 = reader.ReadSingle();
                row.StatOptionType_2 = (StatInfo)reader.ReadInt32();
                row.StatOptionValue_2 = reader.ReadSingle();
                row.StatOptionType_3 = (StatInfo)reader.ReadInt32();
                row.StatOptionValue_3 = reader.ReadSingle();
                row.SkillOption_1 = reader.ReadInt32();
                row.SkillOption_2 = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
