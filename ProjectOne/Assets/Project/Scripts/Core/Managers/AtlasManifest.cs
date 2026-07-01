using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Resources
{
	// 아웃게임 UI 아이콘 아틀라스 목록 — AssetBundleLoader가 [SerializeField]로 보유 (인스펙터 주입)
	// _atlasAddresses는 AddressableAutoMarker.MarkAll() 실행 시 Art/UI 의 .spriteatlasv2 주소로 자동 갱신
	// 배치 위치: Assets/Project/Data/ScriptableObject/Config/AtlasManifest.asset
	[CreateAssetMenu(fileName = "AtlasManifest", menuName = "ProjectOne/AtlasManifest")]
	public class AtlasManifest : ScriptableObject
	{
		[SerializeField] private string[] _atlasAddresses = new string[0];

		public IReadOnlyList<string> AtlasAddresses => _atlasAddresses;
	}
}
