using UnityEngine;

namespace ProjectOne.Dungeon
{
	// 던전 카드스킬 상점 리롤 가격 정책. 인스펙터로 CardSkillBuyUI 에 주입한다.
	// 가격 = BasePrice × PriceMultiplier^(리롤 횟수). 최초 리롤(횟수 0) = BasePrice.
	[CreateAssetMenu(menuName = "Dungeon/Reroll Config", fileName = "RerollConfig")]
	public class RerollConfig : ScriptableObject
	{
		[SerializeField] private int _basePrice = 100;			// 최초 리롤 비용
		[SerializeField] private float _priceMultiplier = 1.5f;	// 리롤마다 곱해지는 배율

		// 리롤 횟수(0부터)에 해당하는 가격
		public int GetPrice(int rerollCount)
		{
			return Mathf.RoundToInt(_basePrice * Mathf.Pow(_priceMultiplier, rerollCount));
		}
	}
}
