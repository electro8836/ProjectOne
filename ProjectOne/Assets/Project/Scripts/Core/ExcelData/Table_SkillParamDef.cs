using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_SkillParamDef
    {
        public class Row {
            public int ID { get; set; } = 0;
            public SkillEffectTypes EffectType { get; set; } = SkillEffectTypes.None;
            public int Index { get; set; } = 0;
            public string ParamKey { get; set; } = string.Empty;
            public ParamDefValueTypes ValueType { get; set; } = ParamDefValueTypes.None;
            public string Desc { get; set; } = string.Empty;
        }

        public const string Filename = "edt_skillparamdef.bytes";
        public const TableType Type = TableType.TableSkillParamDef;
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
                row.EffectType = (SkillEffectTypes)reader.ReadInt32();
                row.Index = reader.ReadInt32();
                row.ParamKey = reader.ReadString();
                row.ValueType = (ParamDefValueTypes)reader.ReadInt32();
                row.Desc = reader.ReadString();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
