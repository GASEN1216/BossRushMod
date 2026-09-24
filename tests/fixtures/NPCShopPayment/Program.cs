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
        Console.WriteLine("NPCShopPayment: PASS (" + assertions + " assertions)");
    }
}
