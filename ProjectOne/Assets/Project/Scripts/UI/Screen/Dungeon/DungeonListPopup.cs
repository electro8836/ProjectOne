using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using EDT;

namespace ProjectOne.UI
{
	// 던전 고르기 팝업(UIPrefab_DungeonListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Dungeon/DungeonListPopup.uxml.
	// HUD 메뉴의 던전 버튼이 연다. 던전 4종 줄 중 하나를 누르면 그 던전을 돌려주고 닫힌다 —
	// 이어서 그 던전의 입장 팝업을 여는 것은 호출자(MainHudPresenter)의 일이다.
	public class DungeonListPopup : ToolkitScreen
	{
		private static readonly EDT.Dungeon[] Dungeons =
		{
			EDT.Dungeon.Gold,
			EDT.Dungeon.Rift,
			EDT.Dungeon.Labyrinth,
			EDT.Dungeon.Ruins
		};

		private UniTaskCompletionSource _tcs;
		private EDT.Dungeon _result;

		protected override void OnBind(VisualElement root)
		{
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
			root.Q<Button>("close").clicked += close;

			for (int i = 0; i < Dungeons.Length; i++)
			{
				Button row = root.Q<Button>("dungeon-" + i);
				row.userData = i;
				row.RegisterCallback<ClickEvent>(onRowClicked);

				Table_Dungeon.Row data = Table_Dungeon.Get(Dungeons[i]);
				if (data != null)
				{
					row.Q<Label>(className: "dungeon-list-row__name").text = data.Name;
				}
			}
		}

		// 고른 던전. 그냥 닫으면 None.
		public async UniTask<EDT.Dungeon> ShowAsync(CancellationToken ct)
		{
			_result = EDT.Dungeon.None;
			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			return _result;
		}

		private void onRowClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			_result = Dungeons[index];
			close();
		}

		private void onDimClicked(ClickEvent evt)
		{
			close();
		}

		private void close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}
	}
}
