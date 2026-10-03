using System.Collections.Generic;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Upgrade;
using ProjectOne.UserData;

namespace ProjectOne.Pets
{
	// 펫 강화 묶음 전송 — 묶음·되돌림·환불 규칙은 GrowthBatcher 참고. 키는 펫 ID다.
	public sealed class PetEnhanceBatcher : GrowthBatcher<PetEnhanceBatcher>
	{
		private readonly List<UpgradeCost> _costs = new List<UpgradeCost>(1);

		private PetEnhanceBatcher()
		{
		}

		protected override string LogTag
		{
			get { return "PetEnhanceBatcher"; }
		}

		// 강화 1회를 즉시 적용하고 묶음에 쌓는다. 적용했으면 true.
		public bool TryEnhance(int id)
		{
			PetBook book = Account.Instance.Pet;
			if (NetworkManager.Instance.IsLoggedIn == false || book.GetEnhanceBlock(id) != PetEnhanceBlock.None)
			{
				return false;
			}

			PetEntry entry = book.Find(id);

			EDT.Currency currency;
			int amount;
			PetCatalog.TryGetEnhanceCost(entry.Level, out currency, out amount);

			_costs.Clear();
			UpgradeCost cost;
			cost.currency = currency;
			cost.amount = amount;
			cost.owned = 0;
			_costs.Add(cost);

			Apply((long)id, entry.Level, _costs);
			return true;
		}

		protected override void SetLevel(long key, int level)
		{
			Account.Instance.Pet.SetLevel((int)key, level);
		}

		protected override void Send(long key, int fromLevel, int count)
		{
			PetEnhanceRequest request = new PetEnhanceRequest();
			request.petId = (int)key;
			request.fromLevel = fromLevel;
			request.count = count;
			NetworkManager.Instance.RequestPetEnhance(request, onResponse);
		}

		private void onResponse(bool success, PetGrowthResponse data, string error)
		{
			int serverLevel = (data != null && data.pet != null) ? data.pet.level : -1;
			OnSendResult(success, serverLevel, (data != null) ? data.spent : null, error);
		}
	}
}
