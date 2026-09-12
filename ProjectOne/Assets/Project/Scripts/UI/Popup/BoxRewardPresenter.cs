using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상자 확률표 Presenter — 보상 그룹의 확률을 등급별로 나눠 View 에 넘긴다.
	//
	// 확률은 전체 기준 절대값이다(모든 탭을 합치면 1). 탭은 "그 등급 것만 걸러 보여주는" 필터일 뿐,
	// 등급 안에서 다시 정규화하지 않는다.
	public sealed class BoxRewardPresenter : Presenter<BoxRewardPopup>
	{
		private readonly List<RewardChance> _all = new List<RewardChance>();
		private readonly List<ItemGradeType> _grades = new List<ItemGradeType>();
		private readonly List<RewardChance> _filtered = new List<RewardChance>();

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnTabSelected -= onTabSelected;
		}

		public async UniTask ShowAsync(int rewardGroupId, CancellationToken ct)
		{
			_all.Clear();
			_grades.Clear();

			RewardProbability.Build(rewardGroupId, _all);
			collectGrades();

			view.SetGrades(_grades);

			if (_grades.Count == 0)
			{
				Debug.LogWarning($"[Shop] 보상 그룹 {rewardGroupId} 에 보여줄 확률이 없다 — Reward 테이블을 확인한다.");
				view.Reveal();
				return;
			}

			view.SelectTab(0);
			await renderAsync(0, ct);

			view.Reveal();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			render(index);
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 아이콘 로드가 await 라 탭을 연타하면 렌더가 겹친다 — 직전 렌더는 취소한다.
		private void render(int gradeIndex)
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			renderAsync(gradeIndex, _renderCts.Token).Forget();
		}

		private UniTask renderAsync(int gradeIndex, CancellationToken ct)
		{
			if (gradeIndex < 0 || gradeIndex >= _grades.Count)
			{
				return UniTask.CompletedTask;
			}

			ItemGradeType grade = _grades[gradeIndex];

			_filtered.Clear();
			for (int i = 0; i < _all.Count; i++)
			{
				if (_all[i].grade == grade)
				{
					_filtered.Add(_all[i]);
				}
			}

			return view.RenderAsync(_filtered, ct);
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
