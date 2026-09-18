using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.UI
{
	// 모든 UI 패널/화면의 기반 클래스.
	// 열기/닫기를 UniTask로 표현해 GameFlow와 동일한 비동기 흐름을 따른다.
	public abstract class UIScreen : MonoBehaviour
	{
		public virtual UniTask OnOpenAsync(CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnCloseAsync() => UniTask.CompletedTask;

		// 이 창이 떠 있는 동안 네비게이션 바를 가릴지.
		// 창 스택 최상단의 값만 반영된다 — 팝업은 스택을 타지 않으므로 이 값을 봐도 의미가 없다.
		public virtual bool HidesNavigationBar
		{
			get { return false; }
		}
	}
}
