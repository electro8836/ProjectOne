using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Reward;

namespace ProjectOne.Mail
{
	// 메일 데이터 출처. 구현체는 뒤끝 우편(BackndMailProvider).
	public interface IMailProvider
	{
		// 삭제하지 않은 메일 — 최신순.
		UniTask<IReadOnlyList<MailData>> GetMailsAsync(CancellationToken ct);

		// 첨부를 수령하고 받은 목록을 돌려준다. 이미 받았거나 첨부가 없으면 빈 목록.
		UniTask<List<GrantedReward>> ClaimAsync(string mailId, CancellationToken ct);

		UniTask DeleteAsync(string mailId, CancellationToken ct);

		// 남은 메일의 미수령 첨부를 모두 받고 전부 삭제한다.
		UniTask<List<GrantedReward>> ClaimAndDeleteAllAsync(CancellationToken ct);
	}
}
