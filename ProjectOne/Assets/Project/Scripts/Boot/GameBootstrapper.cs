using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.Map;

namespace ProjectOne.Boot
{
	// 게임 진입점.
	// - 부트 씬에 단 한 번 배치되어 Start에서 게임 흐름(GameFlow)을 켠다.
	// - 실제 초기화/전이 책임은 GameFlow와 각 상태(BootState 등)로 넘어갔다.
	public class GameBootstrapper : MonoBehaviour
	{
		private void Start()
		{
			// 흐름의 첫 상태로 진입 — 이후 전이는 각 상태가 연쇄한다.
			// 미관측 예외는 GameFlow.ChangeStateAsync 내부 try/catch가 흡수.
			GameFlow.Instance.ChangeStateAsync(new BootState()).Forget();
		}
	}
}
