using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.ServerData;
using ProjectOne.UserData;

namespace ProjectOne.Flow
{
	// 유저 데이터 로드 상태 — Login 씬 위 오버레이(별도 씬 없음).
	// 현재는 로컬 Repository 에서 로드, 추후 서버 수신(async)으로 교체하되 Account 주입은 동일.
	public class DataLoadState : IGameState
	{
		public UniTask EnterAsync(CancellationToken ct)
		{
			CharacterData ch;
			ServerDataSystem.Repository.TryLoad(ServerDataSystem.KeyCharacter, out ch);
			InventoryData inv;
			ServerDataSystem.Repository.TryLoad(ServerDataSystem.KeyInventory, out inv);
			SkillData sk;
			ServerDataSystem.Repository.TryLoad(ServerDataSystem.KeySkill, out sk);
			CurrencyData cur;
			ServerDataSystem.Repository.TryLoad(ServerDataSystem.KeyCurrency, out cur);

			Account.Instance.SetCharacter(ch);
			Account.Instance.SetInventory(inv);
			Account.Instance.SetSkill(sk);
			Account.Instance.SetCurrency(cur);

			GameFlow.Instance.ChangeStateAsync(new LobbyState()).Forget();
			return UniTask.CompletedTask;
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
