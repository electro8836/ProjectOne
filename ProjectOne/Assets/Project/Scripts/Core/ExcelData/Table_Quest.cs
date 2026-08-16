using System;
using System.Collections.Generic;
using System.IO;

namespace EDT {

    public static class Table_Quest
    {
        public class Row {
            public int ID { get; set; } = 0;
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public QuestCategory Category { get; set; } = QuestCategory.None;
            public int ReqLevel { get; set; } = 0;
            public int ReqQuestID { get; set; } = 0;
            public QuestAcceptType AcceptType { get; set; } = QuestAcceptType.None;
            public int AcceptNpcID { get; set; } = 0;
            public bool IsRepeatable { get; set; } = false;
            public QuestCompleteType CompleteType { get; set; } = QuestCompleteType.None;
            public int CompleteNpcID { get; set; } = 0;
            public QuestTargetType QuestTargetType { get; set; } = QuestTargetType.None;
            public string QuestParam_1 { get; set; } = string.Empty;
            public string QuestParam_2 { get; set; } = string.Empty;
            public string QuestParam_3 { get; set; } = string.Empty;
            public int RewardExp { get; set; } = 0;
            public int RewardGroupID { get; set; } = 0;
        }

        public const string Filename = "edt_quest.bytes";
        public const TableType Type = TableType.TableQuest;
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
                row.Category = (QuestCategory)reader.ReadInt32();
                row.ReqLevel = reader.ReadInt32();
                row.ReqQuestID = reader.ReadInt32();
                row.AcceptType = (QuestAcceptType)reader.ReadInt32();
                row.AcceptNpcID = reader.ReadInt32();
                row.IsRepeatable = reader.ReadBoolean();
                row.CompleteType = (QuestCompleteType)reader.ReadInt32();
                row.CompleteNpcID = reader.ReadInt32();
                row.QuestTargetType = (QuestTargetType)reader.ReadInt32();
                row.QuestParam_1 = reader.ReadString();
                row.QuestParam_2 = reader.ReadString();
                row.QuestParam_3 = reader.ReadString();
                row.RewardExp = reader.ReadInt32();
                row.RewardGroupID = reader.ReadInt32();
                _all.Add( row.ID, row );
            } catch( Exception e ) {
                error = string.Format( "EDT Binary parsing error - Message:{0}, File:{1}", e.Message, Filename );
                return false;
            }
            return true;
        }
    }
}
