using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Buff
    {
        public class Row {
            public Buff ID { get; set; } = Buff.None;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public bool IsDebuff { get; set; } = false;
            public BuffStackPolicy StackPolicy { get; set; } = BuffStackPolicy.None;
            public float TickInterval { get; set; } = 0f;
            public ActionBlockType[] BlockFlags { get; set; } = Array.Empty<ActionBlockType>();
            public SkillEffect EffectID_01 { get; set; } = SkillEffect.None;
            public SkillEffect EffectID_02 { get; set; } = SkillEffect.None;
        }

        public const string Filename = "edt_buff.bytes";
        public const TableType Type = TableType.TableBuff;
        static Dictionary<Buff, Row> _all = new Dictionary<Buff, Row>();

        public static Row Get( Buff id )
        {
            Row row = null;
            _all.TryGetValue( id, out row );
            return row;
        }

        public static Dictionary<Buff, Row> All()
        {
            return _all;
        }

        public static bool _parser( BinaryReader reader, ref string error )
        {
            try {
                Row row = new Row();
                row.ID = (Buff)reader.ReadInt32();
                row.Name = reader.ReadString();
                row.Desc = reader.ReadString();
                row.Icon = reader.ReadString();
                row.IsDebuff = reader.ReadBoolean();
                row.StackPolicy = (BuffStackPolicy)reader.ReadInt32();
                row.TickInterval = reader.ReadSingle();
                { int _n = reader.ReadInt32(); row.BlockFlags = new ActionBlockType[_n]; for(int _i=0;_i<_n;_i++) row.BlockFlags[_i] = (ActionBlockType)reader.ReadInt32(); }
                row.EffectID_01 = (SkillEffect)reader.ReadInt32();
                row.EffectID_02 = (SkillEffect)reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
