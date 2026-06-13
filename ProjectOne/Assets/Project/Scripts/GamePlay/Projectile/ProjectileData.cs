using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Projectile
{
	// 발사체 1발의 발사 파라미터 — 스킬 효과 시스템과 연동(적중 시 hitEffect 적용).
	// 속도/수명/이동 방식은 발사체 프리팹(Projectile 컴포넌트)이 소유한다 — 여기엔 발사 순간 정보만 담는다.
	public struct ProjectileData
	{
		public Vector3 direction;     // 발사 방향(정규화)
		public Vector3 startPos;      // 발사 시작 위치(월드)

		public UnitBase caster;       // 발사자 — faction 판정·효과 귀속
		public SkillInfo skillId;     // 발동 스킬 — 효과 귀속
		public SkillEffect hitEffect; // 적중 시 적용할 효과
		public UnitBase target;       // 유도 궤적용 타겟 (직선이면 미사용)
		public float hitRadius;       // 임팩트 AoE 반경 (0=단일 타겟, >0=원형 범위 적용)

		public string expireVFX;      // 소멸(수명/사거리 만료) 시 출력 VFX 주소 (없으면 빈 문자열)
		public string expireSFX;      // 소멸(수명/사거리 만료) 시 출력 SFX 주소 (없으면 빈 문자열)
	}
}
