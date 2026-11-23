using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상자 확률표 Presenter — 보상 그룹의 확률과 실제로 나올 수 있는 등급을 모아 View 에 넘긴다.
	//
	// 확률은 전체 기준 절대값이다(모든 등급을 합치면 1). 등급 구역은 "그 등급 것만 모아 보여주는" 묶음일 뿐,
	// 등급 안에서 다시 정규화하지 않는다.
	public sealed class BoxRewardPresenter : Presenter<BoxRewardPopup>
	{
		private readonly List<RewardChance> _all = new List<RewardChance>();
		private readonly List<ItemGradeType> _grades = new List<ItemGradeType>();

		protected override void OnInitialize()
		{
		}

		protected override void OnDispose()
		{
		}

		public async UniTask ShowAsync(int rewardGroupId, CancellationToken ct)
		{
			_all.Clear();
			_grades.Clear();

			RewardProbability.Build(rewardGroupId, _all);
			collectGrades();

			if (_grades.Count == 0)
			{
				Debug.LogWarning($"[Shop] 보상 그룹 {rewardGroupId} 에 보여줄 확률이 없다 — Reward 테이블을 확인한다.");
				view.Reveal();
				return;
			}

			await view.RenderAsync(_grades, _all, ct);

			view.Reveal();
		}

		// 실제로 나올 수 있는 등급만 낮은 순으로 모은다.
		private void collectGrades()
		{
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				ItemGradeType grade = (ItemGradeType)g;
				for (int i = 0; i < _all.Count; i++)
				{
					if (_all[i].grade != grade)
					{
						continue;
					}

					_grades.Add(grade);
					break;
				}
			}
		}
	}
}
