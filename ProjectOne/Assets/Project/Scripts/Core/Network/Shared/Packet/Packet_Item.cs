namespace ProjectOne.Shared
{
	// 아이템 패킷 — 스택 아이템 차감(파괴).

	// 차감할 아이템 묶음. 클라는 이미 로컬에서 뺐고, 서버가 같은 양을 USER_INVENTORY 에서 뺀다.
	[System.Serializable]
	public class ItemSpendRequest
	{
		public OwnedItemDto[] items;
	}

	[System.Serializable]
	public class ItemSpendResponse : ServerResponse
	{
	}
}
