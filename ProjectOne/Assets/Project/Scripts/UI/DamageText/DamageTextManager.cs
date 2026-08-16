using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 데미지 텍스트 진입점. DamageTakenEvent를 구독해 피격 유닛 위로 데미지 숫자를 띄운다.
	// 전투 씬의 WorldUICanvas 하위에 배치되어 씬과 생명주기를 함께한다.
	public class DamageTextManager : MonoBehaviour
	{
		[SerializeField] private DamageTextPool _pool;
		[SerializeField] private float _offsetY = 0.0f;		// 콜라이더 반지름 위로 추가로 올릴 거리
		[SerializeField] private Color _normalColor = Color.white;
		[SerializeField] private Color _criticalColor = new Color(1f, 0.55f, 0f, 1f);			// 주황

		[SerializeField] private Color _heroNormalColor = new Color(1f, 0.2f, 0.2f, 1f);		// 빨강(Hero 피격)
		[SerializeField] private Color _heroCriticalColor = new Color(1f, 0.2f, 0.8f, 1f);		// 자홍(Hero 크리)

		[SerializeField] private float _stackSpacing = 0.35f;		// 동시 데미지 간 세로 간격(월드 단위)
		[SerializeField] private int _maxStack = 5;					// 스택 최대 칸 수(초과 시 0부터 순환)
		[SerializeField] private float _cullingMargin = 0.05f;		// 화면 경계 컬링 여유값

		// 유닛(InstanceID) → 마지막 스폰 프레임 / 현재 스택 인덱스
		private readonly Dictionary<int, StackInfo> _stacks = new Dictionary<int, StackInfo>();

		private Camera _mainCamera;

		private struct StackInfo
		{
			public int LastFrame;
			public int Index;
		}

		private void Awake()
		{
			_mainCamera = Camera.main;

			EventManager.Instance.Subscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);
		}

		private void Update()
		{
			_pool.TickAll(Time.deltaTime);
		}

		private void onDamageTaken(DamageTakenEvent e)
		{
			if (e.Target == null)
			{
				return;
			}

			// 한 스킬이 같은 프레임에 적용하는 동시 데미지만 위로 한 칸씩 쌓는다 (최대치에서 순환)
			// 다음 타격은 다른 프레임 → index=0으로 리셋되어 항상 기준 위치부터 시작
			int id = e.Target.GetID();
			int index = 0;
			if (_stacks.TryGetValue(id, out StackInfo info) && info.LastFrame == Time.frameCount)
			{
				index = (info.Index + 1) % _maxStack;
			}

			StackInfo next;
			next.LastFrame = Time.frameCount;
			next.Index = index;
			_stacks[id] = next;

			float upOffset = e.Target.Radius * 2 + _offsetY + index * _stackSpacing;
			Vector3 worldPos = e.Target.transform.position + Vector3.up * upOffset;

			if (isOffscreen(worldPos))
			{
				return;
			}

			// Hero 피격 여부에 따라 색상 세트 분기
			bool isHero = e.Target.GetUnitType() == UnitType.Hero;
			Color color;
			if (isHero)
			{
				color = e.IsCritical ? _heroCriticalColor : _heroNormalColor;
			}
			else
			{
				color = e.IsCritical ? _criticalColor : _normalColor;
			}

			_pool.Spawn(worldPos, e.Damage.ToString(), color);
		}

		private void onUnitDied(UnitDiedEvent e)
		{
			_stacks.Remove(e.InstanceID);
		}

		private bool isOffscreen(Vector3 worldPos)
		{
			if (_mainCamera == null)
			{
				_mainCamera = Camera.main;
				if (_mainCamera == null)
				{
					return false;
				}
			}

			Vector3 vp = _mainCamera.WorldToViewportPoint(worldPos);
			return vp.z < 0f
				|| vp.x < -_cullingMargin || vp.x > 1f + _cullingMargin
				|| vp.y < -_cullingMargin || vp.y > 1f + _cullingMargin;
		}
	}
}
