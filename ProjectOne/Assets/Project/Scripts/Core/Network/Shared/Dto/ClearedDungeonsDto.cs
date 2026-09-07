using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 클리어한 던전 ID 목록 직렬화 DTO — 서버-클라 공유 영속 스키마(서버 연동 시 사용). 클라는 ClearedDungeons 로 변환해 쓴다.
	[System.Serializable]
	public class ClearedDungeonsDto
	{
		public List<int> dungeonIds = new List<int>();
	}
}
