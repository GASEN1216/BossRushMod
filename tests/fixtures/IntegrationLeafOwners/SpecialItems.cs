using System.IO;
using BossRush;
using BossRush.Utils;
using Duckov.Buffs;
using Duckov.ItemUsage;
using ItemStatsSystem;
using UnityEngine;

internal static partial class Program
{
    private static void SpecialItemRegistrations()
    {
        Reset();
        string root = Path.Combine(Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location), "Special Items");
        string assetRoot = Path.Combine(root, "Assets");
        string uiRoot = Path.Combine(assetRoot, "ui");
        Directory.CreateDirectory(uiRoot);
        var owner = new ModBehaviour();
        ModBehaviour.Instance = owner;
        owner.Setup(null, new Duckov.Modding.ModInfo { path = root });

        Check(!owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(), "missing cake bundle must leave registration retryable");
        Check(!owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry(), "missing wiki bundle must leave registration retryable");
        File.WriteAllText(Path.Combine(assetRoot, "birthday_cake"), "fixture marker");
        File.WriteAllText(Path.Combine(uiRoot, "bossrush_wiki"), "fixture marker");

        var cake = Prefab(BossRushItemIds.BirthdayCake);
        Item cakeItem = cake.GetComponent<Item>();
        GameplayDataSettings.Buffs.allBuffs.Add(new Buff { name = "Happy", DisplayName = "Happy" });
        ResourceBundleLoader.Assets = new UnityEngine.Object[] { cake };
        ItemAssetsCollection.Rejected.Add(cakeItem.TypeID);
        Check(!owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry() && cakeItem.AppliedUsage != null,
            "cake registration resumes after missing bundle and leaves a rejected publish retryable");
        ItemAssetsCollection.Rejected.Clear();
        bool cakeReentered = false;
        ItemAssetsCollection.BeforePublish = item =>
        {
            cakeReentered = true;
            Check(!owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(), "cake cannot reenter an uncommitted registration");
        };
        Check(owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry() && cakeReentered, "cake retries and publishes its live prefab");
        Check(cakeItem.AppliedUsage.behaviors.Count == 2 && cake.Components.FindAll(c => c is FoodDrink || c is AddBuff).Count == 2,
            "cake retry keeps one food and one buff behavior");
        Check(cake.GetComponent<FoodDrink>().energyValue == 100 && cake.GetComponent<AddBuff>().chance == 1f,
            "cake retry keeps its configured food and buff behavior");
        ItemAssetsCollection.BeforePublish = null;
        Probe.Events.Clear();
        Check(owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(), "ready cake keeps its live prefab");
        Sequence();
        UnityEngine.Object.Destroy(cake);
        cake = Prefab(BossRushItemIds.BirthdayCake);
        ResourceBundleLoader.Assets = new UnityEngine.Object[] { cake };
        Check(owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(), "destroyed cake prefab can be loaded again");

        var book = Prefab(500100);
        book.name = "WikiBook";
        var ui = new GameObject { name = "WikiUI" };
        var bookItem = book.GetComponent<Item>();
        ResourceBundleLoader.Assets = new GameObject[] { book, ui };
        GameObject.ThrowOnComponent = "WikiBookUsageBehavior";
        Check(!owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry(), "wiki configuration failure must stay retryable");
        GameObject.ThrowOnComponent = null;
        ItemAssetsCollection.Rejected.Add(BossRushItemIds.AdventureJournal);
        Check(!owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry() && bookItem.AppliedUsage != null,
            "wiki registration resumes after configuration failure and leaves a rejected publish retryable");
        ItemAssetsCollection.Rejected.Clear();
        bool wikiReentered = false;
        ItemAssetsCollection.BeforePublish = item =>
        {
            wikiReentered = true;
            Check(!owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry(), "wiki cannot reenter an uncommitted registration");
        };
        Check(owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry() && wikiReentered, "wiki retries and publishes its live prefab");
        Check(bookItem.TypeID == BossRushItemIds.AdventureJournal && bookItem.AppliedUsage.behaviors.Count == 1
            && book.Components.FindAll(c => c is WikiBookUsageBehavior).Count == 1 && WikiUIManager.Instance.UIPrefab == ui,
            "wiki retry keeps one use behavior, the published TypeID, and its UI prefab");
        ItemAssetsCollection.BeforePublish = null;
        var reenabledOwner = new ModBehaviour();
        WikiUIManager.Instance.UIPrefab = null;
        Check(reenabledOwner.EnsureAdventureJournalItemRegisteredForDynamicRegistry() && WikiUIManager.Instance.UIPrefab == ui,
            "a new owner can attach wiki UI to a book that remains registered");
        UnityEngine.Object.Destroy(book);
        book = Prefab(500100);
        book.name = "WikiBook";
        ResourceBundleLoader.Assets = new[] { book, ui };
        Check(owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry(), "destroyed wiki prefab can be loaded again");
        ModBehaviour.Instance = null;
    }
}
