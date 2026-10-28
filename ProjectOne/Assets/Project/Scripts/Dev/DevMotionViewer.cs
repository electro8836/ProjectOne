using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using ProjectOne.Data;
using ProjectOne.Unit;

namespace ProjectOne.Dev
{
	// [개발용] Dev_Motion 씬 — 히어로 1명의 방향을 슬라이더로 돌려 가며 무기 모션을 본다.
	//
	// 스탯·스킬 컨테이너 없이 UnitAnimator 만 직접 몬다. 공격 배속·주기는 SkillExecutor 와 같은 공식이다:
	// 주기 = Cooldown ÷ 공속, 무기 클립 배속 = AnimLength ÷ 주기. Cooldown·AnimLength·ComboSteps 는 Skill 테이블 값을 읽는다.
	// 이동은 제자리 걷기다 — 이동 상태·방향만 만들고 위치는 매 프레임 제자리로 돌린다.
	//
	// 버튼·슬라이더는 씬에서 이 컴포넌트의 public 메서드로 직접 연결한다(영구 리스너).
	public class DevMotionViewer : MonoBehaviour
	{
		[Serializable]
		private struct WeaponEntry
		{
			public RuntimeAnimatorController controller;

			public Sprite model;

			// 평타 스킬 — 주기·클립 길이·콤보 수를 여기서 읽는다
			public EDT.Skill attackSkill;
		}

		[SerializeField] private Hero _hero;

		// 대검, 태도, 수리검, 소총
		[SerializeField] private WeaponEntry[] _weapons;

		// 연속공격 토글 버튼 — 켜지면 녹색
		[SerializeField] private Image _continuousButtonImage;

		[SerializeField] private Color _toggleOnColor = Color.green;

		[SerializeField] private TMP_Text _attackSpeedText;

		[SerializeField] private TMP_Text _moveSpeedText;

		[SerializeField] private TMP_Text _directionText;

		// 방향 표시선 — 조준 기준점(WeaponAim 위치)에 두고 0도(+x)로 뻗은 선을 각도만큼 돌린다
		[SerializeField] private Transform _directionArrow;

		// 평타 사거리 — 방향 표시선 아래에 판정 모양(ScanType·ScanRange·ScanParam) 그대로 그린다
		[SerializeField] private Color _rangeColor = new Color(1f, 1f, 1f, 0.2f);

		// 표시 토글 — 방향선(_directionLine)·사거리를 각각 켜고 끈다.
		// 사거리는 방향 표시선(_directionArrow)의 자식이라 선만 따로 끈다.
		[SerializeField] private GameObject _directionLine;

		[SerializeField] private Toggle _directionViewToggle;

		[SerializeField] private Toggle _rangeViewToggle;

		private bool _showRange = true;

		private const int RangeSegments = 48;

		private const float RangeRingThickness = 0.05f;

		private MeshFilter _rangeFilter;

		private MeshRenderer _rangeRenderer;

		private Material _rangeMaterial;

		private Mesh _rangeMesh;

		private UnitAnimator _animator;

		private UnitMover _mover;

		private HeroAvatar _avatar;

		private Vector3 _homePosition;

		// 0도 = 오른쪽, 반시계로 증가
		private Vector2 _direction = Vector2.right;

		private bool _tableLoaded;

		private int _weaponIndex = -1;

		private bool _isMoving;

		private bool _isContinuous;

		// 공격 주기 중에 누른 공격 — 주기가 끝나면 바로 낸다(연타하면 끊기지 않고 이어진다)
		private bool _attackQueued;

		private float _cooldownTimer;

		// 1 = 100%
		private float _attackSpeed = 1f;

		private float _moveSpeed = 1f;

		private Color _toggleOffColor;

		private void Awake()
		{
			_animator = _hero.GetComponent<UnitAnimator>();
			_mover = _hero.GetComponent<UnitMover>();
			_avatar = _hero.GetComponent<HeroAvatar>();
			_homePosition = _hero.transform.position;

			if (_continuousButtonImage != null)
			{
				_toggleOffColor = _continuousButtonImage.color;
			}

			createRange();

			if (_directionViewToggle != null)
			{
				_directionViewToggle.onValueChanged.AddListener(SetDirectionVisible);
			}

			if (_rangeViewToggle != null)
			{
				_rangeViewToggle.onValueChanged.AddListener(SetRangeVisible);
			}
		}

