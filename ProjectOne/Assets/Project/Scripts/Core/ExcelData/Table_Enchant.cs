using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Enchant
    {
        public class Row {
            public int ID { get; set; } = 0;
            public int MaxEnchantLv { get; set; } = 0;
            public CurrencyInfo CostType { get; set; } = CurrencyInfo.None;
            public int CostValue { get; set; } = 0;
            public float CostLvModify { get; set; } = 0f;
            public float Probability { get; set; } = 0f;
            public float ProbabilityLvModify { get; set; } = 0f;
            public StatInfo StepStatOptionType_1 { get; set; } = StatInfo.None;
            public float StepStatOptionValue_1 { get; set; } = 0f;
            public StatInfo StepStatOptionType_2 { get; set; } = StatInfo.None;
            public float StepStatOptionValue_2 { get; set; } = 0f;
            public int UnlockLv { get; set; } = 0;
            public SkillInfo UnlockSkill { get; set; } = SkillInfo.None;
        }

        public const string Filename = "edt_enchant.bytes";
        public const TableType Type = TableType.TableEnchant;
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
                row.MaxEnchantLv = reader.ReadInt32();
                row.CostType = (CurrencyInfo)reader.ReadInt32();
                row.CostValue = reader.ReadInt32();
                row.CostLvModify = reader.ReadSingle();
                row.Probability = reader.ReadSingle();
                row.ProbabilityLvModify = reader.ReadSingle();
                row.StepStatOptionType_1 = (StatInfo)reader.ReadInt32();
                row.StepStatOptionValue_1 = reader.ReadSingle();
                row.StepStatOptionType_2 = (StatInfo)reader.ReadInt32();
                row.StepStatOptionValue_2 = reader.ReadSingle();
                row.UnlockLv = reader.ReadInt32();
                row.UnlockSkill = (SkillInfo)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
