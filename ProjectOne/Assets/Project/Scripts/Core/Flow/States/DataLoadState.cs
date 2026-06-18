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
		public async UniTask EnterAsync(CancellationToken ct)
		{
			(bool _, CharacterData ch) = await ServerDataSystem.Repository.LoadAsync<CharacterData>(ServerDataSystem.KeyCharacter, ct);
			(bool _, InventoryData inv) = await ServerDataSystem.Repository.LoadAsync<InventoryData>(ServerDataSystem.KeyInventory, ct);
			(bool _, SkillData sk) = await ServerDataSystem.Repository.LoadAsync<SkillData>(ServerDataSystem.KeySkill, ct);
			(bool _, CurrencyData cur) = await ServerDataSystem.Repository.LoadAsync<CurrencyData>(ServerDataSystem.KeyCurrency, ct);

			Account.Instance.SetCharacter(ch);
			Account.Instance.SetInventory(inv);
			Account.Instance.SetSkill(sk);
			Account.Instance.SetCurrency(cur);

			GameFlow.Instance.ChangeStateAsync(new LobbyState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
