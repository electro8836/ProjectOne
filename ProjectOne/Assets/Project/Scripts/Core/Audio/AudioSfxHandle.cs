namespace ProjectOne.Audio
{
	// 루프성 SFX(RootSFX 등) 재생 핸들.
	// 필드는 AudioManager 만 다루는 내부 상태라 internal 로 노출한다.
	public sealed class AudioSfxHandle
	{
		internal FMOD.Studio.EventInstance Instance;   // 이벤트가 없어 재생하지 못했으면 invalid
		internal bool Released;
	}
}
