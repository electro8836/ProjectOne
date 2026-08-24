using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Unit
{
	// 파츠 캐릭터의 스프라이트 교체 창구. 장비·코스튬은 이 컴포넌트를 통해서만 외형을 바꾼다.
	//
	// 파츠를 오브젝트 이름으로 지목할 수 없어 AvatarPart enum 으로 잡고, 실제 SpriteRenderer 는
	// 인스펙터에서 끌어다 연결한다 — 계층에 Front 가 4개, Back 이 3개 있고 '12_Helmet2 ' 는
	// 이름 끝에 공백이 있어 이름 기반 조회가 성립하지 않는다.
	//
	// 히어로 전용이다. 몬스터 애니 클립은 m_Sprite 커브를 쓰므로 런타임 대입이 매 프레임 덮어써진다.
	public class HeroAvatar : MonoBehaviour
	{
		[System.Serializable]
		public struct PartBinding
		{
			public AvatarPart part;

			public SpriteRenderer renderer;
		}

		[SerializeField] private PartBinding[] _bindings = new PartBinding[0];

		// 렌더러와 프리팹 원본 스프라이트를 함께 들고 있는다.
		// 원본이 있어야 장비를 벗었을 때 되돌릴 수 있다 — 무기는 원본이 비어 있어 그대로 사라지고,
		// 상의 같은 기본 파츠는 코스튬을 벗으면 원래 옷으로 돌아온다.
		private struct PartState
		{
			public SpriteRenderer renderer;

			public Sprite original;
		}

		private readonly Dictionary<AvatarPart, PartState> _parts = new Dictionary<AvatarPart, PartState>();

		private void Awake()
		{
			for (int i = 0; i < _bindings.Length; i++)
			{
				AvatarPart part = _bindings[i].part;
				SpriteRenderer renderer = _bindings[i].renderer;

				if (part == AvatarPart.None)
				{
					Debug.LogError($"[HeroAvatar] 파츠가 None 인 항목 — index:{i} ({this.name})");
					continue;
				}

				if (renderer == null)
				{
					Debug.LogError($"[HeroAvatar] 렌더러가 비어 있는 항목 — part:{part} ({this.name})");
					continue;
				}

				if (_parts.ContainsKey(part) == true)
				{
					Debug.LogError($"[HeroAvatar] 파츠가 중복 등록됨 — part:{part} ({this.name})");
					continue;
				}

				PartState state = default(PartState);
				state.renderer = renderer;
				state.original = renderer.sprite;
				_parts.Add(part, state);
			}
		}

		// 파츠 하나를 교체한다. sprite 가 null 이면 그 파츠를 감춘다.
		public void SetPart(AvatarPart part, Sprite sprite)
		{
			PartState state;
			if (_parts.TryGetValue(part, out state) == false)
			{
				Debug.LogError($"[HeroAvatar] 등록되지 않은 파츠 — part:{part} ({this.name})");
				return;
			}

			state.renderer.sprite = sprite;
		}

		// 전 파츠를 프리팹 원본으로 되돌린다. 외형 적용은 항상 여기서 시작한다.
		public void ResetAll()
		{
			Dictionary<AvatarPart, PartState>.Enumerator e = _parts.GetEnumerator();
			while (e.MoveNext() == true)
			{
				PartState state = e.Current.Value;
				state.renderer.sprite = state.original;
			}
		}

		// 무기 세트 — 손에 든 것을 결정한다. 장비 무기든 코스튬 무기든 이 경로다.
		// null 이면 양손을 비운다(무기 미장착).
		public void ApplyWeaponSet(AvatarWeaponSet set)
		{
			SetPart(AvatarPart.WeaponL, (set != null) ? set.Left : null);
			SetPart(AvatarPart.WeaponR, (set != null) ? set.Right : null);
		}

		// 바디 세트 — 몸 전체를 완전히 지정한다. 세트가 비워 둔 파츠는 감춘다.
		// 관할 밖(무기 2파츠·그림자)은 건너뛴다 — 무기는 무기셋이, 그림자는 프리팹이 결정한다.
		public void ApplyBodySet(AvatarBodySet set)
		{
			if (set == null)
			{
				Debug.LogError($"[HeroAvatar] 바디 세트가 없습니다 — 기본 코스튬을 확인하세요 ({this.name})");
				return;
			}

			Dictionary<AvatarPart, PartState>.Enumerator e = _parts.GetEnumerator();
			while (e.MoveNext() == true)
			{
				AvatarPart part = e.Current.Key;
				if (AvatarBodySet.Covers(part) == false)
				{
					continue;
				}

				e.Current.Value.renderer.sprite = set.Get(part);
			}
		}
	}
}