		private void OnDestroy()
		{
			if (_directionViewToggle != null)
			{
				_directionViewToggle.onValueChanged.RemoveListener(SetDirectionVisible);
			}

			if (_rangeViewToggle != null)
			{
				_rangeViewToggle.onValueChanged.RemoveListener(SetRangeVisible);
			}

			if (_rangeMaterial != null)
			{
				Destroy(_rangeMaterial);
			}

			if (_rangeMesh != null)
			{
				Destroy(_rangeMesh);
			}
		}

		private void Start()
		{
			SetDirectionAngle(0f);

			// 씬에 저장된 토글 상태로 시작한다
			if (_directionViewToggle != null)
			{
				SetDirectionVisible(_directionViewToggle.isOn);
			}

			if (_rangeViewToggle != null)
			{
				SetRangeVisible(_rangeViewToggle.isOn);
			}

			SelectWeapon(0);
			applySpeeds();
			TableBootLoader.LoadAllAsync(this.GetCancellationTokenOnDestroy()).ContinueWith(onTableLoaded).Forget(onTableLoadFailed);
		}

		private void onTableLoaded(bool ok)
		{
			_tableLoaded = ok;
			rebuildRange();
		}

		private void onTableLoadFailed(Exception e)
		{
			// 로드 중 플레이를 끄면(OnDestroy 토큰 취소) 오는 정상 종료 — 로그 대상이 아니다
			if (e is OperationCanceledException)
			{
				return;
			}

			Debug.LogException(e);
		}

		private void Update()
		{
			// 제자리 걷기 — 무버가 FixedUpdate 에서 옮긴 만큼 렌더 전에 되돌린다
			_hero.transform.position = _homePosition;

			if (_isMoving == true)
			{
				_mover.Move(_direction, 1f);
			}

			if (_cooldownTimer > 0f)
			{
				_cooldownTimer -= Time.deltaTime;
			}

			if (_cooldownTimer <= 0f && (_attackQueued == true || _isContinuous == true))
			{
				fireAttack();
			}
		}

		// ── 버튼 ─────────────────────────────────────────────────────

		// 0 대검 / 1 태도 / 2 수리검 / 3 소총
		public void SelectWeapon(int index)
		{
			if (index < 0 || index >= _weapons.Length || index == _weaponIndex)
			{
				return;
			}

			_weaponIndex = index;
			_attackQueued = false;
			_cooldownTimer = 0f;

			WeaponEntry weapon = _weapons[index];
			_animator.SetController(weapon.controller);
			_avatar.ApplyWeaponModel(weapon.model);

			// 컨트롤러 교체는 애니메이터 파라미터를 기본값으로 되돌린다 — 배속을 다시 넣는다
			applySpeeds();
			rebuildRange();
		}

		public void Stop()
		{
			_isMoving = false;
			_mover.Stop();
		}

		public void Move()
		{
			_isMoving = true;
		}

		public void Attack()
		{
			_attackQueued = true;
		}

		public void ToggleContinuous()
		{
			_isContinuous = !_isContinuous;
			if (_continuousButtonImage != null)
			{
				_continuousButtonImage.color = _isContinuous ? _toggleOnColor : _toggleOffColor;
			}
		}

		// ── 토글 ─────────────────────────────────────────────────────

		public void SetDirectionVisible(bool visible)
		{
			if (_directionLine != null)
			{
				_directionLine.SetActive(visible);
			}
		}

		public void SetRangeVisible(bool visible)
		{
			_showRange = visible;
			rebuildRange();
		}

		// ── 슬라이더 ─────────────────────────────────────────────────

		// 0~360도. 0도 = 오른쪽, 반시계로 증가
		public void SetDirectionAngle(float angle)
		{
			float rad = angle * Mathf.Deg2Rad;
			_direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

			// 이동 중이면 다음 Update 의 Move 가 방향을 바꾼다 — 정지 중에도 바로 돌아보게 facing 을 직접 넣는다
			_mover.SetFacing(_direction);

			if (_directionArrow != null)
			{
				_directionArrow.localRotation = Quaternion.Euler(0f, 0f, angle);
			}

			if (_directionText != null)
			{
				_directionText.text = Mathf.RoundToInt(angle) + "°";
			}
		}

