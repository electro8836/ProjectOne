using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectOne.Unit.Input
{
	public class KeyboardInputProvider : MonoBehaviour, IHeroInputProvider
	{
		[SerializeField]
		private InputActionAsset _inputAsset;

		private const float AttackInterval = 0.1f;

		private InputAction _moveAction;

		private InputAction _attackAction;

		private InputActionMap _playerMap;

		private bool _attackHeld;

		private float _attackTimer;

		public Vector2 MoveInput => _moveAction.ReadValue<Vector2>();

		public event Action OnAttack;

		private void Awake()
		{
			_playerMap = _inputAsset.FindActionMap("Player", throwIfNotFound: true);
			_moveAction = _playerMap.FindAction("Move", throwIfNotFound: true);
			_attackAction = _playerMap.FindAction("Attack", throwIfNotFound: true);
		}

		private void OnEnable()
		{
			_playerMap.Enable();
			_attackAction.started += HandleAttackStarted;
			_attackAction.canceled += HandleAttackCanceled;
		}

		private void OnDisable()
		{
			_attackAction.started -= HandleAttackStarted;
			_attackAction.canceled -= HandleAttackCanceled;
			_playerMap.Disable();
			_attackHeld = false;
		}

		private void Update()
		{
			if (_attackHeld)
			{
				_attackTimer += Time.deltaTime;
				if (_attackTimer >= 0.1f)
				{
					_attackTimer -= 0.1f;
					this.OnAttack?.Invoke();
				}
			}
		}

		private void HandleAttackStarted(InputAction.CallbackContext ctx)
		{
			_attackHeld = true;
			_attackTimer = 0.1f;
			this.OnAttack?.Invoke();
		}

		private void HandleAttackCanceled(InputAction.CallbackContext ctx)
		{
			_attackHeld = false;
		}
	}
}
