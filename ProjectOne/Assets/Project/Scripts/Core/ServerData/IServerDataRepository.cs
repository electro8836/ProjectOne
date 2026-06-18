using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.ServerData
{
	// 도메인 키 단위로 유저 데이터(DTO)를 읽고 쓰는 저장소 추상화.
	// - 현재는 로컬 JSON, 추후 서버 구현으로 교체 → 매니저는 이 인터페이스로만 접근한다.
	// - 네트워크 I/O 대비로 비동기(UniTask) 계약. 로컬 구현은 동기 처리 후 완료된 태스크를 반환한다.
	public interface IServerDataRepository
	{
		// 저장된 데이터가 없으면 found=false (data = null)
		UniTask<(bool found, T data)> LoadAsync<T>(string key, CancellationToken ct) where T : class;

		// 저장 호출은 fire-and-forget 으로 사용 → 실패는 구현체 내부에서 로깅한다.
		UniTask SaveAsync<T>(string key, T data) where T : class;
	}
}
