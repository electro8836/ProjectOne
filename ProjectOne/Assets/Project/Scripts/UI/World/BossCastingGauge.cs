using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Monsters;
using ProjectOne.Skill;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 보스 머리 위 패턴 캐스팅 게이지.
	//
	// **페이즈 전환 패턴(전멸기)에서만 뜬다.** 보스의 평범한 캐스팅 스킬은 무시한다 —
	// 이 게이지의 의미가 "언제까지 파훼해야 하는가"이기 때문이다.
	// 판별은 BossMonsterPhase.PhaseSkillID 와 대조해서 한다.
	//
	// 스스로 구동한다 — SkillCastEvent 를 구독하므로 게임플레이 쪽이 이 UI 를 알 필요가 없다
	// (BossUI 가 UnitSpawnedEvent 를 구독하던 것과 같은 결).
	//
	// 진행도는 **캐스팅 시간(CastingParam)** 기준이다. SkillIndicator 의 채움은 실제 타격
	// 시점(CastingParam + 모션×EffectTime)까지 차지만, 이 게이지는 "언제까지 파훼해야 하는가"를
	// 뜻해야 하므로 시전 구간만 쓴다.
	public class BossCastingGauge : MonoBehaviour
	{
		[Header("구성")]
		// 표시 단위. 이 오브젝트를 켜고 끄며, 위치도 이것을 옮긴다.
		// 컴포넌트 자신이 붙은 오브젝트여도 된다 — 구독이 Awake/OnDestroy 라 꺼져도 유지된다.
		[SerializeField] private GameObject _root;
		// 진행도 표시 (BossUI 의 체력 슬라이더와 같은 방식)
		[SerializeField] private Slider _slider;
		// 진행도 표시 텍스트 — "55%" 형태. 선택이라 비워도 동작한다.
		[SerializeField] private TMP_Text _label;

		[Header("배치")]
		// 대상 HitCenter 로부터 띄울 높이(월드 유닛)
		[SerializeField] private float _yOffset = 0.9f;

		private UnitBase _boss;
		private EDT.Skill _skillId = EDT.Skill.None;
		private float _castTime;
		private float _lastProgress = -1f;

		// 퍼센트 표시는 정수가 바뀔 때만 갱신한다 — 매 프레임 문자열을 만들지 않는다.
		private int _lastPercent = -1;

		// 구독을 OnEnable 이 아니라 Awake 에 두는 이유 —
		// _root 가 이 컴포넌트 자신의 오브젝트일 수 있고, 그때 Hide 가 자기를 꺼버린다.
		// OnDisable 에서 구독을 풀면 다시 켜 줄 이벤트를 영영 못 받는다 (BossUI 와 같은 처리).
		private void Awake()
		{
			EventManager.Instance.Subscribe<SkillCastEvent>(onSkillCast);
			hide();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<SkillCastEvent>(onSkillCast);
		}

		private void onSkillCast(SkillCastEvent evt)
		{
			// 이벤트는 오브젝트가 꺼져 있어도 들어온다 — 여기서 다시 켠다.
			if (_root == null || evt.Caster == null)
			{
				return;
			}

			if (evt.Caster.MonsterType != MonsterType.Boss)
			{
				return;
			}

			ResolvedSkill resolved = evt.Caster.Resolve(evt.SkillId);
			if (resolved == null || resolved.IsValid == false)
			{
				return;
			}

			// 패턴(페이즈 전환 전멸기)이 아니면 무시한다.
			if (isPhaseSkill(evt.Caster.GetTableID(), evt.SkillId) == false)
			{
				return;
			}

			Table_Skill.Row row = resolved.Row;

			// 캐스팅형만 게이지를 띄운다 — 즉시 발동 스킬은 보여줄 시간이 없다.
			if (row.CastingType != SkillCastingTypes.Casting || row.CastingParam <= 0f)
			{
				return;
			}

			_boss = evt.Caster;
			_skillId = evt.SkillId;
			_castTime = row.CastingParam;
			_lastProgress = -1f;
			_lastPercent = -1;

			setProgress(0f);
			follow();

			_root.SetActive(true);
		}

		// 유닛이 움직인 뒤에 따라가야 하므로 LateUpdate 다.
		private void LateUpdate()
		{
			if (_root == null || _root.activeSelf == false)
			{
				return;
			}

			if (_boss == null || _boss.IsDead == true)
			{
				hide();
				return;
			}

			SkillContainer sc = _boss.SkillContainer;

			// 시전이 끝났거나 다른 스킬로 넘어갔으면 걷는다 — 파훼로 취소된 경우도 여기로 온다.
			if (sc == null || sc.IsCasting == false || sc.CastingSkillId != _skillId)
			{
				hide();
				return;
			}

			setProgress(1f - sc.CastRemaining / _castTime);
			follow();
		}

		private void setProgress(float t)
		{
			if (_slider == null)
			{
				return;
			}

			float clamped = Mathf.Clamp01(t);
			if (Mathf.Approximately(clamped, _lastProgress) == true)
			{
				return;
			}

			_lastProgress = clamped;
			_slider.value = clamped;

			if (_label == null)
			{
				return;
			}

			int percent = Mathf.RoundToInt(clamped * 100f);
			if (percent == _lastPercent)
			{
				return;
			}

			_lastPercent = percent;
			_label.text = percent.ToString() + "%";
		}

		// 이 스킬이 그 보스의 페이즈 전환 패턴인가. 페이즈가 없는 몬스터는 빈 목록이라 항상 false 다.
		private static bool isPhaseSkill(int monsterId, EDT.Skill skillId)
		{
			IReadOnlyList<Table_BossMonsterPhase.Row> phases = MonsterCatalog.GetBossPhases(monsterId);
			for (int i = 0; i < phases.Count; i++)
			{
				if (phases[i].PhaseSkillID == skillId)
				{
					return true;
				}
			}

			return false;
		}

		private void follow()
		{
			Vector2 center = _boss.HitCenter;
			_root.transform.position = new Vector3(center.x, center.y + _yOffset, _root.transform.position.z);
		}

		private void hide()
		{
			_boss = null;
			_skillId = EDT.Skill.None;
			_castTime = 0f;
			_lastProgress = -1f;
			_lastPercent = -1;

			if (_root != null)
			{
				_root.SetActive(false);
			}
		}
	}
}
