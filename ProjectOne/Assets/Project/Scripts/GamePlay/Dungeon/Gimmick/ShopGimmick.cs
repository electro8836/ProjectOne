using ProjectOne.Unit;
using ProjectOne.UI;

namespace ProjectOne.Dungeon
{
	// 상점 기믹 — 히어로가 범위에 들어와 있는 동안 BattleHUD 의 상점 열기 버튼을 노출한다.
	// 버튼 클릭 시 실제 카드스킬 구매창은 BattleHUD 가 연다.
	public sealed class ShopGimmick : DungeonGimmick
	{
		protected override void OnHeroEnter(UnitBase hero)
		{
			setShopButton(true);
		}

		protected override void OnHeroExit(UnitBase hero)
		{
			setShopButton(false);
		}

		public override void OnDespawn()
		{
			setShopButton(false);
		}

		private static void setShopButton(bool show)
		{
			if (UIManager.HasInstance == false)
			{
				return;
			}

			BattleHUD hud = UIManager.Instance.GetScreen<BattleHUD>();
			if (hud != null)
			{
				hud.ShowShopButton(show);
			}
		}
	}
}
