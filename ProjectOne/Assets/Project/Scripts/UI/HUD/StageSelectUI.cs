using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;

namespace ProjectOne.UI
{
	// 다음 스테이지 선택 화면. DungeonDirector 가 OpenOverlayAsync 로 열고 WaitSelectionAsync 로 선택을 기다린다.
	// 두 스테이지(또는 1개) 이름을 버튼에 표시하고, 선택된 StageID 를 반환한다.
	public class StageSelectUI : UIScreen
	{
		[Header("스테이지 A")]
		[SerializeField] private UIButton _stageAButton;
		[SerializeField] private TMP_Text _stageANameText;

		[Header("스테이지 B")]
		[SerializeField] private UIButton _stageBButton;
		[SerializeField] private TMP_Text _stageBNameText;

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;

		private UniTaskCompletionSource<int> _selectionSource;
		private int _stageIdA;
		private int _stageIdB;

		private void Awake()
		{
			_stageAButton.OnClickEvent += onStageAClicked;
			_stageBButton.OnClickEvent += onStageBClicked;
			_exitButton.OnClickEvent += onExitClicked;
		}

		private void OnDestroy()
		{
			_stageAButton.OnClickEvent -= onStageAClicked;
			_stageBButton.OnClickEvent -= onStageBClicked;
			_exitButton.OnClickEvent -= onExitClicked;
			_selectionSource?.TrySetCanceled();
		}

		// 두 스테이지 이름을 표시하고 유저 선택(StageID)을 기다린다. ct 취소 시 취소 전파.
		public async UniTask<int> WaitSelectionAsync(int stageIdA, int stageIdB, CancellationToken ct)
		{
			_stageIdA = stageIdA;
			_stageIdB = stageIdB;
			_stageANameText.text = stageName(stageIdA);
			_stageBNameText.text = stageName(stageIdB);

			_selectionSource = new UniTaskCompletionSource<int>();
			using (ct.Register(onCanceled))
			{
				return await _selectionSource.Task;
			}
		}

		private void onCanceled()
		{
			_selectionSource?.TrySetCanceled();
		}

		private void onStageAClicked()
		{
			_selectionSource?.TrySetResult(_stageIdA);
		}

		private void onStageBClicked()
		{
			_selectionSource?.TrySetResult(_stageIdB);
		}

		// 닫기 — 선택하지 않고 UI 를 닫는다(0 반환). 호출자가 OpenSelectButton 을 다시 노출한다.
		private void onExitClicked()
		{
			_selectionSource?.TrySetResult(0);
		}

		private static string stageName(int stageId)
		{
			Table_Stage.Row row = Table_Stage.Get(stageId);
			return row != null ? row.Name : string.Empty;
		}
	}
}
