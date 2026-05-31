using UnityEngine;

namespace ProjectOne.Utils
{
	// 루프성 VFX(RootVFX 등) 부착 핸들.
	// 비동기 로드 중 Release 가 먼저 도착해도 안전하도록 released 플래그로 조율한다.
	// 필드는 VFXManager 만 다루는 내부 상태라 internal 로 노출한다.
	public sealed class VFXHandle
	{
		internal string Address;
		internal Transform Parent;
		internal VFXItem Item;   // 로드 완료 전에는 null
		internal bool Released;

		internal VFXHandle(string address, Transform parent)
		{
			Address = address;
			Parent = parent;
		}
	}
}
