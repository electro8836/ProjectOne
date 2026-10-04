using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EDT;

namespace MailItemExporter
{
	// 뒤끝 콘솔 차트 MailItem 을 만든다 — 우편 첨부로 고를 수 있는 항목(재화·스택 아이템·수집품).
	//
	// 실행: Shared/Backnd/MailItem.bat (인자로 Shared/Backnd 폴더 경로를 넘긴다).
	// 테이블 .bytes 를 읽으므로 엑셀을 고쳤으면 ExcelDataTable 변환을 먼저 돌린다.
	// 콘솔에 업로드하고 '우편 사용'을 켜야 우편에 붙일 수 있다. 아이템·재화가 늘면 다시 내보내서 올린다.
	// 서버(Function_Mail)는 행의 RewardType·TargetID 와 발송 시 입력한 수량으로 지급한다.
	// 장비는 등급·품질이 정해지지 않아 넣지 않는다.
	public static class Program
	{
		// Shared/Backnd 기준 상대 경로
		private const string TablesRelativePath = "../../ProjectOne/Assets/Project/Data/Tables";
		private const string OutputFileName = "MailItem.csv";

		private static string _tablesFolder;

		public static int Main(string[] args)
		{
			if (args.Length < 1 || Directory.Exists(args[0]) == false)
			{
				Console.WriteLine("usage: MailItemExporter <Shared/Backnd folder>");
				return 1;
			}

			string backndFolder = Path.GetFullPath(args[0]);
			_tablesFolder = Path.GetFullPath(Path.Combine(backndFolder, TablesRelativePath));

			Loader loader = new Loader();
			if (loader.LoadAll(openTable, null) == false)
			{
				Console.WriteLine($"table load failed: {loader.CurrentFile} / {loader.Error}");
				return 1;
			}

			StringBuilder csv = new StringBuilder();
			csv.Append("MailItemID,RewardType,TargetID,Name\n");

			HashSet<int> ids = new HashSet<int>();
			int rowCount = 0;

			List<Currency> currencies = new List<Currency>(Table_Currency.All().Keys);
			currencies.Sort();
			for (int i = 0; i < currencies.Count; i++)
			{
				if (currencies[i] == Currency.None)
				{
					continue;
				}

				if (addRow(csv, ids, (int)currencies[i], RewardType.Currency, Table_Currency.Get(currencies[i]).Name) == false)
				{
					return 1;
				}

				rowCount++;
			}

			List<int> itemIds = new List<int>(Table_Item.All().Keys);
			itemIds.Sort();
			for (int i = 0; i < itemIds.Count; i++)
			{
				Table_Item.Row row = Table_Item.Get(itemIds[i]);
				if (row.MainCategory == ItemMainCategory.Equipment)
				{
					continue;
				}

				if (addRow(csv, ids, row.ID, RewardType.Item, row.Name) == false)
				{
					return 1;
				}

				rowCount++;
			}

			string path = Path.Combine(backndFolder, OutputFileName);
			File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
			Console.WriteLine($"{rowCount} rows -> {path}");
			return 0;
		}

		// MailItemID 는 TargetID 와 같다 — 재화(1~)와 아이템(10자리) 대역이 겹치면 중단한다.
		private static bool addRow(StringBuilder csv, HashSet<int> ids, int id, RewardType rewardType, string name)
		{
			if (ids.Add(id) == false)
			{
				Console.WriteLine($"duplicate MailItemID: {id} ({rewardType} {name})");
				return false;
			}

			csv.Append(id).Append(',').Append(rewardType).Append(',').Append(id).Append(',').Append(escape(name)).Append('\n');
			return true;
		}

		private static string escape(string value)
		{
			if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0)
			{
				return value;
			}

			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}

		private static BinaryReader openTable(string filename)
		{
			string path = Path.Combine(_tablesFolder, filename);
			if (File.Exists(path) == false)
			{
				return null;
			}

			return new BinaryReader(File.OpenRead(path));
		}
	}
}
