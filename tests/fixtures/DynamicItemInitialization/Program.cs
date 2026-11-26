using System;
using System.Linq;
using BossRush;
using BossRush.Patches.ItemStatsSystem;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine.SceneManagement;

internal static class Program
{
    private const int CharacterId = 42;
    private const int GemId = 123;
    private static int checks;

    private static void Check(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException(reason);
        checks++;
    }

    private static void ResetHost()
    {
        BossRushDynamicItemRegistry.ResetStaticCaches();
        ModBehaviour.Instance = new ModBehaviour();
        ModBehaviour.CriticalMessages.Clear();
        BossRushEquipmentRescue.UnresolvedLoadNotes = 0;
        ModBehaviour.ConfiguratorRegistrations = 0;
        ModBehaviour.ConfiguratorsReady = false;
        ModItemIcons.BrokenIcon = 0;
        SceneManager.ChangeActiveScene(true);
        EquipmentFactory.Reset();
        ItemFactory.Reset();
        ItemAssetsCollection.Dynamic.Clear();
        ItemAssetsCollection.Official.Clear();
        ItemAssetsCollection.CollectionAvailable = true;
        ItemAssetsCollection.DeclinedPublishType = 0;
        ItemAssetsCollection.Fallbacks = 0;
        ItemAssetsCollection.LastFallback = null;
        var character = new Item { TypeID = CharacterId };
        character.Slots.Add("Primary", new Slot("Gun"));
        ItemAssetsCollection.Official.Add(CharacterId, character);
        var gem = new Item { TypeID = GemId };
        gem.Tags.Add("Gem");
        ItemAssetsCollection.Official.Add(GemId, gem);
    }

    private static ItemTreeData CharacterTree()
    {
        var data = new ItemTreeData { RootId = 80 };
        var character = new ItemTreeData.DataEntry { InstanceId = 80, typeID = CharacterId };
        var weapon = new ItemTreeData.DataEntry { InstanceId = 61, typeID = DragonKingBossGunConfig.WeaponTypeId };
        var gem = new ItemTreeData.DataEntry { InstanceId = 47, typeID = GemId };
        character.SlotContents.Add("Primary", weapon.InstanceId);
        weapon.SlotContents.Add("Gem", gem.InstanceId);
        weapon.Variables.Add("Durability", 27);
        gem.Variables.Add("Level", 4);
        data.entries.Add(gem);
        data.entries.Add(character);
        data.entries.Add(weapon);
        return data;
    }

    private static ItemTreeData WarehouseTree()
    {
        var data = new ItemTreeData { RootId = 4 };
        var item = new ItemTreeData.DataEntry { InstanceId = 4, typeID = ColdQuenchFluidConfig.TYPE_ID };
        item.Variables.Add("Count", 3);
        data.entries.Add(item);
        return data;
    }

    private static void CheckCharacter(Item root)
    {
        Item weapon = root.Slots["Primary"].Content;
        Check(weapon != null && weapon.TypeID == DragonKingBossGunConfig.WeaponTypeId && !weapon.IsFallback,
            "cold equipment-only restore must configure gun tags before publishing or the official slot discards it");
        Check(weapon.AgentUtilities.CreateAgent() == weapon, "restored gun can be drawn by the official hand slot");
        Check(weapon.InitializeCalls == 1 && weapon.AgentUtilities.BindCalls == 1, "both asynchronous creation hooks share one clone binding");
        Check(weapon.Variables["Durability"] == 27, "weapon instance durability survives restore");
        Item gem = weapon.Slots["Gem"].Content;
        Check(gem != null && gem.TypeID == GemId && gem.Variables["Level"] == 4, "nested gun slot keeps its attachment and instance data");
    }

