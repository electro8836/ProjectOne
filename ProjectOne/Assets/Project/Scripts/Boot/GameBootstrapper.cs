using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.Network;
using ProjectOne.Unit;

namespace ProjectOne.Boot
{
	// 게임 진입점.
	// - 부트 씬에 단 한 번 배치되어 Start에서 게임 흐름(GameFlow)을 켠다.
	// - 실제 초기화/전이 책임은 GameFlow와 각 상태(BootState 등)로 넘어갔다.
	public class GameBootstrapper : MonoBehaviour
	{
		// 히어로 프리팹 Addressable 주소.
		// 캐릭터가 하나뿐이라 테이블에 프리팹 컬럼을 두지 않는다. 테이블 조회 대신 인스펙터로 주입한다.
		[Header("히어로")]
		[SerializeField] private string _heroPrefabAddress;

		private void Start()
		{
			// 앱 일시정지/종료 시 미저장 장착 변경을 flush 할 전역 컴포넌트 생성(1회).
			LoadoutSyncFlusher.Ensure();

			UnitFactory.Instance.SetHeroPrefabAddress(_heroPrefabAddress);

			// 흐름의 첫 상태로 진입 — 이후 전이는 각 상태가 연쇄한다.
			// 미관측 예외는 GameFlow.ChangeStateAsync 내부 try/catch가 흡수.
			GameFlow.Instance.ChangeStateAsync(new BootState()).Forget();
		}
	}
}
