using System;
using System.IO;
using EDT;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 게임 테이블 로드 — 클라와 같은 자동생성 파서(EDT.Loader)로 패키지에 동봉된 Tables\*.bytes 를 읽는다.
	//
	// Table_* 는 정적 딕셔너리에 Add 하므로 두 번 로드하면 중복 키로 실패한다.
	// 워밍 컨테이너는 정적 상태를 재사용하므로 처음 한 번만 로드하고 이후엔 플래그로 건너뛴다.
	public static class GameData
	{
		private const string TablesFolder = "Tables";

		private static bool _loaded;

		public static bool EnsureLoaded(out string err)
		{
			if (_loaded == true)
			{
				err = null;
				return true;
			}

			Loader loader = new Loader();
			if (loader.LoadAll(openTable, null) == false)
			{
				err = "table load failed: " + loader.CurrentFile + " - " + loader.Error;
				return false;
			}

			RewardTable.Build();

			_loaded = true;
			err = null;
			return true;
		}

		private static BinaryReader openTable(string filename)
		{
			string path = Path.Combine(AppContext.BaseDirectory, TablesFolder, filename);
			if (File.Exists(path) == false)
			{
				return null;
			}

			return new BinaryReader(File.OpenRead(path));
		}
	}

	// 공유 추첨 코어(ProjectOne.Shared)에 System.Random 을 공급한다.
	// 호출마다 새로 만든다 — 워밍 컨테이너 재사용 시 동일 시드 위험을 피한다.
	public sealed class ServerRandomSource : IRandomSource
	{
		private readonly Random _rng = new Random();

		public int Range(int minInclusive, int maxExclusive)
		{
			return _rng.Next(minInclusive, maxExclusive);
		}
	}
}