    private static void CharacterAndWarehouseRoundTrip()
    {
        ResetHost();
        Item character = CharacterTree().Instantiate();
        CheckCharacter(character);
        Check(BossRushEquipmentRescue.UnresolvedLoadNotes == 0, "a fully registered restore does not schedule equipment rescue");
        Check(ModBehaviour.ConfiguratorRegistrations == 1 && EquipmentFactory.Loads == 1 && ItemFactory.Loads == 0,
            "first saved equipment query warms configuration before any item bundle or delayed bootstrap");

        var warehouseData = new InventoryData();
        warehouseData.entries.Add(new InventoryData.Entry { Position = 7, itemTreeData = WarehouseTree() });
        var warehouse = new Inventory();
        warehouseData.LoadIntoInventory(warehouse);
        Item warehouseItem = warehouse.Items[7];
        Check(!warehouseItem.IsFallback && warehouseItem.TypeID == ColdQuenchFluidConfig.TYPE_ID
            && ItemAssetsCollection.Fallbacks == 0 && warehouseItem.Variables["Count"] == 3,
            "warehouse slot receives a real item and its saved count");

        ItemTreeData savedCharacter = ItemTreeData.FromItem(character);
        InventoryData savedWarehouse = InventoryData.FromInventory(warehouse);
        Check(savedCharacter.entries.Count == 3 && savedWarehouse.entries.Count == 1
            && savedWarehouse.entries[0].Position == 7 && savedWarehouse.entries[0].itemTreeData.entries.Count == 1,
            "the next save still contains character, gun and attachment along with the warehouse item");
        ResetHost();
        warehouse = new Inventory();
        savedWarehouse.LoadIntoInventory(warehouse);
        Check(!warehouse.Items[7].IsFallback && warehouse.Items[7].Variables["Count"] == 3, "warehouse-first cold restart preserves the saved position and data");
        CheckCharacter(savedCharacter.Instantiate());
        CheckCharacter(savedCharacter.Instantiate());
        Check(ItemAssetsCollection.Fallbacks == 0 && ModBehaviour.ConfiguratorRegistrations == 1
            && EquipmentFactory.Loads == 1 && ItemFactory.Loads == 1,
            "warm reads do not register configurators or open either bundle again");
    }

    private static void RetriesUnpublishedPrefab()
    {
        ResetHost();
        ItemAssetsCollection.DeclinedPublishType = DragonKingBossGunConfig.WeaponTypeId;
        Check(!BossRushDynamicItemRegistry.EnsureRegistered(DragonKingBossGunConfig.WeaponTypeId),
            "declining the requested prefab must not mark it available");
        Check(BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(EquipmentFactory.Armor.TypeID)
            && !EquipmentFactory.Registration.AllPrefabsLive(), "a surviving sibling does not complete the bundle");
        ItemAssetsCollection.DeclinedPublishType = 0;
        CheckCharacter(CharacterTree().Instantiate());
        Check(EquipmentFactory.Loads == 2 && EquipmentFactory.ConfigurationRuns == 2
            && EquipmentFactory.Registration.AllPrefabsLive(),
            "the pending prefab retries publication without reconfiguring it or the published sibling");
    }

