using UnityEngine;

namespace ProjectOne.Unit
{
	// 무기 FX(도검 궤적·총구 섬광) 스프라이트 묶음. 장비 행(Equipment.WeaponFX)이 주소로 고른다.
	//
	// 공격 클립은 스프라이트를 직접 잡지 않고 WeaponFxFrame 파라미터로 이 배열의 인덱스만 키로 잡는다(-1 = 끔).
	// 클립 번호는 "몇 번째 타 FX 를 언제 띄울지"만 알린다 — 번호가 새 타 구간에 들어서면 HeroWeapon 이 그 타의 FX 를 무기에서 떼어 띄우고,
	// 띄운 FX 는 그 타의 프레임을 공속과 무관하게 _frameTime 간격으로 스스로 재생한다. 연타가 빨라지면 겹쳐 그려진다.
	// 생김새는 장비가 정한다 — 같은 무기 종류의 다른 장비가 다른 궤적을 낼 수 있다.
	// 콤보 단계별 프레임을 이어 붙인다(예: 1타 0~9, 2타 10~19). 같은 무기 종류의 세트는 타 구간이 같아야 한다(클립이 번호를 잡으므로).
	[CreateAssetMenu(fileName = "WeaponFx_", menuName = "ProjectOne/Avatar/WeaponFxSet")]
	public class WeaponFxSet : ScriptableObject
	{
		[SerializeField] private Sprite[] _frames = new Sprite[0];

		// 타별 시작 프레임(오름차순). 한 타는 다음 타 시작 직전(마지막 타는 배열 끝)까지다. 비어 있으면 전체가 한 타다.
		[SerializeField] private int[] _hitStarts = new int[] { 0 };

		// 띄운 FX 의 프레임 간격(초) — 실제 시간이다
		[SerializeField] private float _frameTime = 1f / 60f;

		public float FrameTime { get { return _frameTime; } }

		// 범위 밖(-1 포함)이면 null — FX 를 감춘다.
		public Sprite Get(int index)
		{
			if (index < 0 || index >= _frames.Length)
			{
				return null;
			}

			return _frames[index];
		}

		// 프레임 번호가 속한 타. 범위 밖이면 -1.
		public int GetHit(int frame)
		{
			if (frame < 0 || frame >= _frames.Length)
			{
				return -1;
			}

			int hit = 0;
			for (int i = 1; i < _hitStarts.Length; i++)
			{
				if (frame >= _hitStarts[i])
				{
					hit = i;
				}
			}

			return hit;
		}

		public int GetHitStart(int hit)
		{
			if (hit <= 0 || hit >= _hitStarts.Length)
			{
				return 0;
			}

			return _hitStarts[hit];
		}

		// 그 타의 프레임 수 — 다음 타 시작(마지막 타는 배열 끝)까지
		public int GetHitFrameCount(int hit)
		{
			int end = (hit + 1 < _hitStarts.Length) ? _hitStarts[hit + 1] : _frames.Length;
			return end - GetHitStart(hit);
		}
	}
}
