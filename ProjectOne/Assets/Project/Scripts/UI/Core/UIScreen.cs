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
	}
}
