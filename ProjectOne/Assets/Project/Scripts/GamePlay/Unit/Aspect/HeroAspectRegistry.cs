using System.Collections.Generic;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	// Hero 후처리 모듈(IHeroAspect) 등록소
	// - 등록된 Aspect 리스트(상태)를 보유하므로 static 부적합 → Singleton<T> 컨벤션
	// - ApplyAll 은 Stage 오름차순으로 일괄 적용 (Hero 스폰 시 호출)
	// - Reapply 는 특정 source 만 Remove → Apply (장비 교체 등 런타임 갱신)
	public sealed class HeroAspectRegistry : Singleton<HeroAspectRegistry>
	{
		readonly List<IHeroAspect> _aspects = new List<IHeroAspect>(8);
		bool _dirty;

		private HeroAspectRegistry()
		{
		}

		public void Register(IHeroAspect aspect)
		{
			if (aspect == null || _aspects.Contains(aspect) == true)
			{
				return;
			}

			_aspects.Add(aspect);
			_dirty = true;
		}

		public void Unregister(IHeroAspect aspect)
		{
			if (aspect == null)
			{
				return;
			}

			_aspects.Remove(aspect);
		}

		// Hero 에 등록된 모든 Aspect 를 Stage 순서대로 적용
		public void ApplyAll(Hero hero)
		{
			if (hero == null)
			{
				return;
			}

			EnsureSorted();
			for (int i = 0; i < _aspects.Count; i++)
			{
				_aspects[i].ApplyTo(hero);
			}
		}

		// 특정 source 의 Aspect 만 Remove → Apply (장비 교체 등 부분 갱신)
		public void Reapply(Hero hero, string sourceKey)
		{
			if (hero == null || string.IsNullOrEmpty(sourceKey) == true)
			{
				return;
			}

			for (int i = 0; i < _aspects.Count; i++)
			{
				IHeroAspect a = _aspects[i];
				if (a.SourceKey == sourceKey)
				{
					a.RemoveFrom(hero);
					a.ApplyTo(hero);
				}
			}
		}

		// 디버그/UI 용 — 현재 등록된 Aspect 수
		public int Count
		{
			get { return _aspects.Count; }
		}

		// 영구 성장 출처 목록 — 전투력처럼 버프·디버프를 배제해야 하는 쪽이 쓴다.
		// Aspect 가 붙인 modifier 만 육성 결과이고, 버프는 Aspect 가 아니므로 여기 안 들어온다.
		// 버퍼는 호출자가 소유한다(매 호출 할당을 피한다).
		public void CollectSourceKeys(List<string> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			for (int i = 0; i < _aspects.Count; i++)
			{
				string key = _aspects[i].SourceKey;
				if (string.IsNullOrEmpty(key) == true)
				{
					continue;
				}

				// 같은 SourceKey 를 쓰는 Aspect 가 둘 이상일 수 있다 — 중복 합산을 막는다.
				if (buffer.Contains(key) == true)
				{
					continue;
				}

				buffer.Add(key);
			}
		}

		void EnsureSorted()
		{
			if (_dirty == false)
			{
				return;
			}

			// Stage 오름차순 — 동률은 등록 순서 유지(List.Sort 는 unstable 하지만 실용상 무시 가능)
			_aspects.Sort(CompareByStage);
			_dirty = false;
		}

		static int CompareByStage(IHeroAspect a, IHeroAspect b)
		{
			return ((int)a.Stage).CompareTo((int)b.Stage);
		}
	}
}
