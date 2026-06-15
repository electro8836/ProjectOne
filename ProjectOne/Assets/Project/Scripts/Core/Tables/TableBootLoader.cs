using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Resources;

namespace ProjectOne.Data
{
	// Addressables 라벨 "Tables" 의 edt_*.bytes 를 자동생성 EDT.Loader 에 공급해 일괄 파싱한다.
	// EDT.Loader 는 테이블 재생성 시 함께 갱신되므로(목록 하드코딩이 자동 동기화됨) 수동 유지보수가 필요 없다.
	public static class TableBootLoader
	{
		private const string Label = "Tables";

		// 도메인당 1회만 파싱. Table_*._all(static)은 초기화 코드가 없어,
		// 도메인 리로드 off 환경에서 재호출되면 _all.Add 가 중복 키 예외를 던진다.
		private static bool _loaded;

		public static async UniTask<bool> LoadAllAsync(CancellationToken ct = default)
		{
			if (_loaded == true)
			{
				return true;
			}

			IList<TextAsset> assets = await ResourceManager.Instance.PreloadByLabelAsync<TextAsset>(Label, null, ct);

			ByteSource source = new ByteSource(assets);
			EDT.Loader loader = new EDT.Loader();
			bool ok = loader.LoadAll(source.Open, null);
			ResourceManager.Instance.ReleasePreloaded(assets);

			if (ok == false)
			{
				Debug.LogError($"[TableBootLoader] 테이블 로드 실패: {loader.CurrentFile} / {loader.Error}");
				return false;
			}

			_loaded = true;
			return true;
		}

		// EDT.Loader.OpenFileFunctor 에 메서드 그룹(Open)으로 넘길 바이트 공급자.
		// 라벨 프리로드 결과(TextAsset)를 파일명("edt_xxx.bytes")으로 매핑한다.
		private sealed class ByteSource
		{
			private readonly Dictionary<string, TextAsset> _byName;

			public ByteSource(IList<TextAsset> assets)
			{
				_byName = new Dictionary<string, TextAsset>(assets.Count);
				for (int i = 0; i < assets.Count; i++)
				{
					TextAsset asset = assets[i];
					if (asset == null)
					{
						continue;
					}

					// Addressables 라벨 로드 결과는 TextAsset.name 이 확장자 없는 이름이라 보정해 매칭
					string key = asset.name;
					if (key.EndsWith(".bytes", StringComparison.Ordinal) == false)
					{
						key = key + ".bytes";
					}

					_byName[key] = asset;
				}
			}

			public BinaryReader Open(string filename)
			{
				TextAsset asset;
				if (_byName.TryGetValue(filename, out asset) == false)
				{
					Debug.LogError($"[TableBootLoader] 테이블 파일 없음: {filename}");
					return null;
				}

				return new BinaryReader(new MemoryStream(asset.bytes));
			}
		}
	}
}
