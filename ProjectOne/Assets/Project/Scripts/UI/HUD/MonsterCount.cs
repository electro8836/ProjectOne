using System;
using UnityEngine;
using TMPro;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 남은 몬스터 수 표시. 웨이브 진행 중에만 노출하고 대기 중에는 숨긴다.
	// 카운트 변경 전용 이벤트가 없어 표시 중 ActiveCount를 폴링한다.
	public class MonsterCount : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private GameObject _visual;   // 표시/숨김할 시각 요소 컨테이너
		[SerializeField] private TMP_Text _countText;

		private Action<WaveStateChangedEvent> _onWaveStateChanged;
		private bool _isActive;
		private int _lastCount = -1;

		private void Awake()
		{
			_onWaveStateChanged = onWaveStateChanged;
			EventManager.Instance.Subscribe<WaveStateChangedEvent>(_onWaveStateChanged);

			setActive(false);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<WaveStateChangedEvent>(_onWaveStateChanged);
		}

		private void Update()
		{
			if (_isActive == false || MonsterSpawnManager.HasInstance == false)
			{
				return;
			}

			int count = MonsterSpawnManager.Instance.ActiveCount;
			if (count != _lastCount)
			{
				_lastCount = count;
				_countText.text = count.ToString();
			}
		}

		private void onWaveStateChanged(WaveStateChangedEvent evt)
		{
			// 웨이브 진행(IsWaiting=false) 중에만 표시, 대기 중에는 숨김
			setActive(evt.IsWaiting == false);
		}

		private void setActive(bool active)
		{
			_isActive = active;
			_lastCount = -1;
			if (_visual != null)
			{
				_visual.SetActive(active);
			}
		}
	}
}
