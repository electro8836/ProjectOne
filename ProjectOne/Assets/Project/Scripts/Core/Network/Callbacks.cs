using ProjectOne.Shared;

namespace ProjectOne.Network
{
	// 네트워크 콜백 델리게이트 정의.
	// 호출부가 넘기는 람다는 허용한다(API 소비자 선택). 내부 구현은 메서드 그룹으로 연결한다.

	// 함수 응답 콜백 — 성공 여부 / 응답 DTO / 에러 메시지
	public delegate void ResponseCallback<TResponse>(bool isSuccess, TResponse data, string errorMsg)
		where TResponse : ServerResponse;

	// 로그인 콜백 — 성공 여부 / 에러 메시지
	public delegate void LoginCallback(bool isSuccess, string errorMsg);
}
