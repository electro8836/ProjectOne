using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Resources;

namespace EDT
{
	// Addressables 라벨 "Tables"로 묶인 edt_*.bytes 19개를 일괄 로드하고
	// 파일명에 매핑된 Table_*._parser 로 BinaryReader 파싱을 수행.
	// - 파싱이 끝나면 TextAsset 핸들은 즉시 반환 (Row 사전은 static에 적재됨)
	// - 부트스트래퍼 진입점에서 한 번만 호출
	public static class TableLoader
	{
		private const string Label = "Tables";

		private delegate bool ParserFn(BinaryReader reader, ref string error);

		private static readonly Dictionary<string, ParserFn> _parsers
			= buildParserMap();

		// 도메인당 1회만 파싱. Table_*._all(static)은 초기화 코드가 없어,
		// 도메인 리로드 off 환경에서 재호출되면 _all.Add가 중복 키 예외를 던진다.
		private static bool _loaded;

		public static async UniTask LoadAllAsync(CancellationToken ct = default)
		{
			if (_loaded)
			{
				return;
			}

			IList<TextAsset> assets = await ResourceManager.Instance
				.PreloadByLabelAsync<TextAsset>(Label, null, ct);

			try
			{
				for (int i = 0; i < assets.Count; i++)
				{
					TextAsset asset = assets[i];
					if (asset == null)
					{
						continue;
					}
					parseOne(asset);
				}
			}
			finally
			{
				ResourceManager.Instance.ReleasePreloaded(assets);
			}

			_loaded = true;
		}

		private static void parseOne(TextAsset asset)
		{
			// Addressables 라벨 로드 결과는 TextAsset.name이 확장자 없는 이름.
			// Table_*.Filename 은 "edt_xxx.bytes" 이므로 보정해서 매칭.
			string key = asset.name;
			if (!key.EndsWith(".bytes", StringComparison.Ordinal))
			{
				key = key + ".bytes";
			}

			ParserFn parser;
			if (!_parsers.TryGetValue(key, out parser))
			{
				Debug.LogWarning("TableLoader: 매핑되지 않은 테이블 파일 - " + asset.name);
				return;
			}

			using (MemoryStream ms = new MemoryStream(asset.bytes))
			using (BinaryReader reader = new BinaryReader(ms))
			{
				// 파일 헤더: int32 rowCount
				int rowCount = reader.ReadInt32();
				for (int i = 0; i < rowCount; i++)
				{
					string error = null;
					if (!parser(reader, ref error))
					{
						Debug.LogError("TableLoader: 파싱 실패 - " + error);
						throw new Exception("테이블 파싱 실패: " + key + " / " + error);
					}
				}
			}
		}

		private static Dictionary<string, ParserFn> buildParserMap()
		{
			Dictionary<string, ParserFn> map = new Dictionary<string, ParserFn>(20);
			map.Add(Table_BaseStat.Filename, Table_BaseStat._parser);
			map.Add(Table_BuffInfo.Filename, Table_BuffInfo._parser);
			map.Add(Table_Character.Filename, Table_Character._parser);
			map.Add(Table_Consumable.Filename, Table_Consumable._parser);
			map.Add(Table_CurrencyInfo.Filename, Table_CurrencyInfo._parser);
			map.Add(Table_DungeonList.Filename, Table_DungeonList._parser);
			map.Add(Table_DungeonStage.Filename, Table_DungeonStage._parser);
			map.Add(Table_Equipment.Filename, Table_Equipment._parser);
			map.Add(Table_ItemInfo.Filename, Table_ItemInfo._parser);
			map.Add(Table_Material.Filename, Table_Material._parser);
			map.Add(Table_Monster.Filename, Table_Monster._parser);
			map.Add(Table_Reward.Filename, Table_Reward._parser);
			map.Add(Table_SkillEffect.Filename, Table_SkillEffect._parser);
			map.Add(Table_SkillInfo.Filename, Table_SkillInfo._parser);
			map.Add(Table_SkillSet.Filename, Table_SkillSet._parser);
			map.Add(Table_SkinInfo.Filename, Table_SkinInfo._parser);
			map.Add(Table_StageInfo.Filename, Table_StageInfo._parser);
			map.Add(Table_StatInfo.Filename, Table_StatInfo._parser);
			map.Add(Table_UIWidgetPath.Filename, Table_UIWidgetPath._parser);
			return map;
		}
	}
}
