using System;
using System.Collections.Generic;
using BossRush;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

// Only the projection's lookup/Unity registration is adapted. TryCommitDelivery is extracted
// verbatim by run.py; item reservation, placement and the Campaign persistence chain are linked.
namespace BossRush
{
    internal static partial class OfficialQuestProjection
    {
        private sealed class Entry { internal OfficialQuestBinding Binding; internal bool Delivering; }
        private static Entry entry;
        internal static void Bind(OfficialQuestBinding value) { entry = new Entry { Binding = value }; }
        private static bool TryGetOwned(int id, out Entry result)
        { result = entry; return result != null && result.Binding.QuestId == id; }
    }
}

partial class Program
{
    private const int DeliveryQuestId = 590101, RewardType = 999, InputType = 777;

    private sealed class DeliveryClient : IOfficialQuestClient
    {
        public string LogTag { get { return "delivery-regression"; } }
        public bool Ready { get { return true; } }
        public int Slot { get { return SavesSystem.CurrentSlot; } }
        public void BeginTick() { }
        public void EndTick(bool dirty) { }
        public void RefreshMarkers() { }
    }

    private static OfficialQuestBinding PrepareDelivery(bool inboxOnly = false)
    {
        Reset(); PrepareCampaign(); CampaignPersistence.EnsureSubscribed();
        CharacterMainControl.Main.CharacterItem.Inventory.AddAt(new Item { TypeID = InputType, StackCount = 3 }, 0);
        CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");
        PlayerStorage.Inventory.Save("PlayerStorage"); PlayerStorageBuffer.SaveBuffer();
        SavesSystem.SaveFile(false); SavesSystem.Writes = 0; SavesSystem.History.Clear();
        var binding = new OfficialQuestBinding
        {
            QuestId = DeliveryQuestId, Client = new DeliveryClient(),
            CanDeliver = () => true,
            IsDelivered = () => CampaignProgressService.GetState("ch1") == CampaignChapterState.Completed,
            Deliver = (out string reason) => { reason = null; return CampaignProgressService.TryDeliver("ch1"); },
            BeginDelivery = CampaignSaveCoordinator.BeginQuestDelivery,
            EndDelivery = CampaignSaveCoordinator.EndQuestDelivery,
            RewardsToInbox = () => inboxOnly,
            RewardItems = new[] { new OfficialQuestItemStack(RewardType, 3) },
            Submissions = inboxOnly ? null : new[] { new OfficialQuestSubmission { TypeIds = new[] { InputType }, Count = 2 } }
        };
        OfficialQuestProjection.Bind(binding);
        return binding;
    }

    private static int CountOnDisk(string key, int type)
    {
        object value; int count;
        return SavesSystem.Disk.TryGetValue(key + "_counts", out value)
            && ((Dictionary<int, int>)value).TryGetValue(type, out count) ? count : 0;
    }

    private static int BufferedOnDisk(int type)
    {
        object value; int count = 0;
        if (SavesSystem.Disk.TryGetValue("PlayerStorage_Buffer", out value))
            foreach (ItemTreeData item in (List<ItemTreeData>)value) if (item.RootTypeID == type) count += item.Count;
        return count;
    }

    private static int LiveRewards()
    {
        int count = OfficialQuestItems.HeldInBackpack(RewardType);
        foreach (Item item in PlayerStorage.Inventory) if (item != null && item.TypeID == RewardType) count += item.StackCount;
        foreach (ItemTreeData item in PlayerStorage.IncomingItemBuffer) if (item.RootTypeID == RewardType) count += item.Count;
        return count;
    }

