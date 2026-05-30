using System;
using UnityEngine;

namespace ProjectOne.Unit.Input
{
	public interface IHeroInputProvider
	{
		Vector2 MoveInput { get; }

		event Action OnAttack;
	}
}
