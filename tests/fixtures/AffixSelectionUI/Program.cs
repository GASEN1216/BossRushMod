using System;
using BossRush;
using ItemStatsSystem;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string reason)
    {
        assertions++;
        if (!value) throw new Exception(reason);
    }
    private static Item Weapon(bool decomposable = false)
    { return new Item { DisplayName = "Weapon", eligible = true, decomposable = decomposable }; }
    public static void Main()
    {
        foreach (bool english in new[] { false, true })
        foreach (bool vanillaLast in new[] { false, true })
        foreach (bool decomposable in new[] { false, true })
        {
            ReforgeUIManager.ResetHarness(english);
            ReforgeUIManager.Select(Weapon(decomposable), vanillaLast);
            Check(ReforgeUIManager.Pending == 1, "Selection must schedule one refresh after official Setup");
            ReforgeUIManager.Frame();
            Check(ReforgeUIManager.ButtonVisible && ReforgeUIManager.Clickable, "Eligible weapon must have an enabled forge button regardless of decomposition recipe or callback order");
            Check(!ReforgeUIManager.CannotVisible && !ReforgeUIManager.EmptyVisible, "Vanilla cannot-decompose and empty indicators must be hidden");
            // 2026-09-24（A-07）：付费按钮写价钱，替身的金币价是 100
            Check(ReforgeUIManager.ButtonLabel == (english ? "Reroll Affixes · 100" : "随机词缀 · 100"), "Forge label must match the current language and show the price");
            Check(ReforgeUIManager.Pending == 0, "Selection refresh must not keep polling");
            ReforgeUIManager.RefreshSharedButton();
            Check(ReforgeUIManager.Clickable && ReforgeSystem.QueryCount == 0, "Shared slider callback must use affix costs, not ordinary reforge costs");
        }
        foreach (int reason in new[] { 0, 1, 2 })
        {
            ReforgeUIManager.ResetHarness(false);
            var item = Weapon(); item.locked = reason == 2;
            ReforgeUIManager.SetBudget(reason == 0 ? 0 : 1000, reason == 1 ? 0 : 10);
            ReforgeUIManager.Select(item, true); ReforgeUIManager.Frame();
            Check(ReforgeUIManager.ButtonVisible && !ReforgeUIManager.Clickable && !ReforgeUIManager.CannotVisible,
                "Insufficient money/stones or locked slots must disable the forge button, not show cannot-decompose");
            ReforgeUIManager.SetBudget(10000, reason == 1 ? 0 : 10);
            ReforgeUIManager.RefreshSharedButton();
            Check(ReforgeUIManager.Clickable == (reason == 0), "Shared callback must not bypass stone or locked-slot requirements");
        }
        ReforgeUIManager.ResetHarness(true);
        ReforgeUIManager.Select(new Item { DisplayName = "Noncombat", decomposable = true }, true);
        ReforgeUIManager.Frame();
        Check(!ReforgeUIManager.ButtonVisible && ReforgeUIManager.CannotVisible, "Ineligible item must use affix eligibility even when vanilla can decompose it");
        Check(ReforgeUIManager.CannotLabel == "This equipment cannot carry affixes", "Ineligible item must explain affix eligibility");
        ReforgeUIManager.Select(Weapon(), true);
        ReforgeUIManager.Select(null, true);
        Check(ReforgeUIManager.Pending == 1, "Rapid selections must cancel the old refresh");
        ReforgeUIManager.Frame();
        Check(!ReforgeUIManager.ButtonVisible && !ReforgeUIManager.CannotVisible && ReforgeUIManager.EmptyVisible, "Deselect must not restore the previous weapon");
        ReforgeUIManager.Select(Weapon(), true);
        ReforgeUIManager.Select(new Item(), true);
        ReforgeUIManager.Frame();
        Check(!ReforgeUIManager.ButtonVisible && ReforgeUIManager.CannotVisible, "Deferred refresh must use the latest item");

        // 延时刷新必须把词缀行一起重铺：只修按钮会让玩家看到「按钮对了、词缀名整列空着」
        ReforgeUIManager.ResetHarness(true);
        ReforgeUIManager.Select(Weapon(), true);
        int refreshesBeforeFrame = ReforgeUIManager.PanelRefreshes;
        ReforgeUIManager.Frame();
        Check(ReforgeUIManager.PanelRefreshes > refreshesBeforeFrame,
            "Deferred refresh must repopulate the affix rows, not only the button");
        Check(ReforgeUIManager.PanelBuilds > 0,
            "Deferred refresh must ensure the affix panel exists before repopulating it");
        for (int boundary = 0; boundary < 4; boundary++)
        {
            ReforgeUIManager.ResetHarness(false); ReforgeUIManager.Select(Weapon(), true);
            if (boundary == 0) ReforgeUIManager.CloseHarness();
            if (boundary == 1) ReforgeUIManager.SwitchToReforge();
            if (boundary == 2) ReforgeUIManager.DestroyView();
            if (boundary == 3)
            {
                ReforgeUIManager.CleanupAffixForgeUI();
                Check(ReforgeUIManager.Pending == 0, "Cleanup must cancel work immediately");
            }
            ReforgeUIManager.Frame();
            Check(!ReforgeUIManager.ButtonVisible, "Closed/destroyed/other-mode UI must not receive a stale refresh");
            Check(ReforgeUIManager.Pending == 0, "Lifecycle boundary must leave no queued work");
        }
        ReforgeUIManager.ResetHarness(false); ReforgeUIManager.Select(Weapon(), false);
        ReforgeUIManager.SwitchToReforge(); ReforgeUIManager.RefreshSharedButton();
        Check(!ReforgeUIManager.Clickable && ReforgeSystem.QueryCount == 1, "Ordinary reforge must keep its own cost rule");
        Console.WriteLine("AffixSelectionUI: " + assertions + " assertions passed");
    }
}
