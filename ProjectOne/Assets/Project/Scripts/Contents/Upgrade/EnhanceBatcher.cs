using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Upgrade
{
	// 장비 강화 묶음 전송 — 연타·꾹 누르기를 한 번의 요청으로 묶는다.
	//
	// 강화는 확률이 없는 결정적 규칙이라 누를 때마다 클라가 먼저 적용한다(레벨 +1, 비용 차감).
	// 마지막 입력 후 IdleFlushSeconds 동안 입력이 없으면 모은 횟수를 서버로 보낸다(딤 없음).
	//
	// 서버 결과가 기대와 다르면(재화 부족으로 부분 적용, 레벨 어긋남, 통신 실패) 서버 상태로 맞추고
	// 미리 뺀 재화를 돌려준다. 같은 장비의 뒤 묶음은 fromLevel 이 틀어졌으므로 함께 되돌린다.
	// 재화는 증감으로만 반영한다 — 전투 중 들어오는 처치 재화와 섞여도 서로 덮어쓰지 않는다.
	public sealed class EnhanceBatcher : Singleton<EnhanceBatcher>
	{
		// 마지막 입력 후 이 시간(초) 동안 입력이 없으면 보낸다.
		private const float IdleFlushSeconds = 0.5f;

		private sealed class Batch
		{
			public long uid;
			public int fromLevel;
			public int count;
			public readonly List<UpgradeCost> spent = new List<UpgradeCost>(2);
		}

		// 모으는 중인 묶음
		private Batch _collecting;

		// 보낼 차례를 기다리는 묶음 — 한 번에 하나만 전송한다(같은 action 중복 차단과 fromLevel 순서 보장).
		private readonly Queue<Batch> _ready = new Queue<Batch>();
		private Batch _sending;

		private CancellationTokenSource _idleCts;

		private readonly List<UpgradeCost> _costs = new List<UpgradeCost>(2);

		private EnhanceBatcher()
		{
		}

		// 강화 1회를 즉시 적용하고 묶음에 쌓는다. 적용했으면 true.
		public bool TryEnhance(EquipmentInstance instance)
		{
			if (NetworkManager.Instance.IsLoggedIn == false || EquipmentUpgrade.CanEnhance(instance) == false)
			{
				return false;
			}

			EquipmentUpgrade.GetEnhanceCost(instance, _costs);
			if (EquipmentUpgrade.IsAffordable(_costs) == false)
			{
				return false;
			}

			// 다른 장비로 넘어가면 앞 장비 묶음을 먼저 보낸다.
			if (_collecting != null && _collecting.uid != instance.uid)
			{
				Flush();
			}

			if (_collecting == null)
			{
				_collecting = new Batch();
				_collecting.uid = instance.uid;
				_collecting.fromLevel = instance.level;
			}

			for (int i = 0; i < _costs.Count; i++)
			{
				CurrencyManager.Instance.TrySpend(_costs[i].currency, _costs[i].amount);
				addCost(_collecting.spent, _costs[i].currency, _costs[i].amount);
			}

			_collecting.count++;
			setLevel(instance, instance.level + 1);

			restartIdleTimer();
			return true;
		}

		// 모은 묶음을 보낸다 — 대기 타이머 만료·장비 변경·제작 창 닫힘.
		public void Flush()
		{
			cancelIdleTimer();

			if (_collecting != null)
			{
				_ready.Enqueue(_collecting);
				_collecting = null;
			}

			sendNext();
		}

		// ── 전송 ──────────────────────────────────────────────────────

		private void sendNext()
		{
			if (_sending != null || _ready.Count == 0)
			{
				return;
			}

			_sending = _ready.Dequeue();

			EquipmentEnhanceRequest request = new EquipmentEnhanceRequest();
			request.uid = _sending.uid;
			request.fromLevel = _sending.fromLevel;
			request.count = _sending.count;
			Debug.Log($"[EnhanceBatcher] 강화 묶음 전송 — uid:{request.uid} Lv.{request.fromLevel} +{request.count}");
			NetworkManager.Instance.RequestEquipmentEnhance(request, onResponse);
		}

		private void onResponse(bool success, EquipmentGrowthResponse data, string error)
		{
			Batch batch = _sending;
			_sending = null;

			if (batch == null)
			{
				return;
			}

			EquipmentInstanceDto server = findDto(data, batch.uid);
			if (success == false || server == null)
			{
				// 통신 실패·거절 — 이 묶음은 적용되지 않은 것으로 본다. 응답만 유실됐다면 다음 로드에서 서버 값으로 바로잡힌다.
				Debug.LogWarning($"[EnhanceBatcher] 강화 묶음 실패 — 되돌림 uid:{batch.uid} {batch.fromLevel}→+{batch.count}: {error}");
				rollback(batch, batch.fromLevel, null);
			}
			else if (server.level != batch.fromLevel + batch.count)
			{
				// 부분 적용·레벨 어긋남 — 서버 상태로 맞추고 서버가 쓰지 않은 만큼 돌려준다.
				Debug.LogWarning($"[EnhanceBatcher] 강화 묶음 불일치 — 서버 기준으로 맞춤 uid:{batch.uid} 기대 {batch.fromLevel + batch.count}, 서버 {server.level}");
				rollback(batch, server.level, data.spent);
			}

			sendNext();
		}

		// batch 와 같은 장비의 뒤 묶음까지 되돌린다 — 레벨은 level 로, 재화는 미리 뺀 양에서 서버 차감분을 뺀 만큼 돌려준다.
		private void rollback(Batch batch, int level, CurrencyAmountDto[] serverSpent)
		{
			refund(batch.spent, serverSpent);
			discardFollowing(batch.uid);

			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(batch.uid);
			if (instance != null)
			{
				setLevel(instance, level);
			}
		}

		// 같은 장비의 대기·수집 중 묶음을 버리고 그 재화를 돌려준다(fromLevel 이 틀어졌다).
		private void discardFollowing(long uid)
		{
			int count = _ready.Count;
			for (int i = 0; i < count; i++)
			{
				Batch queued = _ready.Dequeue();
				if (queued.uid == uid)
				{
					refund(queued.spent, null);
					continue;
				}

				_ready.Enqueue(queued);
			}

			if (_collecting != null && _collecting.uid == uid)
			{
				refund(_collecting.spent, null);
				_collecting = null;
				cancelIdleTimer();
			}
		}

		private static void refund(List<UpgradeCost> spent, CurrencyAmountDto[] serverSpent)
		{
			for (int i = 0; i < spent.Count; i++)
			{
				int amount = spent[i].amount - findSpent(serverSpent, spent[i].currency);
				if (amount > 0)
				{
					CurrencyManager.Instance.Add(spent[i].currency, amount);
				}
			}
		}

		// ── 대기 타이머 ───────────────────────────────────────────────

		private void restartIdleTimer()
		{
			cancelIdleTimer();
			_idleCts = new CancellationTokenSource();
			waitIdleAsync(_idleCts.Token).Forget();
		}

		private void cancelIdleTimer()
		{
			if (_idleCts == null)
			{
				return;
			}

			_idleCts.Cancel();
			_idleCts.Dispose();
			_idleCts = null;
		}

		private async UniTaskVoid waitIdleAsync(CancellationToken ct)
		{
			bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(IdleFlushSeconds), DelayType.Realtime, PlayerLoopTiming.Update, ct).SuppressCancellationThrow();
			if (canceled == true)
			{
				return;
			}

			Flush();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void setLevel(EquipmentInstance instance, int level)
		{
			instance.level = level;
			Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
			Account.Instance.Loadout.ReapplyEquipped(instance.uid);
		}

		private static void addCost(List<UpgradeCost> buffer, EDT.Currency currency, int amount)
		{
			for (int i = 0; i < buffer.Count; i++)
			{
				if (buffer[i].currency == currency)
				{
					UpgradeCost sum = buffer[i];
					sum.amount += amount;
					buffer[i] = sum;
					return;
				}
			}

			UpgradeCost cost;
			cost.currency = currency;
			cost.amount = amount;
			cost.owned = 0;
			buffer.Add(cost);
		}

		private static int findSpent(CurrencyAmountDto[] spent, EDT.Currency currency)
		{
			if (spent == null)
			{
				return 0;
			}

			for (int i = 0; i < spent.Length; i++)
			{
				if (spent[i] != null && spent[i].currencyId == (int)currency)
				{
					return spent[i].amount;
				}
			}

			return 0;
		}

		private static EquipmentInstanceDto findDto(EquipmentGrowthResponse data, long uid)
		{
			if (data == null || data.equipments == null)
			{
				return null;
			}

			for (int i = 0; i < data.equipments.Length; i++)
			{
				if (data.equipments[i] != null && data.equipments[i].uid == uid)
				{
					return data.equipments[i];
				}
			}

			return null;
		}
	}
}
