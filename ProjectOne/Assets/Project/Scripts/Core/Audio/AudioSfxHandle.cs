namespace ProjectOne.Audio
{
	// 루프성 SFX(RootSFX 등) 재생 핸들.
	// 비동기 로드 중 Stop 이 먼저 도착해도 안전하도록 released 플래그로 조율한다.
	// 필드는 AudioManager 만 다루는 내부 상태라 internal 로 노출한다.
	public sealed class AudioSfxHandle
	{
		internal string Address;
		internal AudioSourceItem Item;   // 로드 완료 전에는 null
		internal bool Released;

		internal AudioSfxHandle(string address)
		{
			Address = address;
		}
	}
}
