using UnityEngine;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 보스 전멸기 파훼용 기믹 코어.
	//
	// 접촉 즉시가 아니라 **범위 안에 _holdTime 만큼 머물러야** 1개로 인정되고 사라진다.
	// 도중에 범위를 벗어나면 진행도는 0으로 리셋된다 — 보스 공격을 피하면서 버티게 하는 것이 의도다.
	// _holdTime 이 0 이면 베이스의 즉시 획득 경로를 그대로 탄다.
	//
	// 수명(_lifetime)은 0으로 두고 전환 시퀀스가 Recall 로 회수한다 —
	// 파훼 제한시간은 전멸기의 캐스팅 시간이지 코어 수명이 아니다.
	//
	// 판정에 OnTriggerStay2D 를 쓰지 않는 이유 — Kinematic Rigidbody2D 가 잠들면
	// Stay 콜백이 끊긴다. Enter/Exit 카운팅 + Tick 누산이 안전하다.
	//
	// 진행도는 코어마다 자기 게이지(BossGimmickGauge)로 보여준다 — 코어가 여럿 떠 있고
	// 어느 것을 얼마나 밟았는지 각각 보여야 하므로 공용 InteractionGauge 를 쓰지 않는다.
	public class BossGimmickCore : DropObject
	{
		[Header("파훼 판정")]
		// 범위 안에 머물러야 하는 시간(초). 0 이면 접촉 즉시 인정된다.
		[SerializeField] private float _holdTime = 2f;

		private IBossGimmickListener _listener;

		// 범위 안에 있는 히어로 수. 1명이라도 있으면 진행된다.
		private int _inside;

		// 누적 유지 시간
		private float _held;

		// 이 코어 위에 뜨는 전용 게이지. 스폰 즉시 만들어 두고 0% 로 보여준다.
		private BossGimmickGauge _gauge;

		protected override bool PickupOnTouch
		{
			get { return _holdTime <= 0f; }
		}

		// 풀에서 꺼낼 때 이전 생의 진행도와 리스너가 남지 않게 비운다.
		public override void Initialize(DropObjectPool pool)
		{
			base.Initialize(pool);
			_listener = null;
			_inside = 0;
			_held = 0f;

			// 밟기 전에도 어디를 밟아야 하는지 보이도록 스폰 즉시 0% 게이지를 띄운다.
			// 즉시 획득(_holdTime 0)은 진행도가 없으므로 만들지 않는다.
			if (_holdTime > 0f && UIManager.HasInstance == true)
			{
				_gauge = UIManager.Instance.CreateBossGimmickGauge();
				if (_gauge != null)
				{
					_gauge.Attach(transform);
				}
			}
		}

		// DropObjectPool.Spawn 직후 전환 시퀀스가 자기 자신을 등록한다.
		public void SetListener(IBossGimmickListener listener)
		{
			_listener = listener;
		}

		// 파훼가 끝났을 때(성공·실패 무관) — 통지 없이 회수한다.
		public void Recall()
		{
			_listener = null;
			ReleaseSelf();
		}

		protected override void OnHeroEnter(UnitBase hero)
		{
			_inside++;
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			if (IsReleased == true)
			{
				return;
			}

			UnitBase hero;
			if (TryGetHero(other, out hero) == false)
			{
				return;
			}

			_inside--;
			if (_inside > 0)
			{
				return;
			}

			// 아무도 없으면 처음부터 다시 채워야 한다.
			_inside = 0;
			_held = 0f;

			if (_gauge != null)
			{
				_gauge.SetProgress(0f);
			}
		}

		protected override void Tick(float dt)
		{
			if (_holdTime <= 0f || _inside <= 0)
			{
				return;
			}

			_held += dt;

			if (_gauge != null)
			{
				_gauge.SetProgress(_held / _holdTime);
			}

			if (_held < _holdTime)
			{
				return;
			}

			complete();
		}

		// _holdTime 이 0 일 때만 베이스가 부른다.
		protected override void OnPickup(UnitBase hero)
		{
			notifyOnce();
		}

		private void complete()
		{
			PlayPickupFeedback();
			ReleaseSelf();
			notifyOnce();
		}

		// 게이지를 걷는 코드가 없는 이유 — 완료·회수 어느 쪽이든 ReleaseSelf 로 이 오브젝트가
		// 비활성화되고, 게이지가 그것을 보고 스스로 파괴된다 (BossGimmickGauge.LateUpdate).
		// 씬 전환으로 코어가 사라지는 경우까지 같은 한 곳이 덮는다.

		// 통지 전에 리스너를 끊는다 — 회수 경로와 겹쳐 두 번 세지는 것을 막는다.
		private void notifyOnce()
		{
			if (_listener == null)
			{
				return;
			}

			IBossGimmickListener listener = _listener;
			_listener = null;
			listener.OnGimmickActivated();
		}
	}
}
