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
