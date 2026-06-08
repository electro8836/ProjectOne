using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Resources
{
	// 패치 부트 설정 — AssetBundleLoader가 [SerializeField]로 보유 (인스펙터 주입)
	// _downloadLabels는 AddressableAutoMarker.MarkAll() 실행 시 자동 갱신
	// 배치 위치: Assets/Project/Data/ScriptableObject/Config/PatchConfig.asset
	[CreateAssetMenu(fileName = "PatchConfig", menuName = "ProjectOne/PatchConfig")]
	public class PatchConfig : ScriptableObject
	{
		[SerializeField] private string[] _downloadLabels = new string[0];

		// 서버 버전체크 API URL — 비어 있으면 버전체크 스텁 통과
		[SerializeField] private string _versionCheckUrl = "";

		// 폴백 번들 서버 URL — 비어 있으면 Addressables 프로파일 기본값 사용
		[SerializeField] private string _defaultServerUrl = "";

		public IReadOnlyList<string> DownloadLabels => _downloadLabels;
		public string VersionCheckUrl => _versionCheckUrl;
		public string DefaultServerUrl => _defaultServerUrl;
	}
}
