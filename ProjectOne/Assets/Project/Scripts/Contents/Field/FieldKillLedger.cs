using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Field
{
	// 필드 처치 원장 — 시드 기반 배치 정산의 클라 쪽 기록.
	//
	// 처치마다 killIndex 를 발급하고, 그 처치에서 떨어진 드랍이 **모두 정리될 때까지**(주움·소멸·씬 이탈) 붙잡아 둔다.
	// 정리된 처치는 killIndex 순서대로 앞에서부터 묶어 서버로 보낸다 — 서버는 nextKillIndex 부터 연속된 배치만 받는다.
	// 서버는 같은 시드로 추첨을 재현해 pickedMask 의 보상만 지급하므로, 클라가 결과를 위조할 수 없다.
	//
	// 로그인 세션(서버 발급 seed)이 없으면 비활성이다 — 그때는 처치 보상이 예전처럼 로컬로만 지급된다.
	public sealed class FieldKillLedger : Singleton<FieldKillLedger>
	{
		// pickedMask(long) 의 비트 수 — 처치 1건에서 추적할 수 있는 보상 수 상한.
		public const int MaxTrackedRewards = 64;

		// 한 배치에 싣는 최대 처치 수 — 페이로드와 서버 처리 시간을 묶어 둔다.
		private const int MaxBatchKills = 200;

		// 세션 교체 때 한 번에 싣는 최대 처치 수 — 넘치는 분은 교체 후 버려진다(로딩 직전 대량 미전송은 사실상 없다).
		private const int MaxRotateKills = 500;

		// 드랍 수명(60초) 뒤에도 정리 신호가 오지 않은 처치는 미획득으로 확정한다(신호 누락 대비 안전망).
		private const float ForceResolveSeconds = 90f;

		private sealed class KillRecord
		{
			public FieldKillDto dto;
			public int rewardCount;
			public long resolvedMask;
			public int exp;
			public float createdAt;
			public bool sending;
		}

		private readonly List<KillRecord> _kills = new List<KillRecord>();

		private long _seed;
		private int _epoch;
		private int _nextKillIndex;
		private bool _active;

		private FieldKillLedger() { }

		public bool IsActive
		{
			get { return _active; }
		}

		public long Seed
		{
			get { return _seed; }
		}

		public int Epoch
		{
			get { return _epoch; }
		}

		public int NextKillIndex
		{
			get { return _nextKillIndex; }
		}

		// 아직 서버에 반영되지 않은 처치 경험치 합 — 서버 권위 경험치로 덮을 때 더해 줘야 줄어들지 않는다.
		public int UnsettledExp
		{
			get
			{
				int sum = 0;
				for (int i = 0; i < _kills.Count; i++)
				{
					sum += _kills[i].exp;
				}

				return sum;
			}
		}

		// 로그인 직후 서버 세션으로 시작한다. 이전 세션의 미전송 처치는 버린다(서버도 새 세션이다).
		public void Begin(FieldSessionDto session)
		{
			_kills.Clear();
			_active = session != null && session.seed != 0;
			if (_active == false)
			{
				return;
			}

			_seed = session.seed;
			_epoch = session.epoch;
			_nextKillIndex = session.nextKillIndex;
		}

		// 처치 1건을 등록하고 killIndex 를 소비한다. 호출 전에 NextKillIndex 로 시드를 만들어 굴려 둔다.
		public void Register(FieldKillDto dto, int rewardCount, int exp)
		{
			KillRecord record = new KillRecord();
			record.dto = dto;
			record.rewardCount = rewardCount > MaxTrackedRewards ? MaxTrackedRewards : rewardCount;
			record.exp = exp;
			record.createdAt = Time.realtimeSinceStartup;
			_kills.Add(record);

			_nextKillIndex = dto.killIndex + 1;
		}

		// 드랍 하나의 결과를 기록한다. 이미 정리된 보상이면 false — 호출자는 지급하지 않는다
		// (서버에 미획득으로 이미 확정됐거나 보낸 뒤라 로컬만 받으면 어긋난다).
		public bool TryResolve(int killIndex, int rewardIndex, bool picked)
		{
			KillRecord record = find(killIndex);
			if (record == null || rewardIndex < 0 || rewardIndex >= record.rewardCount || record.sending == true)
			{
				return false;
			}

			long bit = 1L << rewardIndex;
			if ((record.resolvedMask & bit) != 0)
			{
				return false;
			}

			record.resolvedMask |= bit;
			if (picked == true)
			{
				record.dto.pickedMask |= bit;
			}

			return true;
		}

		// 남은 드랍을 전부 미획득으로 확정한다 — 씬 이탈(드랍 풀 파괴)·앱 종료 시점.
		public void ResolveAll()
		{
			for (int i = 0; i < _kills.Count; i++)
			{
				_kills[i].resolvedMask = fullMask(_kills[i].rewardCount);
			}
		}

		// 앞에서부터 연속으로 정리된 처치를 묶는다. 전송 중인 배치가 있거나 보낼 것이 없으면 null.
		public FieldKillDto[] BuildBatch()
		{
			if (_active == false || _kills.Count == 0 || _kills[0].sending == true)
			{
				return null;
			}

			float now = Time.realtimeSinceStartup;
			int count = 0;
			for (int i = 0; i < _kills.Count && count < MaxBatchKills; i++)
			{
				KillRecord record = _kills[i];
				if (now - record.createdAt >= ForceResolveSeconds)
				{
					record.resolvedMask = fullMask(record.rewardCount);
				}

				if (record.resolvedMask != fullMask(record.rewardCount))
				{
					break;
				}

				count++;
			}

			if (count == 0)
			{
				return null;
			}

			FieldKillDto[] batch = new FieldKillDto[count];
			for (int i = 0; i < count; i++)
			{
				_kills[i].sending = true;
				batch[i] = _kills[i].dto;
			}

			return batch;
		}

		// 로딩(세션 교체) 시점 — 전송 중이 아닌 처치를 전부 꺼낸다. ResolveAll 직후라 모두 정리된 상태다.
		// 주기 배치가 전송 중이면 그 처치는 SendQueue 순서상 서버가 먼저 처리하므로 여기서 빼고 보낸다.
		public FieldKillDto[] TakeRemaining()
		{
			List<FieldKillDto> batch = new List<FieldKillDto>();
			for (int i = 0; i < _kills.Count && batch.Count < MaxRotateKills; i++)
			{
				KillRecord record = _kills[i];
				if (record.sending == true)
				{
					continue;
				}

				record.sending = true;
				batch.Add(record.dto);
			}

			return batch.ToArray();
		}

		// 서버 응답 — serverNextKillIndex 미만은 서버에 반영(또는 거절로 폐기)됐으므로 지운다.
		public void OnSettled(int serverNextKillIndex)
		{
			int removeCount = 0;
			while (removeCount < _kills.Count && _kills[removeCount].dto.killIndex < serverNextKillIndex)
			{
				removeCount++;
			}

			if (removeCount > 0)
			{
				_kills.RemoveRange(0, removeCount);
			}

			clearSending();
		}

		// 통신 실패 — 아무것도 반영되지 않았으므로 다음 기회에 같은 배치를 다시 보낸다.
		public void OnSendFailed()
		{
			clearSending();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private KillRecord find(int killIndex)
		{
			if (_kills.Count == 0)
			{
				return null;
			}

			int offset = killIndex - _kills[0].dto.killIndex;
			if (offset < 0 || offset >= _kills.Count)
			{
				return null;
			}

			return _kills[offset];
		}

		private void clearSending()
		{
			for (int i = 0; i < _kills.Count; i++)
			{
				_kills[i].sending = false;
			}
		}

		private static long fullMask(int rewardCount)
		{
			if (rewardCount <= 0)
			{
				return 0;
			}

			if (rewardCount >= MaxTrackedRewards)
			{
				return -1L;
			}

			return (1L << rewardCount) - 1;
		}
	}
}
