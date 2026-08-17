using System.Collections.Generic;

namespace ProjectOne.Unit.AI.BT
{
	// 노드 실행 결과. Running 을 두는 이유는 여러 틱에 걸치는 행동(접근·캐스팅)을 표현하기 위해서다.
	public enum BTStatus
	{
		Failure = 0,
		Success,
		Running
	}

	// BT 실행 문맥 — 노드가 소유자·블랙보드·델타타임에 접근하는 통로.
	// struct 로 두어 노드마다 인자 3개를 들고 다니지 않게 한다.
	public readonly struct BTContext
	{
		public readonly UnitBase Self;
		public readonly Blackboard Blackboard;
		public readonly float DeltaTime;

		public BTContext(UnitBase self, Blackboard blackboard, float deltaTime)
		{
			Self = self;
			Blackboard = blackboard;
			DeltaTime = deltaTime;
		}
	}

	public interface IBTNode
	{
		BTStatus Tick(in BTContext ctx);
	}

	// 자식을 순서대로 시도해 **처음 성공/실행중인 것**에서 멈춘다 — "이것 아니면 저것".
	public sealed class BTSelector : IBTNode
	{
		private readonly IBTNode[] _children;

		public BTSelector(params IBTNode[] children)
		{
			_children = children ?? new IBTNode[0];
		}

		public BTStatus Tick(in BTContext ctx)
		{
			for (int i = 0; i < _children.Length; i++)
			{
				BTStatus status = _children[i].Tick(in ctx);
				if (status != BTStatus.Failure)
				{
					return status;
				}
			}

			return BTStatus.Failure;
		}
	}

	// 자식을 순서대로 실행해 **하나라도 실패하면 중단**한다 — "이것 하고 저것".
	public sealed class BTSequence : IBTNode
	{
		private readonly IBTNode[] _children;

		public BTSequence(params IBTNode[] children)
		{
			_children = children ?? new IBTNode[0];
		}

		public BTStatus Tick(in BTContext ctx)
		{
			for (int i = 0; i < _children.Length; i++)
			{
				BTStatus status = _children[i].Tick(in ctx);
				if (status != BTStatus.Success)
				{
					return status;
				}
			}

			return BTStatus.Success;
		}
	}

	// 조건 — 참이면 Success, 거짓이면 Failure. 부수효과를 두지 않는다.
	public sealed class BTCondition : IBTNode
	{
		public delegate bool Predicate(in BTContext ctx);

		private readonly Predicate _predicate;

		public BTCondition(Predicate predicate)
		{
			_predicate = predicate;
		}

		public BTStatus Tick(in BTContext ctx)
		{
			return (_predicate != null && _predicate(in ctx)) ? BTStatus.Success : BTStatus.Failure;
		}
	}

	// 실제 행동. 여러 틱에 걸치면 Running 을 돌려준다.
	public sealed class BTAction : IBTNode
	{
		public delegate BTStatus Act(in BTContext ctx);

		private readonly Act _action;

		public BTAction(Act action)
		{
			_action = action;
		}

		public BTStatus Tick(in BTContext ctx)
		{
			return (_action != null) ? _action(in ctx) : BTStatus.Failure;
		}
	}
}
