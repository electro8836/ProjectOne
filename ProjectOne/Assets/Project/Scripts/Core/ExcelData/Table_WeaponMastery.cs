using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_WeaponMastery
    {
        public class Row {
            public WeaponMastery ID { get; set; } = WeaponMastery.None;
            public string Name { get; set; } = string.Empty;
            public WeaponType WeaponType { get; set; } = WeaponType.None;
            public WeaponRangeType RangeType { get; set; } = WeaponRangeType.None;
            public string AnimControllerName { get; set; } = string.Empty;
            public Option LevelBonusOption { get; set; } = Option.None;
            public float LevelBonusPerLevel { get; set; } = 0f;
            public Skill NormalAttackSkill { get; set; } = Skill.None;
            public int MaxRow { get; set; } = 0;
            public int MaxColumn { get; set; } = 0;
            public int SkillTreeNodeGroupID { get; set; } = 0;
        }

        public const string Filename = "edt_weaponmastery.bytes";
        public const TableType Type = TableType.TableWeaponMastery;
        static Dictionary<WeaponMastery, Row> _all = new Dictionary<WeaponMastery, Row>();

        public static Row Get( WeaponMastery id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<WeaponMastery, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (WeaponMastery)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.WeaponType = (WeaponType)reader.ReadInt32();
                row.RangeType = (WeaponRangeType)reader.ReadInt32();
                row.AnimControllerName = reader.ReadString();
                row.LevelBonusOption = (Option)reader.ReadInt32();
                row.LevelBonusPerLevel = reader.ReadSingle();
                row.NormalAttackSkill = (Skill)reader.ReadInt32();
                row.MaxRow = reader.ReadInt32();
                row.MaxColumn = reader.ReadInt32();
                row.SkillTreeNodeGroupID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
