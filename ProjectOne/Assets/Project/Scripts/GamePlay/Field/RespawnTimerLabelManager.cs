using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Field
{
	// 리스폰 남은 시간 라벨의 생성·재사용 창구. 어떤 리스폰 콘텐츠든 위치와 남은 초만 넘기면 된다.
	//
	// 부트 씬에 프리팹 참조를 물려 배치하는 것이 전제다 (DamageTextManager 와 같다).
	// 동시에 뜨는 라벨이 적어(필드보스 등) PoolBase 대신 비활성 목록으로 재사용한다.
	public sealed class RespawnTimerLabelManager : MonoSingleton<RespawnTimerLabelManager>
	{
		[SerializeField] private RespawnTimerLabel _prefab;

		// 회수된 라벨 — 다음 Show 에서 꺼내 쓴다.
		private readonly List<RespawnTimerLabel> _free = new List<RespawnTimerLabel>();

		// title 은 시간 위 줄에 붙는다(리치 텍스트 가능). 시간만 필요하면 null.
		public RespawnTimerLabel Show(Vector3 position, float remainingSeconds, string title)
		{
			if (_prefab == null)
			{
				Debug.LogError("[RespawnTimerLabelManager] 라벨 프리팹이 지정되지 않았습니다 — 부트 씬 배치를 확인하세요.");
				return null;
			}

			RespawnTimerLabel label;
			int last = _free.Count - 1;
			if (last >= 0)
			{
				label = _free[last];
				_free.RemoveAt(last);
			}
			else
			{
				label = Instantiate(_prefab, transform);
			}

			label.Show(position, remainingSeconds, title);
			return label;
		}

		public void Hide(RespawnTimerLabel label)
		{
			if (label == null)
			{
				return;
			}

			label.gameObject.SetActive(false);
			_free.Add(label);
		}
	}
}
