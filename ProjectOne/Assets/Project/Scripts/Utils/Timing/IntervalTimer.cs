namespace ProjectOne.Utils
{
	// 프레임 dt 를 누적해 일정 간격마다 발동 횟수를 돌려주는 경량 타이머 (값 타입)
	// - interval 은 호출부가 매 Tick 에 넘긴다(가변 주기/직렬화 필드와 충돌 없음)
	// - 한 프레임에 여러 구간을 넘기면 그만큼 발동 횟수를 반환(다발 처리)
	public struct IntervalTimer
	{
		private float _accum;

		// dt 누적 후, interval 도달 횟수만큼 차감하고 그 횟수 반환. interval <= 0 이면 0 반환
		public int Tick(float dt, float interval)
		{
			if (interval <= 0f)
			{
				return 0;
			}

			_accum += dt;
			int count = 0;
			while (_accum >= interval)
			{
				_accum -= interval;
				count++;
			}

			return count;
		}

		// 누적값 초기화 (재적용/강제 리셋용)
		public void Reset()
		{
			_accum = 0f;
		}
	}
}