		// 값은 % 단위

		public void SetAttackSpeedPercent(float percent)
		{
			_attackSpeed = percent * 0.01f;
			applySpeeds();
		}

		public void SetMoveSpeedPercent(float percent)
		{
			_moveSpeed = percent * 0.01f;
			applySpeeds();
		}

		// ── 내부 ─────────────────────────────────────────────────────

		private void applySpeeds()
		{
			_animator.SetAttackSpeed(_attackSpeed);
			_animator.SetMoveSpeedRatio(_moveSpeed);

			if (_attackSpeedText != null)
			{
				_attackSpeedText.text = Mathf.RoundToInt(_attackSpeed * 100f) + "%";
			}

			if (_moveSpeedText != null)
			{
				_moveSpeedText.text = Mathf.RoundToInt(_moveSpeed * 100f) + "%";
			}
		}

		// 방향 표시선의 자식이라 회전·기준점(조준 중심)을 그대로 따라간다. 캐릭터 아래(Shadow 레이어)에 그린다.
		private void createRange()
		{
			if (_directionArrow == null)
			{
				return;
			}

			GameObject go = new GameObject("AttackRange");
			go.transform.SetParent(_directionArrow, false);
			_rangeFilter = go.AddComponent<MeshFilter>();
			_rangeRenderer = go.AddComponent<MeshRenderer>();
			_rangeMaterial = new Material(Shader.Find("Sprites/Default"));
			_rangeMaterial.color = _rangeColor;
			_rangeRenderer.sharedMaterial = _rangeMaterial;
			_rangeRenderer.sortingLayerName = "Shadow";
			_rangeRenderer.shadowCastingMode = ShadowCastingMode.Off;
			_rangeRenderer.receiveShadows = false;
			_rangeRenderer.enabled = false;
		}

		// 무기가 바뀌거나 테이블이 들어왔을 때만 다시 만든다
		private void rebuildRange()
		{
			if (_rangeRenderer == null)
			{
				return;
			}

			_rangeRenderer.enabled = false;
			if (_showRange == false || _tableLoaded == false || _weaponIndex < 0)
			{
				return;
			}

			EDT.Table_Skill.Row row = EDT.Table_Skill.Get(_weapons[_weaponIndex].attackSkill);
			if (row == null)
			{
				return;
			}

			IndicatorMeshBuilder builder = IndicatorMeshBuilder.Get(row.ScanType);
			if (builder == null || row.ScanRange <= 0f)
			{
				return;
			}

			if (_rangeMesh != null)
			{
				Destroy(_rangeMesh);
			}

			_rangeMesh = builder.Build(row.ScanRange, row.ScanParam, RangeSegments, RangeRingThickness);
			_rangeFilter.sharedMesh = _rangeMesh;
			_rangeRenderer.enabled = true;
		}

		// SkillExecutor.playAnim 과 같은 순서 — 클립 배속, 콤보 수, 트리거
		private void fireAttack()
		{
			_attackQueued = false;
			if (_tableLoaded == false || _weaponIndex < 0)
			{
				return;
			}

			EDT.Table_Skill.Row row = EDT.Table_Skill.Get(_weapons[_weaponIndex].attackSkill);
			if (row == null)
			{
				Debug.LogWarning("[DevMotionViewer] 평타 스킬 행 없음: " + _weapons[_weaponIndex].attackSkill);
				return;
			}

			float cycle = row.Cooldown / _attackSpeed;
			float weaponSpeed = _attackSpeed;
			if (row.Cooldown > 0f && row.AnimLength > 0f)
			{
				weaponSpeed = row.AnimLength / cycle;
			}

			_cooldownTimer = cycle;
			_animator.SetWeaponAttackSpeed(weaponSpeed);
			_animator.SetComboSteps(row.ComboSteps);
			_animator.PlayMotion(row.AnimName);
		}
	}
}
