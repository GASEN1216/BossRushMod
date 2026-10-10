using System;
using BossRush;

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string message)
    {
        assertions++;
        if (!ok) throw new Exception(message);
    }

    private static StockShop Shop(out StockShop.Entry entry)
    {
        var shop = new StockShop();
        entry = new StockShop.Entry { CurrentStock = 2, Show = false };
        shop.entries.Add(entry);
        StockShopView.Instance = new StockShopView();
        NotificationText.Messages.Clear();
        return shop;
    }

    private static void Main()
    {
        StockShop.Entry entry;
        StockShop shop = Shop(out entry);
        var cashItem = new Item();
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7);
        NPCShopSystem.Purchase(shop, cashItem);
        Check(!cashItem.Detached && entry.CurrentStock == 2 && NPCShopSystem.RefreshCount == 0,
            "cash purchases follow the official callback without a second payment");

        int spent = 0;
        var purification = NPCShopPaymentStrategy.Purification(
            price => price <= 7, price => { spent += price; return true; });
        var paidItem = new Item();
        NPCShopSystem.BeginTest(shop, purification, 7);
        NPCShopSystem.Purchase(shop, paidItem);
        Check(spent == 7 && !paidItem.Detached && NPCShopSystem.RefreshCount == 1,
            "purification payment follows the official purchase callback");

        shop = Shop(out entry);
        var denied = new Item();
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Purification(
            price => false, price => false), 7);
        NPCShopSystem.Purchase(shop, denied);
        Check(denied.Detached && denied.gameObject.Destroyed && entry.CurrentStock == 3 && entry.Show,
            "failed payment removes delivery and restores exactly one stock unit");
        Check(StockShopView.Instance.Refreshes == 1 && NPCShopSystem.RefreshCount == 1,
            "failed payment refreshes stock and currency UI");

        shop = Shop(out entry);
        var missingPrice = new Item();
        NPCShopSystem.BeginTest(shop, purification, 0);
        NPCShopSystem.Purchase(shop, missingPrice);
        Check(missingPrice.Detached && entry.CurrentStock == 3 && spent == 7,
            "missing price cannot become a free purchase");

        shop = Shop(out entry);
        var foreign = new Item();
        NPCShopSystem.BeginTest(shop, purification, 7);
        NPCShopSystem.Purchase(new StockShop(), foreign);
        Check(!foreign.Detached && spent == 7 && NPCShopSystem.RefreshCount == 0,
            "another shop's callback cannot spend this shop's points");

        var sold = new Item();
        Cost.Cash = 20;
        NPCShopSystem.Sell(shop, sold, 5);
        Check(Cost.Cash == 15 && ItemUtilities.Returned == sold,
            "purification shop rejects selling and returns the sold item");
        ShopLifecycle();
        Console.WriteLine("NPCShopPayment: PASS (" + assertions + " assertions)");
    }

    private static void ShopLifecycle()
    {
        StockShop.Entry entry;
        StockShop shop = Shop(out entry);
        var owner = new UnityEngine.Transform();
        var childOwner = new UnityEngine.Transform { parent = owner };
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        StockShopView view = StockShopView.Instance;
        view.Target = shop;
        view.open = view.InputBlocked = true;
        ShopController controller = NPCShopSystem.ControllerForTest;
        var display = new Item();
        var inventoryItem = new Item { InInventory = new object() };
        NPCShopSystem.OwnDisplayForTest(display);
        NPCShopSystem.OwnDisplayForTest(inventoryItem);
        view.Closing = delegate
        {
            Check(!NPCShopSystem.ActiveForTest && ManagedUIElement.CloseListeners == 0
                && StockShop.PurchaseListeners == 0 && StockShop.SellListeners == 0,
                "service and event listeners are released before closing the official view");
            Check(!shop.Destroyed && !display.Destroyed,
                "the view closes before its target and display items are destroyed");
            NPCShopSystem.CloseShop();
        };
        NPCShopSystem.CloseShopIfOwnedBy(new UnityEngine.Transform());
        Check(view.Closes == 0 && NPCShopSystem.ActiveForTest,
            "disposing an unrelated owner preserves this active shop");
        NPCShopSystem.CloseShopIfOwnedBy(childOwner);
        Check(view.Closes == 1 && !view.open && !view.InputBlocked && controller.Farewells == 1,
            "owner disposal closes the matching view once and releases input without reentry");
        Check(shop == null && shop.Destroyed && shop.gameObject.Destroyed
            && display == null && display.Destroyed && !inventoryItem.Destroyed,
            "shop destruction cascades to components and only destroys detached display items");
        NPCShopSystem.CloseShopIfOwnedBy(owner);
        NPCShopSystem.ResetStaticCaches();
        Check(view.Closes == 1, "repeated close and static reset remain idempotent");

        shop = Shop(out entry);
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        view = StockShopView.Instance;
        view.Target = shop;
        view.open = view.InputBlocked = true;
        NPCShopSystem.ResetStaticCaches();
        Check(view.Closes == 1 && !view.InputBlocked && !NPCShopSystem.ActiveForTest && shop == null,
            "static reset closes the owned official view before destroying its shop");

        shop = Shop(out entry);
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        view = StockShopView.Instance;
        view.Target = shop;
        view.open = view.InputBlocked = true;
        NPCShopSystem.CleanupForTest();
        Check(view.Closes == 1 && !view.InputBlocked && !NPCShopSystem.ActiveForTest
            && ManagedUIElement.CloseListeners == 0 && StockShop.PurchaseListeners == 0,
            "direct cleanup also deactivates service and unsubscribes before closing");

        shop = Shop(out entry);
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        view = StockShopView.Instance;
        var foreignShop = new StockShop();
        view.Target = foreignShop;
        view.open = view.InputBlocked = true;
        NPCShopSystem.CloseShop();
        Check(view.Closes == 0 && view.open && view.InputBlocked && !foreignShop.Destroyed && shop == null,
            "cleanup leaves a view displaying another merchant open");

        shop = Shop(out entry);
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        view = StockShopView.Instance;
        view.Target = shop;
        view.open = false;
        NPCShopSystem.CloseShop();
        Check(view.Closes == 0 && shop == null, "an already closed matching view is not closed again");

        shop = Shop(out entry);
        NPCShopSystem.BeginTest(shop, NPCShopPaymentStrategy.Cash, 7, owner);
        StockShopView.Instance = null;
        NPCShopSystem.ResetStaticCaches();
        Check(shop == null && !NPCShopSystem.ActiveForTest && ManagedUIElement.CloseListeners == 0,
            "static reset also cleans up after the official UI scene has unloaded");
    }
}
