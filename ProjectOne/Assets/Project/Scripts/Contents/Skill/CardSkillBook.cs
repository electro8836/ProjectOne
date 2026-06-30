using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 보유 카드스킬(로비) 도메인 모델 — 획득 누적/강화레벨 보관(인메모리).
	// 공유 DTO(CardSkillBookDto)를 받아 런타임 OwnedCardSkill 로 변환 보유하고, 저장/전송 시 ToDto() 로 역변환한다.
	// 영속은 서버(Backnd 함수)가 담당 예정, 현재는 로컬 우선 + 변경 시 알림만 발행.
	public sealed class CardSkillBook
	{
		// 카드 첫 획득 시의 기본 강화레벨(던전 최소 구매 레벨 1)
		private const int DefaultEnchantLevel = 1;

		// 순서 보존용 목록 + 빠른 조회용 인덱스(동일 OwnedCardSkill 참조 공유)
		private readonly List<OwnedCardSkill> _cardSkills = new List<OwnedCardSkill>();
		private readonly Dictionary<int, OwnedCardSkill> _index = new Dictionary<int, OwnedCardSkill>();

		// 강화 후 서버 동기화용 변경 플래그(서버 연동 시 사용 — 현재는 자리만)
		private bool _dirty;

		public CardSkillBook(CardSkillBookDto dto)
		{
			buildFromDto(dto);
		}

		public bool Dirty => _dirty;

		// ── 공개 API ──────────────────────────────────────────────────

		public bool Has(int cardSkillId)
		{
			OwnedCardSkill cs;
			if (_index.TryGetValue(cardSkillId, out cs) == true)
			{
				return cs.ownedCount >= 1;
			}

			return false;
		}

		public int GetCount(int cardSkillId)
		{
			OwnedCardSkill cs;
			if (_index.TryGetValue(cardSkillId, out cs) == true)
			{
				return cs.ownedCount;
			}

			return 0;
		}

		public int GetEnchantLevel(int cardSkillId)
		{
			OwnedCardSkill cs;
			if (_index.TryGetValue(cardSkillId, out cs) == true)
			{
				return cs.enchantLevel;
			}

			return 0;
		}

		// 전체 보유 카드스킬 조회 (읽기 전용) — UI 목록/디버그용
		public IReadOnlyList<OwnedCardSkill> GetAll()
		{
			return _cardSkills;
		}

		// 카드스킬 획득 — 없으면 생성(강화레벨 1), 있으면 보유개수 증가
		public void Add(int cardSkillId, int amount = 1)
		{
			if (cardSkillId <= 0 || amount <= 0)
			{
				return;
			}

			OwnedCardSkill cs;
			if (_index.TryGetValue(cardSkillId, out cs) == false)
			{
				cs = makeCardSkill(cardSkillId);
				_cardSkills.Add(cs);
				_index.Add(cardSkillId, cs);
			}

			cs.ownedCount += amount;
			publishChange(cs);
		}

		// 강화(인챈트) — 다음 레벨에 필요한 중복카드 개수를 보유개수에서 소모하고 강화레벨 +1.
		// 최대레벨 도달/필요행 없음/보유부족이면 false.
		public bool TryEnchant(int cardSkillId)
		{
			OwnedCardSkill cs;
			if (_index.TryGetValue(cardSkillId, out cs) == false)
			{
				return false;
			}

			if (cs.enchantLevel >= CardSkillResolver.MaxLevel)
			{
				return false;
			}

			Table_CardSkill.Row card = Table_CardSkill.Get(cardSkillId);
			if (card == null)
			{
				return false;
			}

			int targetLevel = cs.enchantLevel + 1;
			int required = CardSkillResolver.RequiredCountForEnchant(card.CardGrade, targetLevel);
			if (required <= 0 || cs.ownedCount < required)
			{
				return false;
			}

			cs.ownedCount -= required;
			cs.enchantLevel = targetLevel;
			_dirty = true;
			publishChange(cs);
			return true;
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public CardSkillBookDto ToDto()
		{
			CardSkillBookDto dto = new CardSkillBookDto();
			for (int i = 0; i < _cardSkills.Count; i++)
			{
				OwnedCardSkill src = _cardSkills[i];
				OwnedCardSkillDto entry = new OwnedCardSkillDto();
				entry.cardSkillId = src.cardSkillId;
				entry.ownedCount = src.ownedCount;
				entry.enchantLevel = src.enchantLevel;
				dto.cardSkills.Add(entry);
			}

			return dto;
		}

		// 서버 동기화 완료 표시(서버 연동 시 사용)
		public void MarkSynced()
		{
			_dirty = false;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildFromDto(CardSkillBookDto dto)
		{
			_cardSkills.Clear();
			_index.Clear();
			_dirty = false;
			if (dto == null)
			{
				return;
			}

			for (int i = 0; i < dto.cardSkills.Count; i++)
			{
				OwnedCardSkillDto src = dto.cardSkills[i];
				if (src == null || src.cardSkillId <= 0)
				{
					continue;
				}

				OwnedCardSkill cs = new OwnedCardSkill();
				cs.cardSkillId = src.cardSkillId;
				cs.ownedCount = src.ownedCount;
				cs.enchantLevel = src.enchantLevel < DefaultEnchantLevel ? DefaultEnchantLevel : src.enchantLevel;
				_cardSkills.Add(cs);
				_index[cs.cardSkillId] = cs;
			}
		}

		private static OwnedCardSkill makeCardSkill(int cardSkillId)
		{
			OwnedCardSkill cs = new OwnedCardSkill();
			cs.cardSkillId = cardSkillId;
			cs.ownedCount = 0;
			cs.enchantLevel = DefaultEnchantLevel;
			return cs;
		}

		private void publishChange(OwnedCardSkill cs)
		{
			EventManager.Instance.Publish(new CardSkillChangedEvent(cs.cardSkillId, cs.ownedCount, cs.enchantLevel));
		}
	}
}
