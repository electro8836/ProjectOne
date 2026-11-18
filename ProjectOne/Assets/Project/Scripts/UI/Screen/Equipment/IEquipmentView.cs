using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;

namespace ProjectOne.UI
{
	// 장비 화면 View 계약 — EquipmentPresenter 가 보는 입력 이벤트와 표시 API.
	// UGUI 판(EquipmentUI)과 UI Toolkit 판(EquipmentToolkitUI)이 같은 Presenter 를 쓰도록 둘 다 이것을 구현한다.
	public interface IEquipmentView : IView
	{
		event Action<int> OnTabSelected;
		event Action<long, int> OnSlotClicked;
		event Action OnStatClicked;
		event Action OnCurrencyClicked;
		event Action OnPetClicked;
		event Action OnCostumeClicked;
		event Action OnSortClicked;
		event Action OnStashClicked;
		event Action OnStashCloseClicked;
		event Action OnDecompositionClicked;

		CancellationToken GetDestroyToken();
		void SelectTab(int index);
		UniTask RenderGridAsync(IReadOnlyList<ItemSlotData> data, CancellationToken ct);
		UniTask RenderStashGridAsync(IReadOnlyList<ItemSlotData> data, CancellationToken ct);
		UniTask RenderEquippedAsync(IReadOnlyList<EquippedSlotData> data, CancellationToken ct);
		void RenderLevel(string weaponName, int masteryLevel);
		void RenderBattlePower(int power);
		void SetNickname(string nickname);
		void RenderInventoryCapacity(string text);
		void RenderStashCapacity(string text);
		void SetStashVisible(bool visible);
		void SetConsumableTabVisible(bool visible);
		void SetSortLabel(string label);
		void SetSortVisible(bool visible);
		void PlayEquippedSlotPop(EquipSlotTypes type);
	}
}
