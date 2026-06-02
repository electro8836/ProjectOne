using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.CameraSystem
{
	// 게임플레이 카메라 총괄 — Cinemachine 3 위에 얹은 추종/쉐이크/줌 진입점.
	// - 추종: UnitSpawnedEvent(Hero)로 타겟을 잡고, 카메라-타겟 평면 거리가 임계값을 넘으면
	//         위치 댐핑을 0으로 떨궈 즉시 따라붙(대쉬 등 급이동), 이내면 설정 댐핑으로 부드럽게.
	// - 쉐이크: shake_NN 프리펩(CinemachineImpulseSource 프리셋)을 캐시해 GenerateImpulse 호출.
	//           (가상 카메라에 CinemachineImpulseListener가 있어야 화면에 반영된다)
	// - 줌: 가상 카메라 OrthographicSize 를 코루틴으로 보간 (보스/기믹 연출용).
	public sealed class CameraManager : MonoSingleton<CameraManager>
	{
		// id ↔ 쉐이크 프리펩 매핑 (인스펙터 등록용)
		[System.Serializable]
		private struct ShakePreset
		{
			public string Id;
			// CinemachineImpulseSource를 보유한 프리펩 (shake_NN)
			public GameObject Prefab;
		}

		[Header("Cinemachine 참조")]
		[SerializeField] private CinemachineCamera _vcam;

		[Header("추종 — 거리 기반 스냅")]
		// 카메라-타겟 평면 거리가 이 값을 넘으면 즉시 따라붙는다(대쉬 등 급이동 대응)
		[SerializeField] private float _snapDistance = 3f;
		// 일반 이동 시 적용할 부드러운 위치 댐핑(축별, 초 단위)
		[SerializeField] private Vector3 _smoothDamping = new Vector3(0.4f, 0.4f, 0f);

		[Header("줌")]
		// 기본 직교 줌 크기 (ZoomReset 복귀 기준). 0 이하면 시작 시 vcam 값을 사용한다
		[SerializeField] private float _defaultOrthoSize = 5f;

		[Header("쉐이크 프리셋")]
		[SerializeField] private List<ShakePreset> _shakePresets = new List<ShakePreset>();

		private CinemachineFollow _follow;
		private Transform _target;
		private Coroutine _zoomRoutine;

		// id → 인스턴스화한 ImpulseSource (최초 1회 생성 후 재사용)
		private readonly Dictionary<string, CinemachineImpulseSource> _shakeSources =
			new Dictionary<string, CinemachineImpulseSource>();

		protected override void Awake()
		{
			base.Awake();

			// 중복 인스턴스라 파괴 예정이면(_instance != this) 초기화 생략
			if (Instance != this)
			{
				return;
			}

			if (_vcam != null)
			{
				_follow = _vcam.GetComponent<CinemachineFollow>();
				if (_defaultOrthoSize <= 0f)
				{
					_defaultOrthoSize = _vcam.Lens.OrthographicSize;
				}
			}

			BuildShakeSources();
		}

		private void OnEnable()
		{
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(OnUnitSpawned);
		}

		private void OnDisable()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(OnUnitSpawned);
		}

		// 매 프레임 추종 오차를 보고 댐핑을 전환 — 멀어지면 즉시(스냅), 가까우면 부드럽게
		private void LateUpdate()
		{
			if (_follow == null || _target == null)
			{
				return;
			}

			Vector2 camXY = _vcam.transform.position;
			Vector2 targetXY = _target.position;
			float distance = Vector2.Distance(camXY, targetXY);

			if (distance > _snapDistance)
			{
				_follow.TrackerSettings.PositionDamping = Vector3.zero;
			}
			else
			{
				_follow.TrackerSettings.PositionDamping = _smoothDamping;
			}
		}

		// ── 추종 ──────────────────────────────────────────────────────

		// 씬 종속 가상 카메라를 런타임에 등록 (영속 매니저 ↔ 씬마다 생멸하는 vcam 연결).
		// Battle 진입 시 CM_GameplayCam의 GameplayCameraRig가 OnEnable에서 호출한다.
		public void SetVcam(CinemachineCamera vcam)
		{
			if (vcam == null)
			{
				return;
			}

			_vcam = vcam;
			_follow = _vcam.GetComponent<CinemachineFollow>();
			if (_defaultOrthoSize <= 0f)
			{
				_defaultOrthoSize = _vcam.Lens.OrthographicSize;
			}

			// 이미 추종 대상이 있으면(이벤트가 vcam 등록보다 먼저 온 경우) 즉시 재적용
			if (_target != null)
			{
				SetFollowTarget(_target);
			}
		}

		// 등록 해제 — 현재 들고 있는 vcam과 같을 때만 (늦게 파괴되는 이전 vcam이 새 vcam을 덮어쓰지 않도록)
		public void ClearVcam(CinemachineCamera vcam)
		{
			if (_vcam != vcam)
			{
				return;
			}

			_vcam = null;
			_follow = null;
		}

		// 외부에서 추종 대상을 직접 설정 (이벤트 외 수동 지정용)
		public void SetFollowTarget(Transform target)
		{
			_target = target;
			if (_vcam == null)
			{
				return;
			}

			_vcam.Follow = target;
			if (target == null)
			{
				return;
			}

			// 직전 타겟 위치에서 끌려오지 않도록 새 타겟으로 즉시 스냅
			Vector3 position = target.position;
			if (_follow != null)
			{
				position += _follow.FollowOffset;
			}

			_vcam.ForceCameraPosition(position, _vcam.transform.rotation);
		}

		private void OnUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero || e.Unit == null)
			{
				return;
			}

			SetFollowTarget(e.Unit.transform);
		}

		// ── 쉐이크 ────────────────────────────────────────────────────

		// 등록된 프리셋 id로 카메라 쉐이크 발생 (예: Shake("shake_01"))
		public void Shake(string id)
		{
			if (string.IsNullOrEmpty(id) == true)
			{
				return;
			}

			CinemachineImpulseSource source;
			if (_shakeSources.TryGetValue(id, out source) == false || source == null)
			{
				Debug.LogWarning($"[CameraManager] 등록되지 않은 쉐이크 id — {id}");
				return;
			}

			source.GenerateImpulse();
		}

		// 쉐이크 프리펩을 인스턴스화해 자식으로 보관 (id로 재사용)
		private void BuildShakeSources()
		{
			for (int i = 0; i < _shakePresets.Count; i++)
			{
				ShakePreset preset = _shakePresets[i];
				if (string.IsNullOrEmpty(preset.Id) == true || preset.Prefab == null)
				{
					continue;
				}

				if (_shakeSources.ContainsKey(preset.Id) == true)
				{
					Debug.LogWarning($"[CameraManager] 중복 쉐이크 id 무시 — {preset.Id}");
					continue;
				}

				GameObject instance = Instantiate(preset.Prefab, transform);
				CinemachineImpulseSource source = instance.GetComponent<CinemachineImpulseSource>();
				if (source == null)
				{
					Debug.LogWarning($"[CameraManager] CinemachineImpulseSource 없는 쉐이크 프리펩 — {preset.Id}");
					Destroy(instance);
					continue;
				}

				_shakeSources.Add(preset.Id, source);
			}
		}

		// ── 줌 ────────────────────────────────────────────────────────

		// 지정 직교 크기로 duration 초 동안 부드럽게 줌 (duration<=0이면 즉시 적용)
		public void ZoomTo(float orthoSize, float duration)
		{
			StartZoom(orthoSize, duration);
		}

		// 기본 줌 크기로 복귀
		public void ZoomReset(float duration)
		{
			StartZoom(_defaultOrthoSize, duration);
		}

		private void StartZoom(float targetSize, float duration)
		{
			if (_vcam == null)
			{
				return;
			}

			if (_zoomRoutine != null)
			{
				StopCoroutine(_zoomRoutine);
				_zoomRoutine = null;
			}

			if (duration <= 0f)
			{
				_vcam.Lens.OrthographicSize = targetSize;
				return;
			}

			_zoomRoutine = StartCoroutine(ZoomRoutine(targetSize, duration));
		}

		private IEnumerator ZoomRoutine(float targetSize, float duration)
		{
			float startSize = _vcam.Lens.OrthographicSize;
			float elapsed = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);

				// SmoothStep 가감속
				t = t * t * (3f - 2f * t);
				_vcam.Lens.OrthographicSize = Mathf.Lerp(startSize, targetSize, t);
				yield return null;
			}

			_vcam.Lens.OrthographicSize = targetSize;
			_zoomRoutine = null;
		}
	}
}