    private static void RecoverAcrossOneFrame()
    {
        int typeId = DragonKingBossGunConfig.WeaponTypeId;
        ResetHost();
        ItemAssetsCollection.DeclinedPublishType = typeId;
        var invalidPrefab = new Item { TypeID = typeId };
        ItemAssetsCollection.Dynamic[typeId] = invalidPrefab;
        UnityEngine.Object.Destroy(invalidPrefab.gameObject);
        UniTask<Item> operation = ItemAssetsCollection.BeginInstantiateAsync(typeId);
        Item fallback = ItemAssetsCollection.LastFallback;
        Check(UniTask.WaitingForFrame && !operation.GetAwaiter().IsCompleted && fallback != null,
            "bypass the destroyed dynamic prefab and do not deliver its fallback to the item tree in the first frame");
        ItemAssetsCollection.DeclinedPublishType = 0;
        UniTask.AdvanceFrame();
        Item gun = operation.GetAwaiter().GetResult();
        Check(gun != null && !gun.IsFallback && gun.TypeID == typeId && gun.AgentUtilities.CreateAgent() == gun,
            "publish the configured weapon before asynchronous restore returns to the official item tree");
        Check(fallback == null && fallback.gameObject == null, "release the unowned fallback after a real replacement exists");

        ResetHost();
        ItemAssetsCollection.DeclinedPublishType = typeId;
        operation = ItemAssetsCollection.BeginInstantiateAsync(typeId);
        Check(UniTask.WaitingForFrame, "persistent absence gets one frame to recover");
        UniTask.AdvanceFrame();
        Check(operation.GetAwaiter().IsCompleted && operation.GetAwaiter().GetResult() == ItemAssetsCollection.LastFallback,
            "persistent absence returns the same fallback after one frame");
        Check(BossRushEquipmentRescue.UnresolvedLoadNotes > 0,
            "a fallback handed to the official item tree is reported so equipment rescue checks the saved record after the level loads");

        ResetHost();
        ItemAssetsCollection.DeclinedPublishType = typeId;
        operation = ItemAssetsCollection.BeginInstantiateAsync(typeId);
        Check(UniTask.WaitingForFrame, "capture the active mod owner before the frame delay");
        ModBehaviour.Instance = new ModBehaviour();
        ItemAssetsCollection.DeclinedPublishType = 0;
        UniTask.AdvanceFrame();
        Check(operation.GetAwaiter().GetResult() == ItemAssetsCollection.LastFallback,
            "an operation from a previous mod owner does not replace items in the new owner");
    }

    private static void SynchronousFallback()
    {
        ResetHost();
        int typeId = DragonKingBossGunConfig.WeaponTypeId;
        ItemAssetsCollection.DeclinedPublishType = typeId;
        Item fallback = ItemAssetsCollection.InstantiateSync(typeId);
        Check(fallback.IsFallback && fallback.TypeID == typeId && !ItemAssetsCollection.Dynamic.ContainsKey(typeId),
            "known missing Mod prefab follows the official fallback before the original sync path can dereference a missing entry");
        Check(ItemAssetsCollection.InstantiateSync(GemId).TypeID == GemId,
            "official synchronous creation still uses the original path");
    }

    private static void RecoveryRespectsStartingScene()
    {
        ResetHost();
        int typeId = DragonKingBossGunConfig.WeaponTypeId;
        ItemAssetsCollection.DeclinedPublishType = typeId;
        UniTask<Item> operation = ItemAssetsCollection.BeginInstantiateAsync(typeId);
        Item fallback = ItemAssetsCollection.LastFallback;
        Check(UniTask.WaitingForFrame && !operation.GetAwaiter().IsCompleted && fallback != null,
            "the request captures its starting scene before waiting for resources");
        SceneManager.ChangeActiveScene(true);
        ItemAssetsCollection.DeclinedPublishType = 0;
        UniTask.AdvanceFrame();
        Check(operation.GetAwaiter().GetResult() == fallback && fallback != null
            && !ItemAssetsCollection.Dynamic.ContainsKey(typeId),
            "an unloaded starting scene must not create or publish a replacement for its old request");

        ResetHost();
        ItemAssetsCollection.DeclinedPublishType = typeId;
        operation = ItemAssetsCollection.BeginInstantiateAsync(typeId);
        fallback = ItemAssetsCollection.LastFallback;
        SceneManager.ChangeActiveScene(false);
        ItemAssetsCollection.DeclinedPublishType = 0;
        UniTask.AdvanceFrame();
        Item replacement = operation.GetAwaiter().GetResult();
        Check(replacement != null && !replacement.IsFallback && fallback == null,
            "changing the active scene without unloading the starting scene permits recovery");
    }

