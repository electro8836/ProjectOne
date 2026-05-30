using System;
using UnityEngine;

namespace ProjectOne.Unit.Input
{
	public class TouchInputProvider : MonoBehaviour, IHeroInputProvider
	{
		private Vector2 _moveInput;

		public Vector2 MoveInput => _moveInput;

		public event Action OnAttack;

		public void SetMoveInput(Vector2 direction)
		{
			_moveInput = direction;
		}

		public void NotifyAttack()
		{
			this.OnAttack?.Invoke();
		}
	}
}