    private static void QuestDeliveryTransactions()
    {
        string reason;
        PrepareDelivery();
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason), "official quest accepts complete item/cash/fact transaction");
        Check(CampaignClaimedOnDisk() && DiskMoney == 104000 && CountOnDisk("MainCharacterItemData", RewardType) == 3
            && CountOnDisk("MainCharacterItemData", InputType) == 1 && SavesSystem.Writes == 1,
            "completion, reward items, consumed submissions and cash share one physical snapshot");
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && LiveRewards() == 3 && EconomyManager.Adds == 1,
            "repeat delivery never regrants rewards or consumes more submissions");

        PrepareDelivery(); CharacterMainControl.Main.CharacterItem.Inventory.Capacity = 1;
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && CountOnDisk("PlayerStorage", RewardType) == 3,
            "full backpack stores reward and completion together in storage");
        PrepareDelivery(); CharacterMainControl.Main.CharacterItem.Inventory.Capacity = 1; PlayerStorage.Inventory.Capacity = 0;
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && BufferedOnDisk(RewardType) == 3 && CampaignClaimedOnDisk(),
            "full backpack and storage persist reward in the official collection buffer");

        PrepareDelivery(true); CharacterMainControl.Main.CharacterItem.Inventory.AddAt(new Item { TypeID = 888, StackCount = 4 }, 4);
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && BufferedOnDisk(RewardType) == 3,
            "raid reward is durable in the base collection buffer");
        Check(CountOnDisk("MainCharacterItemData", 888) == 0 && !CharacterMainControl.Main.CharacterItem.Destroyed,
            "raid reward does not persist unrelated in-raid backpack loot");

        PrepareDelivery(); ItemAssetsCollection.MissingPrefabId = RewardType;
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && !CampaignClaimedOnDisk()
            && OfficialQuestItems.HeldInBackpack(InputType) == 3 && LiveRewards() == 0 && EconomyManager.Adds == 0,
            "missing prefab leaves submissions, completion and money unchanged");

        OfficialQuestBinding binding = PrepareDelivery();
        binding.RewardItems = new[] { new OfficialQuestItemStack(RewardType, 23) };
        ItemUtilities.DuringDelivery = () =>
        {
            CharacterMainControl.Main.CharacterItem.Inventory.Reject = true;
            PlayerStorage.Inventory.Reject = true; ItemUtilities.ThrowBeforeBuffer = true;
        };
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && LiveRewards() == 0
            && OfficialQuestItems.HeldInBackpack(InputType) == 3 && !CampaignClaimedOnDisk() && EconomyManager.Adds == 0,
            "second reward placement failure retracts first item and restores all submissions");

        binding = PrepareDelivery(); binding.Deliver = (out string message) => { message = "rejected"; return false; };
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && LiveRewards() == 0
            && OfficialQuestItems.HeldInBackpack(InputType) == 3 && EconomyManager.Adds == 0,
            "client rejection rolls back placed rewards and reserved submissions");

        PrepareDelivery(true); ItemUtilities.ThrowAfterBuffer = true;
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && BufferedOnDisk(RewardType) == 3,
            "buffer notification exception is recognized by the actual receipt and never duplicates");
        binding = PrepareDelivery(true); ItemUtilities.ThrowAfterBuffer = true;
        binding.Deliver = (out string message) => { message = "rejected"; return false; };
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && LiveRewards() == 0 && !CampaignClaimedOnDisk(),
            "buffer receipt is precisely removed when client rejects after successful placement");

        PrepareDelivery(); bool nestedAccepted = true; int writesInside = -1;
        ItemUtilities.DuringDelivery = () =>
        {
            string ignored; nestedAccepted = OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out ignored);
            CampaignSaveCoordinator.RequestFlush(); SavesSystem.Collect(); writesInside = SavesSystem.Writes;
        };
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && !nestedAccepted && writesInside == 0
            && EconomyManager.Adds == 1 && LiveRewards() == 3,
            "reentrant interaction and client collection are gated during item reservation");

        binding = PrepareDelivery(); binding.Deliver = (out string message) =>
        { message = null; CampaignProgressService.TryDeliver("ch1"); throw new InvalidOperationException("notification"); };
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && CampaignClaimedOnDisk() && LiveRewards() == 3,
            "accepted fact survives client notification exception without reward rollback");

        PrepareDelivery(); SavesSystem.FailPhysical = 1;
        Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && !CampaignClaimedOnDisk()
            && CampaignSaveCoordinator.HasDeferredFlush && LiveRewards() == 3,
            "physical failure retains complete accepted delivery and retry obligation");
        UnityEngine.Time.frameCount++; CampaignSaveCoordinator.Tick();
        Check(CampaignClaimedOnDisk() && CountOnDisk("MainCharacterItemData", RewardType) == 3 && EconomyManager.Adds == 1,
            "physical retry persists item/cash/fact without replaying delivery");

        foreach (string key in new[] { "MainCharacterItemData", "PlayerStorage", "PlayerStorage_Buffer", "EconomyData" })
        {
            PrepareDelivery(); SavesSystem.FailKey = key;
            Check(OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && !CampaignClaimedOnDisk()
                && CampaignSaveCoordinator.HasDeferredFlush, "failed asset/cash collection retains delivery: " + key);
            UnityEngine.Time.frameCount++; CampaignSaveCoordinator.Tick();
            Check(CampaignClaimedOnDisk() && CountOnDisk("MainCharacterItemData", RewardType) == 3 && EconomyManager.Adds == 1,
                "asset/cash collection retries without regrant: " + key);
        }

        PrepareDelivery(); ItemUtilities.DuringDelivery = () => SavesSystem.SetFile(2);
        Check(!OfficialQuestProjection.TryCommitDelivery(DeliveryQuestId, out reason) && LiveRewards() == 0
            && EconomyManager.Adds == 0 && !CampaignClaimedOnDisk() && !CampaignSaveCoordinator.HasDeferredFlush,
            "slot change during placement cannot deliver or persist old-slot rewards in new slot");
    }
}
