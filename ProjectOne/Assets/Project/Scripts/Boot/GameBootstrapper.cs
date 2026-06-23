using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.Network;

namespace ProjectOne.Boot
{
	// 게임 진입점.
	// - 부트 씬에 단 한 번 배치되어 Start에서 게임 흐름(GameFlow)을 켠다.
	// - 실제 초기화/전이 책임은 GameFlow와 각 상태(BootState 등)로 넘어갔다.
	public class GameBootstrapper : MonoBehaviour
	{
		private void Start()
		{
			// 앱 일시정지/종료 시 미저장 장착 변경을 flush 할 전역 컴포넌트 생성(1회).
			LoadoutSyncFlusher.Ensure();

			// 흐름의 첫 상태로 진입 — 이후 전이는 각 상태가 연쇄한다.
			// 미관측 예외는 GameFlow.ChangeStateAsync 내부 try/catch가 흡수.
			GameFlow.Instance.ChangeStateAsync(new BootState()).Forget();
		}
	}
}
