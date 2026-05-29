using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using EDT;
using ProjectOne.Input;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;

namespace ProjectOne.Unit
{
	// 플레이어 단일 히어로 입력 컨트롤러 — WASD 이동 / 스페이스 평타
	// IA_Character(Input Actions) 생성 래퍼를 소비. 영웅 프리팹에 부착.
	[RequireComponent(typeof(Hero))]
	public class HeroController : MonoBehaviour
	{
		Hero _hero;
		UnitMover _mover;
		IA_Character _input;

		void Awake()
		{
			_hero  = GetComponent<Hero>();
			_mover = GetComponent<UnitMover>();
			_input = new IA_Character();
		}

		void OnEnable()
		{
			_input.Player.Enable();
			_input.Player.Attack.performed += OnAttack;
		}

		void OnDisable()
		{
			_input.Player.Attack.performed -= OnAttack;
			_input.Player.Disable();
		}

		void OnDestroy()
		{
			_input.Dispose();
		}

		void Update()
		{
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

			Vector2 move = _input.Player.Move.ReadValue<Vector2>();
			if (move.sqrMagnitude < 0.01f)
			{
				_mover.Stop();
				return;
			}

			_mover.Move(move, _hero.Stats.GetStat(StatTypes.MoveSpeed));
		}

		// 스페이스 입력 → 보유 첫 스킬(평타) 발동. 범위 밖 적이어도 TryCast가 그대로 실행.
		void OnAttack(InputAction.CallbackContext ctx)
		{
			if (_hero.IsDead == true || _hero.SkillContainer == null)
			{
				return;
			}

			IReadOnlyList<SkillInfo> all = _hero.SkillContainer.GetAll();
			if (all.Count > 0)
			{
				_hero.SkillContainer.TryCast(all[0]);
			}
		}
	}
}
