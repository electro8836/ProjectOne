using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_RuinsHazard
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public RuinsHazardType HazardType { get; set; } = RuinsHazardType.None;
            public int Count { get; set; } = 0;
            public float Radius { get; set; } = 0f;
            public float InnerRadius { get; set; } = 0f;
            public float WarnTime { get; set; } = 0f;
            public float RepeatInterval { get; set; } = 0f;
            public int PointGroup { get; set; } = 0;
            public SkillEffect DamageEffectID { get; set; } = SkillEffect.None;
            public float AfterDelay { get; set; } = 0f;
        }

        public const string Filename = "edt_ruinshazard.bytes";
        public const TableType Type = TableType.TableRuinsHazard;
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
                row.HazardType = (RuinsHazardType)reader.ReadInt32();
                row.Count = reader.ReadInt32();
                row.Radius = reader.ReadSingle();
                row.InnerRadius = reader.ReadSingle();
                row.WarnTime = reader.ReadSingle();
                row.RepeatInterval = reader.ReadSingle();
                row.PointGroup = reader.ReadInt32();
                row.DamageEffectID = (SkillEffect)reader.ReadInt32();
                row.AfterDelay = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