    private static void WarehouseRetainsTypeWhenCollectionIsMissing()
    {
        ResetHost();
        ItemAssetsCollection.CollectionAvailable = false;
        var warehouseData = new InventoryData();
        warehouseData.entries.Add(new InventoryData.Entry { Position = 7, itemTreeData = WarehouseTree() });
        var warehouse = new Inventory();
        var operation = warehouseData.LoadIntoInventoryAsync(warehouse);
        Check(UniTask.WaitingForFrame && !operation.IsCompleted && ItemAssetsCollection.Fallbacks == 0,
            "the missing official collection returns null and cannot place a warehouse item in the first frame");
        UniTask.AdvanceFrame();
        operation.GetAwaiter().GetResult();
        Item item;
        Check(warehouse.Items.TryGetValue(7, out item) && item.IsFallback
            && item.TypeID == ColdQuenchFluidConfig.TYPE_ID && item.Variables["Count"] == 3,
            "persistent null must become a same-type fallback with saved quantity in its warehouse slot");
        InventoryData saved = InventoryData.FromInventory(warehouse);
        Check(saved.entries.Count == 1 && saved.entries[0].Position == 7
            && saved.entries[0].itemTreeData.entries[0].typeID == ColdQuenchFluidConfig.TYPE_ID,
            "the next warehouse snapshot keeps the unresolved item identity and position");
    }

    private static void RestoresSplitModelsForAlreadyPublishedItems()
    {
        var samples = new (int TypeId, string Bundle, string ModelName, bool Melee)[]
        {
            (DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID, "fenhuang_halberd_model", FenHuangHalberdIds.ModelBaseName, true),
            (FrostmourneIds.WeaponTypeId, "frostmourne_model", FrostmourneIds.ModelBaseName, true),
            (NewWeaponIds.ViperDaggerTypeId, "viperdagger_melee_model", NewWeaponIds.ViperDaggerModelBaseName, true),
            (NewWeaponIds.SummonStaffTypeId, "summonstaff_melee_model", NewWeaponIds.SummonStaffModelBaseName, true),
            (NewWeaponIds.EnergyShieldTypeId, "energyshield_totem_model", NewWeaponIds.EnergyShieldModelBaseName, false),
            (NewWeaponIds.FrostSpearTypeId, "frostspear_melee_model", NewWeaponIds.FrostSpearModelBaseName, true)
        };
        foreach (var sample in samples)
        {
            ResetHost();
            EquipmentFactory.SplitBundle = sample.Bundle;
            EquipmentFactory.SplitModelName = sample.ModelName;
            ItemFactory.SplitTypeId = sample.TypeId;
            ItemFactory.SplitModelIsMelee = sample.Melee;
            var prefab = new Item { TypeID = sample.TypeId, MeleeModel = new ItemAgent(), EquipmentModel = new ItemAgent() };
            Check(BossRushDynamicItemRegistry.Publish(prefab), "initially publish split model item " + sample.TypeId);

            Check(BossRushDynamicItemRegistry.EnsureRegistered(sample.TypeId)
                && EquipmentFactory.SplitBundleLoads == 1 && ItemFactory.SplitConfigurations == 0
                && ItemFactory.Loads == 0 && ItemAssetsCollection.Dynamic[sample.TypeId] == prefab,
                "keep the registered item and skip configuration until its exact split model exists " + sample.TypeId);

            EquipmentFactory.SplitBundleAvailable = true;
            Item item = ItemAssetsCollection.BeginInstantiateAsync(sample.TypeId).GetAwaiter().GetResult();
            ItemAgent boundModel = sample.Melee ? item.MeleeModel : item.EquipmentModel;
            Check(boundModel != null && boundModel == EquipmentFactory.SplitModel && !item.IsFallback
                && ItemFactory.ConfiguredSplitPrefab == prefab && ItemFactory.SplitConfigurations == 1,
                "bind the requested split model to the existing prefab before the official clone is returned " + sample.TypeId);

            ItemAssetsCollection.BeginInstantiateAsync(sample.TypeId).GetAwaiter().GetResult();
            Check(EquipmentFactory.SplitBundleLoads == 2 && ItemFactory.SplitConfigurations == 1
                && ItemFactory.Loads == 0 && ModBehaviour.ConfiguratorRegistrations == 1
                && ItemAssetsCollection.Dynamic[sample.TypeId] == prefab,
                "warm split model hits do not reload the model, reload the item bundle, or reconfigure the prefab " + sample.TypeId);
        }
    }

