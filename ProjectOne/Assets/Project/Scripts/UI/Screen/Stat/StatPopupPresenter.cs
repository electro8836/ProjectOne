using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;

namespace ProjectOne.UI
{
	// 스탯 슬롯 1칸 렌더 데이터 — Presenter 가 계산해 View 에 넘긴다(View 는 그리기만).
	public struct StatSlotData
	{
		public string iconAddress;
		public string text;
	}

	// 스탯 팝업 Presenter — 어떤 스탯을 어떤 문구로 보여줄지 정한다.
	//
	// 표시 대상은 Stat 테이블의 Display 컬럼이 소유한다(코드에 목록을 두면 테이블과 어긋난다).
	// 값은 히어로의 최종 스탯이라 장비·마스터리가 모두 반영된 결과다.
	public sealed class StatPopupPresenter : Presenter<StatPopup>
	{
		// 스탯 창은 증감을 보여주는 화면이 아니라 현재값을 보여주는 화면이라
		// StatOptionText 의 증가/감소 색과 별개로 한 가지 색만 쓴다.
		private const string ValueColor = "#5CFF5C";

		// 카테고리별 렌더 버퍼 — 표시 대상이 고정이라 매번 같은 리스트를 다시 채운다.
		private readonly List<Table_Stat.Row> _rows = new List<Table_Stat.Row>();
		private readonly List<StatSlotData> _offensive = new List<StatSlotData>();
		private readonly List<StatSlotData> _defensive = new List<StatSlotData>();
		private readonly List<StatSlotData> _utility = new List<StatSlotData>();

		public async UniTask ShowAsync(CancellationToken ct)
		{
			StatContainer stats = findHeroStats();
			if (stats == null)
			{
				// 히어로가 없으면 보여줄 값 자체가 없다 — 빈 팝업 대신 로그를 남기고 그대로 연다.
				Debug.LogError("[StatPopupPresenter] 히어로를 찾지 못했습니다 — 스탯을 표시할 수 없습니다.");
			}

			build(stats);

			await view.RenderAsync(_offensive, _defensive, _utility, ct);

			view.Reveal();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 스탯 팝업은 장비창 위에서만 열린다. 장비창은 마을뿐 아니라 필드에서도 열리므로
		// (NavigationBar 의 표시 컨텍스트), 씬을 가로질러 사는 UnitManager 를 본다
		// — 부모 화면인 EquipmentPresenter.findAliveHero 와 같은 방식이다.
		private StatContainer findHeroStats()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.IsDead == false)
				{
					return hero.Stats;
				}
			}

			return null;
		}

		private void build(StatContainer stats)
		{
			_offensive.Clear();
			_defensive.Clear();
			_utility.Clear();

			_rows.Clear();

			Dictionary<Stat, Table_Stat.Row> all = Table_Stat.All();
			Dictionary<Stat, Table_Stat.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Stat.Row row = e.Current.Value;
				if (row.ID == Stat.None || row.Display == false)
				{
					continue;
				}

				_rows.Add(row);
			}

			_rows.Sort(compareStatId);	// Dictionary 는 순서를 보장하지 않는다 — 테이블 행 순서로 되돌린다

			for (int i = 0; i < _rows.Count; i++)
			{
				Table_Stat.Row row = _rows[i];

				StatSlotData data;
				data.iconAddress = row.Icon;
				data.text = buildText(row, (stats != null) ? stats.GetStat(row.ID) : 0f);

				switch (row.Category)
				{
					case StatCategory.Offensive:
						_offensive.Add(data);
						break;

					case StatCategory.Defensive:
						_defensive.Add(data);
						break;

					case StatCategory.Utility:
						_utility.Add(data);
						break;
				}
			}
		}

		// "공격력 : <color=#5CFF5C>300</color>" — 이름은 Table_Stat 의 Name 컬럼이 소유한다.
		// Percent 스탯은 0.35 처럼 비율로 저장되어 있다 — 표시할 때만 100 을 곱한다 (설계 3.5).
		private string buildText(Table_Stat.Row row, float value)
		{
			float shown = (row.ValueType == StatValueTypes.Percent) ? value * 100f : value;
			string unit = (row.ValueType == StatValueTypes.Percent) ? "%" : string.Empty;

			return $"{row.Name} : <color={ValueColor}>{shown.ToString("0.##")}{unit}</color>";
		}

		private int compareStatId(Table_Stat.Row a, Table_Stat.Row b)
		{
			return ((int)a.ID).CompareTo((int)b.ID);
		}
	}
}
