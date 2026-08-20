using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.UI;

namespace ProjectOne.Quests
{
	// NPC 상호작용 실행기 — 판정 결과를 실제 화면으로 옮긴다.
	//
	// 판정은 NpcInteraction 이 이미 끝내 놓았다(우선순위 4단계 + 폴백). 여기는 그 결과를
	// "어떤 창을 열 것인가"로만 바꾼다. 두 책임을 한 클래스에 두면 UI 없이 판정을 검증할 수 없다.
	//
	// 화면은 UIManager.OpenAsync(UIScreenId) 하나로만 연다 — HUD 버튼이 여는 상점과
	// NPC 가 여는 상점이 같은 코드여야 "굳이 찾아가지 않아도 같은 창"이 성립한다.
	public static class NpcInteractor
	{
		public static void Interact(int npcId)
		{
			if (npcId <= 0)
			{
				return;
			}

			NpcInteractionResult result = NpcInteraction.Resolve(npcId);

			// 목표 갱신 → 완료 판정 순서를 지킨다 (설계 4.5).
			// 대화 대상이 완료 담당과 같으면 한 번의 대화로 달성과 완료가 연달아 일어난다.
			QuestTracker.Instance.NotifyTalk(npcId);

			openAsync(result).Forget();
		}

		private static async UniTaskVoid openAsync(NpcInteractionResult result)
		{
			UIScreenId screen = resolveScreen(result);
			if (screen == UIScreenId.None)
			{
				// 열 화면이 없으면 대사만 있는 NPC 다. 대화창이 만들어지면 Dialog 로 열린다.
				Debug.Log($"[NpcInteractor] Npc {result.npcId} — {result.trigger}, 열 화면 없음(대사 {result.lines.Length}줄)");
				return;
			}

			await UIManager.Instance.OpenAsync(screen, default(System.Threading.CancellationToken));
		}

		// 퀘스트가 걸려 있으면 대화가 우선이고, 그렇지 않을 때만 NpcType 기능을 연다.
		// 이 우선순위는 NpcInteraction 이 이미 결정해 trigger 로 알려준다.
		private static UIScreenId resolveScreen(NpcInteractionResult result)
		{
			if (result.trigger != DialogTriggerType.Default)
			{
				return UIScreenId.Dialog;
			}

			UIScreenId functionScreen = UIScreenCatalog.FromNpcType(result.function);
			if (functionScreen != UIScreenId.None)
			{
				return functionScreen;
			}

			// 기능도 없고 기본 대사만 있는 NPC — 대사가 있으면 대화창을 연다.
			return (result.lines.Length > 0) ? UIScreenId.Dialog : UIScreenId.None;
		}
	}
}
