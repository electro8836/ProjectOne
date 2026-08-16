using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Projectile
    {
        public class Row {
            public Projectile ID { get; set; } = Projectile.None;
            public string PrefabPath { get; set; } = string.Empty;
            public float Speed { get; set; } = 0f;
            public float Accel { get; set; } = 0f;
            public float MaxSpeed { get; set; } = 0f;
            public float HomingRate { get; set; } = 0f;
            public int Pierce { get; set; } = 0;
            public float MaxDistance { get; set; } = 0f;
            public float ExplodeRadius { get; set; } = 0f;
            public SkillEffect HitEffectID_1 { get; set; } = SkillEffect.None;
            public SkillEffect HitEffectID_2 { get; set; } = SkillEffect.None;
        }

        public const string Filename = "edt_projectile.bytes";
        public const TableType Type = TableType.TableProjectile;
        static Dictionary<Projectile, Row> _all = new Dictionary<Projectile, Row>();

        public static Row Get( Projectile id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Projectile, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Projectile)reader.ReadInt32();
                row.PrefabPath = reader.ReadString();
                row.Speed = reader.ReadSingle();
                row.Accel = reader.ReadSingle();
                row.MaxSpeed = reader.ReadSingle();
                row.HomingRate = reader.ReadSingle();
                row.Pierce = reader.ReadInt32();
                row.MaxDistance = reader.ReadSingle();
                row.ExplodeRadius = reader.ReadSingle();
                row.HitEffectID_1 = (SkillEffect)reader.ReadInt32();
                row.HitEffectID_2 = (SkillEffect)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
