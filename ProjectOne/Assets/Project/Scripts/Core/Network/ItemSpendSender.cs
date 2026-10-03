using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Network
{
	// 스택 아이템 차감(파괴)을 서버에 묶어 보낸다 — 딤 없이 백그라운드로.
	//
	// 같은 action 은 응답 전 중복 호출이 버려진다(BackndFunctionCaller). 연달아 파괴하면
	// 두 번째 차감이 유실되므로, 전송 중에 들어온 차감은 모아 두었다가 응답이 오면 이어서 보낸다.
	public sealed class ItemSpendSender : Singleton<ItemSpendSender>
	{
		// 보내지 않은 차감 — 아이템별 합계
		private readonly Dictionary<int, int> _pending = new Dictionary<int, int>();

		private bool _isSending;

		private ItemSpendSender()
		{
		}

		// 로컬에서 이미 뺀 양을 서버에도 빼도록 쌓는다.
		public void Enqueue(int itemId, int count)
		{
			if (count <= 0 || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			int prev;
			_pending.TryGetValue(itemId, out prev);
			_pending[itemId] = prev + count;

			if (_isSending == false)
			{
				send();
			}
		}

		private void send()
		{
			if (_pending.Count == 0)
			{
				return;
			}

			OwnedItemDto[] items = new OwnedItemDto[_pending.Count];
			int index = 0;
			Dictionary<int, int>.Enumerator e = _pending.GetEnumerator();
			while (e.MoveNext() == true)
			{
				OwnedItemDto item = new OwnedItemDto();
				item.itemId = e.Current.Key;
				item.count = e.Current.Value;
				items[index] = item;
				index++;
			}

			_pending.Clear();

			ItemSpendRequest request = new ItemSpendRequest();
			request.items = items;
			_isSending = true;
			NetworkManager.Instance.RequestItemSpend(request, onSent);
		}

		// 실패는 되돌리지 않는다 — 로컬은 이미 썼고, 다음 로그인 때 서버값으로 정리된다.
		private void onSent(bool success, ItemSpendResponse data, string error)
		{
			_isSending = false;

			if (success == false)
			{
				Debug.LogWarning($"[ItemSpendSender] 아이템 차감 서버 반영 실패 — 다음 로그인에 서버값으로 정리된다: {error}");
			}

			send();
		}
	}
}