    private static void InstanceInitialization()
    {
        ResetHost();
        DynamicItemRegistrationPatchSupport.InitializeInstance(null);
        foreach (int id in new[] { BossRushItemIds.CodexBook, BossRushItemIds.RelicEgg })
        {
            var item = new Item { TypeID = id, HasHandheldAgent = true };
            Check(item.AgentUtilities.CreateAgent() == null, "inactive clone reproduces missing master before repair");
            DynamicItemRegistrationPatchSupport.InitializeInstance(item);
            Check(item.AgentUtilities.CreateAgent() == item, "pickup and handheld agent bind to actual clone");
            DynamicItemRegistrationPatchSupport.InitializeInstance(item);
            Check(item.InitializeCalls == 1 && item.AgentUtilities.BindCalls == 1, "repeat restore is idempotent");
        }
        var stale = new Item { TypeID = BossRushItemIds.CodexBook, AlreadyInitialized = true };
        stale.AgentUtilities.Master = new Item();
        DynamicItemRegistrationPatchSupport.InitializeInstance(stale);
        Check(stale.AgentUtilities.Master == stale, "stale initialized clone repairs its owner");
        var official = new Item { TypeID = GemId };
        DynamicItemRegistrationPatchSupport.InitializeInstance(official);
        Check(official.InitializeCalls == 0, "official items remain on official lifecycle");
        var failed = new Item { TypeID = BossRushItemIds.CodexBook, ThrowOnInitialize = true };
        DynamicItemRegistrationPatchSupport.InitializeInstance(failed);
        Check(ModBehaviour.Errors == 1 && failed.AgentUtilities.Master == null,
            "host failure is diagnosed without pretending initialized");
    }

    private static void Main()
    {
        int[] published = BossRushDynamicItemRegistry.GetPublishedTypeIds();
        int[] itemLedger = Ledger.Ids.Except(new[] { DragonBreathConfig.BUFF_ID, PhantomWitchConfig.CurseBuffID }).ToArray();
        Check(published.SequenceEqual(itemLedger),
            "published registration plans differ from the TypeID ledger. Missing: "
            + string.Join(", ", itemLedger.Except(published)) + ". Extra: " + string.Join(", ", published.Except(itemLedger)));
        Check(ModBehaviour.Errors == 0, "registration plan construction reported errors: "
            + string.Join(" | ", ModBehaviour.CriticalMessages));
        Check(itemLedger.All(BossRushDynamicItemRegistry.IsBossRushDynamicItemType), "every released item TypeID uses the production restore and instance hooks");
        BundleSourceAudit.Verify(Check);
        CharacterAndWarehouseRoundTrip();
        RetriesUnpublishedPrefab();
        RecoverAcrossOneFrame();
        SynchronousFallback();
        RecoveryRespectsStartingScene();
        WarehouseRetainsTypeWhenCollectionIsMissing();
        RestoresSplitModelsForAlreadyPublishedItems();
        InstanceInitialization();
        ResetHost();
        ModItemIcons.BrokenIcon = DragonKingBossGunConfig.WeaponTypeId;
        Check(BossRushDynamicItemRegistry.EnsureRegistered(ModItemIcons.BrokenIcon)
            && ItemAssetsCollection.Dynamic[ModItemIcons.BrokenIcon] == EquipmentFactory.Weapon && ModBehaviour.Errors > 0,
            "an icon failure must not turn a real equipped weapon into a fallback");
        Console.WriteLine("DynamicItemInitialization: PASS " + checks + " checks, " + published.Length + " published TypeIDs");
    }
}
