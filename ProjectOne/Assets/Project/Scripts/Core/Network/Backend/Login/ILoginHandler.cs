using Cysharp.Threading.Tasks;

namespace ProjectOne.Network
{
	// 로그인 방식별 처리 추상화 — NetworkManager 가 LoginType 에 맞는 핸들러를 선택해 호출한다.
	// 게스트는 동기, 구글 등은 계정 선택 UI 로 비동기라 반환은 UniTask 로 통일한다.
	public interface ILoginHandler
	{
		// 로그인 수행 결과 — 성공 여부와 실패 사유.
		UniTask<(bool success, string error)> LoginAsync();
	}
}
