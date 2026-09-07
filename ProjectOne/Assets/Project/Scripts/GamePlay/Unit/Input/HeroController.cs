using UnityEngine;
using UnityEngine.InputSystem;
using ProjectOne.Unit.Input;
using ProjectOne.CameraSystem;

namespace ProjectOne.Unit
{
	// 플레이어 단일 히어로 입력 컨트롤러 — IHeroInputProvider로부터 이동 입력을 받는다.
	// 입력 소스(KeyboardInputProvider 등)는 같은 GameObject에 함께 부착하며,
	// 키보드 provider는 시리얼라이즈된 InputActionAsset 데이터를 사용한다.
	//
	// 공격은 여기서 처리하지 않는다 — 조준·시전 모두 HeroAutoBehavior 가 담당한다.
	[RequireComponent(typeof(Hero))]
	public class HeroController : MonoBehaviour
	{
		Hero _hero;
		UnitMover _mover;
		IHeroInputProvider _input;

		void Awake()
		{
			_hero = GetComponent<Hero>();
			_mover = GetComponent<UnitMover>();
			_input = GetComponent<IHeroInputProvider>();
			if (_input == null)
			{
				Debug.LogError("[HeroController] IHeroInputProvider 컴포넌트가 없습니다.");
			}
		}

		void Update()
		{
			// 테스트용 — 1/2 키로 카메라 쉐이크 확인
			if (Keyboard.current != null)
			{
				if (Keyboard.current.digit1Key.wasPressedThisFrame == true)
				{
					CameraManager.Instance.Shake("shake_01");
				}

				if (Keyboard.current.digit2Key.wasPressedThisFrame == true)
				{
					CameraManager.Instance.Shake("shake_02");
				}
			}

			if (_hero.IsDead == true)
			{
				_mover.Stop();
				return;
			}

			// 스탯 주입(UnitFactory) 전에는 이동속도 조회 불가 → 대기
			if (_hero.Stats == null)
			{
				return;
			}

			if (_input == null)
			{
				return;
			}

			Vector2 move = _input.MoveInput;
			if (move.sqrMagnitude < 0.01f)
			{
				_mover.Stop();
				return;
			}

			_mover.Move(move, _hero.MoveSpeed);
		}
	}
}
