using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Upgrade
{
	// 레벨 강화 묶음 전송 공용 베이스 — 연타·꾹 누르기를 한 번의 요청으로 묶는다(장비 강화·펫 강화).
	//
	// 강화는 확률이 없는 결정적 규칙이라 누를 때마다 클라가 먼저 적용한다(레벨 +1, 비용 차감).
	// 마지막 입력 후 IdleFlushSeconds 동안 입력이 없으면 모은 횟수를 서버로 보낸다(딤 없음).
	//
	// 서버 결과가 기대와 다르면(재화 부족으로 부분 적용, 레벨 어긋남, 통신 실패) 서버 상태로 맞추고
	// 미리 뺀 재화를 돌려준다. 같은 대상의 뒤 묶음은 fromLevel 이 틀어졌으므로 함께 되돌린다.
	// 재화는 증감으로만 반영한다 — 전투 중 들어오는 처치 재화와 섞여도 서로 덮어쓰지 않는다.
	//
	// 하위 클래스는 대상의 레벨 설정과 요청 전송만 구현한다. 대상은 long 키 하나로 식별한다(장비 uid, 펫 ID).
	public abstract class GrowthBatcher<T> : Singleton<T> where T : GrowthBatcher<T>
	{
		// 마지막 입력 후 이 시간(초) 동안 입력이 없으면 보낸다.
		private const float IdleFlushSeconds = 0.5f;

		private sealed class Batch
		{
			public long key;
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

		// 로그 머리말 — 하위 클래스 이름
		protected abstract string LogTag { get; }

		// 대상의 레벨을 바꾸고 화면·스탯 갱신을 알린다(적용·되돌림 공용). 대상이 사라졌으면 무시한다.
		protected abstract void SetLevel(long key, int level);

		// 묶음 1건을 보낸다. 응답에서 반드시 OnSendResult 를 한 번 부른다.
		protected abstract void Send(long key, int fromLevel, int count);

		// 강화 1회를 적용하고 묶음에 쌓는다 — 판정·비용·보유량 확인은 호출한 하위 클래스가 끝냈다.
		protected void Apply(long key, int currentLevel, List<UpgradeCost> costs)
		{
			// 다른 대상으로 넘어가면 앞 대상 묶음을 먼저 보낸다.
			if (_collecting != null && _collecting.key != key)
			{
				Flush();
			}

			if (_collecting == null)
			{
				_collecting = new Batch();
				_collecting.key = key;
				_collecting.fromLevel = currentLevel;
			}

			for (int i = 0; i < costs.Count; i++)
			{
				CurrencyManager.Instance.TrySpend(costs[i].currency, costs[i].amount);
				addCost(_collecting.spent, costs[i].currency, costs[i].amount);
			}

			_collecting.count++;
			SetLevel(key, currentLevel + 1);

			restartIdleTimer();
		}

		// 모은 묶음을 보낸다 — 대기 타이머 만료·대상 변경·화면 닫힘·앱 일시정지.
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

		// 전송 결과. serverLevel 이 음수면 서버 상태를 받지 못한 것(통신 실패·거절)이다.
		protected void OnSendResult(bool success, int serverLevel, CurrencyAmountDto[] serverSpent, string error)
		{
			Batch batch = _sending;
			_sending = null;

			if (batch == null)
			{
				return;
			}

			if (success == false || serverLevel < 0)
			{
				// 통신 실패·거절 — 이 묶음은 적용되지 않은 것으로 본다. 응답만 유실됐다면 다음 로드에서 서버 값으로 바로잡힌다.
				Debug.LogWarning($"[{LogTag}] 강화 묶음 실패 — 되돌림 key:{batch.key} {batch.fromLevel}→+{batch.count}: {error}");
				rollback(batch, batch.fromLevel, null);
			}
			else if (serverLevel != batch.fromLevel + batch.count)
			{
				// 부분 적용·레벨 어긋남 — 서버 상태로 맞추고 서버가 쓰지 않은 만큼 돌려준다.
				Debug.LogWarning($"[{LogTag}] 강화 묶음 불일치 — 서버 기준으로 맞춤 key:{batch.key} 기대 {batch.fromLevel + batch.count}, 서버 {serverLevel}");
				rollback(batch, serverLevel, serverSpent);
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

			Debug.Log($"[{LogTag}] 강화 묶음 전송 — key:{_sending.key} Lv.{_sending.fromLevel} +{_sending.count}");
			Send(_sending.key, _sending.fromLevel, _sending.count);
		}

		// batch 와 같은 대상의 뒤 묶음까지 되돌린다 — 레벨은 level 로, 재화는 미리 뺀 양에서 서버 차감분을 뺀 만큼 돌려준다.
		private void rollback(Batch batch, int level, CurrencyAmountDto[] serverSpent)
		{
			refund(batch.spent, serverSpent);
			discardFollowing(batch.key);
			SetLevel(batch.key, level);
		}

		// 같은 대상의 대기·수집 중 묶음을 버리고 그 재화를 돌려준다(fromLevel 이 틀어졌다).
		private void discardFollowing(long key)
		{
			int count = _ready.Count;
			for (int i = 0; i < count; i++)
			{
				Batch queued = _ready.Dequeue();
				if (queued.key == key)
				{
					refund(queued.spent, null);
					continue;
				}

				_ready.Enqueue(queued);
			}

			if (_collecting != null && _collecting.key == key)
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
	}
}
