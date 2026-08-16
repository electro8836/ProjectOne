using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Consumable
    {
        public class Row {
            public int ID { get; set; } = 0;
            public ConsumeEffect ConsumeEffect { get; set; } = ConsumeEffect.None;
            public string EffectParam_1 { get; set; } = string.Empty;
            public string EffectParam_2 { get; set; } = string.Empty;
            public int CooldownGroup { get; set; } = 0;
            public float Cooldown { get; set; } = 0f;
        }

        public const string Filename = "edt_consumable.bytes";
        public const TableType Type = TableType.TableConsumable;
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
                row.ConsumeEffect = (ConsumeEffect)reader.ReadInt32();
                row.EffectParam_1 = reader.ReadString();
                row.EffectParam_2 = reader.ReadString();
                row.CooldownGroup = reader.ReadInt32();
                row.Cooldown = reader.ReadSingle();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
