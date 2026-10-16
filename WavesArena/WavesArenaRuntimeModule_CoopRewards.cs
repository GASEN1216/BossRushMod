using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private sealed class CoopArenaReward
        {
            internal string Asset, Room;
            internal CoopArenaPersonal Fact;
            internal InteractableLootbox Box;
            internal VictoryRewardShadowCrateController Shadow;
            internal InfiniteHellMilestoneDelivery Milestone;
            internal long Cash;
            internal int CashPiles;
            internal bool ItemDelivered, Complete, LocalCommitted, StandardLoading, Cancelled;
            internal float RetryAt;
        }
        private readonly Dictionary<string, CoopArenaReward> coopArenaRewards = new Dictionary<string, CoopArenaReward>(StringComparer.Ordinal);

        private void CleanupCoopArenaRewards()
        {
            foreach (CoopArenaReward reward in coopArenaRewards.Values)
                CancelCoopArenaReward(reward);
            coopArenaRewards.Clear();
            CoopArenaPersonalLoot.Shutdown();
        }

        private static void CancelCoopArenaReward(CoopArenaReward reward)
        {
            reward.Cancelled = true;
            if (reward.Shadow != null && reward.Shadow.gameObject != null) UnityEngine.Object.Destroy(reward.Shadow.gameObject);
            // 原生异步 factory 尚持有库存时，等真实续作终止后再销毁未开放箱。
            if (!reward.StandardLoading && !reward.Complete && reward.Box != null) UnityEngine.Object.Destroy(reward.Box.gameObject);
        }

        private void DeliverCoopArenaReward(string kind, long cash, int quality, int itemType, int milestone)
        {
            if (!BossRushCoop.IsHost || string.IsNullOrEmpty(coopArenaEpoch)) return;
            string id = coopArenaEpoch + ":" + kind + ":" + InfiniteHellWaveIndex;
            foreach (var participant in coopArenaWaveParticipants)
                coopPersonal.Deliver(participant.Key, id, new CoopArenaPersonal
                {
                    Kind = kind, Epoch = coopArenaEpoch, Wave = InfiniteHellWaveIndex,
                    Difficulty = quality, Cash = cash, ItemType = itemType, Milestone = milestone,
                    RunSequence = coopArenaRunSequence
                }, participant.Value);
        }

        private bool ApplyCoopArenaReward(CoopArenaPersonal fact)
        {
            string key = BossRushCoop.LocalAssetId + ":" + fact.Epoch + ":" + fact.Kind + ":" + fact.Wave;
            CoopArenaReward reward;
            if (!coopArenaRewards.TryGetValue(key, out reward))
            {
                reward = new CoopArenaReward { Asset = BossRushCoop.LocalAssetId, Room = BossRushCoop.RunId, Fact = fact, Cash = Math.Max(0, fact.Cash) };
                coopArenaRewards.Add(key, reward);
            }
            if (reward.Room != BossRushCoop.RunId || reward.Asset != BossRushCoop.LocalAssetId) return false;
            TryTickCoopArenaReward(reward);
            if (!reward.Complete || reward.Room != BossRushCoop.RunId || reward.Asset != BossRushCoop.LocalAssetId) return false;
            reward.LocalCommitted = true;
            return true;
        }

        private void TickCoopArenaRewards()
        {
            if (coopArenaRewards.Count == 0) return;
            // 个人欠账不依赖世界仍 active；换槽暂挂，回原槽继续。
            List<string> retired = null;
            foreach (var entry in coopArenaRewards)
            {
                CoopArenaReward reward = entry.Value;
                if (reward.Room != BossRushCoop.RunId || reward.LocalCommitted)
                {
                    if (reward.Room != BossRushCoop.RunId) CancelCoopArenaReward(reward);
                    else if (reward.Shadow != null && reward.Shadow.gameObject != null) UnityEngine.Object.Destroy(reward.Shadow.gameObject);
                    if (retired == null) retired = new List<string>(); retired.Add(entry.Key); continue;
                }
                if (!reward.Complete && reward.Asset == BossRushCoop.LocalAssetId) TryTickCoopArenaReward(reward);
            }
            if (retired != null) foreach (string key in retired) coopArenaRewards.Remove(key);
        }

        private void TryTickCoopArenaReward(CoopArenaReward reward)
        {
            if (Time.unscaledTime < reward.RetryAt) return;
            try { TickCoopArenaReward(reward); }
            catch (Exception e) { reward.RetryAt = Time.unscaledTime + 1f; ModBehaviour.DevLog("[BossRush Coop] Personal arena reward retained for retry: " + e.Message); }
        }

        private void TickCoopArenaReward(CoopArenaReward reward)
        {
            if (reward.Cancelled || reward.Complete || owner == null || !BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene
                || CharacterMainControl.Main == null || BossRushUI.IsGamePaused()) return;
            CoopArenaPersonal fact = reward.Fact;
            if (fact.Kind == "box")
            {
                if (reward.Box != null) { TickCoopArenaStandardBox(reward); return; }
                if (reward.Shadow != null) return;
                InteractableLootbox prefab = GetVictoryRewardVisualLootBoxTemplate_LootAndRewards();
                if (prefab == null) return;
                string token = BossRushCoop.SceneToken;
                var go = new GameObject("BossRush_PersonalVictoryCrate");
                MultiSceneCore.MoveToActiveWithScene(go, SceneManager.GetActiveScene().buildIndex);
                var shadow = go.AddComponent<VictoryRewardShadowCrateController>();
                reward.Shadow = shadow;
                if (!shadow.Initialize(owner, CharacterMainControl.Main, prefab, fact.Difficulty,
                    position =>
                    {
                        if (reward.Box == null) reward.Box = SpawnDifficultyRewardLootbox_LootAndRewards(fact.Difficulty, true, position);
                        if (reward.Box == null) return false;
                        TickCoopArenaStandardBox(reward);
                        return reward.Complete;
                    }, () => owner != null && reward.Room == BossRushCoop.RunId && reward.Asset == BossRushCoop.LocalAssetId
                        && BossRushCoop.IsConnected && BossRushCoop.HasBoundScene && token == BossRushCoop.SceneToken))
                { UnityEngine.Object.Destroy(go); reward.Shadow = null; }
                return;
            }
            if (reward.Box == null)
            {
                InteractableLootbox prefab = GetDifficultyRewardLootBoxTemplate();
                if (prefab == null) return;
                var box = UnityEngine.Object.Instantiate(prefab, CharacterMainControl.Main.transform.position, Quaternion.identity);
                LootBoxLoader loader = box.GetComponent<LootBoxLoader>();
                if (loader != null) { loader.autoSetup = false; loader.enabled = false; }
                if (!InteractableLootboxInventoryHelper.EnsureLocalInventory(box) || !CoopArenaPersonalLoot.Attach(box))
                { UnityEngine.Object.Destroy(box.gameObject); return; }
                box.gameObject.SetActive(true); box.needInspect = true;
                reward.Box = box;
            }
            if (fact.Kind == "milestone")
            {
                if (reward.Milestone == null)
                {
                    reward.Milestone = new InfiniteHellMilestoneDelivery(item => reward.Box != null
                        && InteractableLootboxInventoryHelper.TryAddExtraItem(reward.Box.Inventory, item), fact.Milestone - 1);
                    reward.Milestone.Enqueue(fact.Milestone, Vector3.zero);
                }
                reward.Milestone.Tick(owner);
                reward.Complete = reward.Milestone.IsComplete(fact.Milestone) && CoopArenaPersonalLoot.TryPublish(reward.Box);
                return;
            }
            while (reward.Cash > 0 && reward.CashPiles < InfiniteHellMaxCashPiles)
            {
                Item cash = ItemAssetsCollection.InstantiateSync(EconomyManager.CashItemID);
                if (cash == null) return;
                cash.StackCount = (int)Math.Min(reward.Cash, Math.Min((long)cash.MaxStackCount, (long)int.MaxValue));
                int amount = cash.StackCount;
                if (amount <= 0) { cash.DestroyTree(); return; }
                if (!InteractableLootboxInventoryHelper.TryAddExtraItem(reward.Box.Inventory, cash)) return;
                reward.Cash -= amount; reward.CashPiles++;
            }
            if (reward.Cash > 0 && !InfiniteHellMilestoneDelivery.TryCreditAccount(ref reward.Cash)) return;
            if (fact.ItemType > 0 && !reward.ItemDelivered)
            {
                Item item = ItemAssetsCollection.InstantiateSync(fact.ItemType);
                if (item == null || !InteractableLootboxInventoryHelper.TryAddExtraItem(reward.Box.Inventory, item)) return;
                reward.ItemDelivered = true;
            }
            reward.Complete = CoopArenaPersonalLoot.TryPublish(reward.Box);
        }

        private async void TickCoopArenaStandardBox(CoopArenaReward reward)
        {
            if (reward.StandardLoading || reward.Complete || Time.unscaledTime < reward.RetryAt) return;
            reward.StandardLoading = true;
            bool handedToCleanup = false;
            try
            {
                Inventory inventory = reward.Box.Inventory;
                if (inventory == null) return;
                if (inventory.Content == null || inventory.Content.Count < reward.Fact.Difficulty)
                {
                    LootBoxLoader loader = reward.Box.GetComponent<LootBoxLoader>();
                    if (loader == null) return;
                    FieldInfo capacity = typeof(LootBoxLoader).GetField("inventorySize", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (capacity == null) return;
                    capacity.SetValue(loader, Math.Max(1, reward.Fact.Difficulty) + (inventory.Content != null ? inventory.Content.Count : 0));
                    loader.autoSetup = false; loader.dropOnSpawnItem = false;
                    // 原 Loader 的完整异步生成；失败不开放部分箱，不丢同箱原物。
                    await loader.Setup();
                }
                if (reward.Cancelled || owner == null || reward.Box == null || reward.Room != BossRushCoop.RunId) return;
                inventory.Loading = false;
                owner.StartCoroutine(FinalizeCoopArenaStandardBox(reward));
                handedToCleanup = true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Personal victory box setup retained: " + e.Message); }
            finally
            {
                if (!handedToCleanup)
                {
                    reward.StandardLoading = false; reward.RetryAt = Time.unscaledTime + 1f;
                    if (reward.Cancelled && !reward.Complete && reward.Box != null) UnityEngine.Object.Destroy(reward.Box.gameObject);
                }
            }
        }

        private IEnumerator FinalizeCoopArenaStandardBox(CoopArenaReward reward)
        {
            try
            {
                yield return CleanupDifficultyRewardLootboxInventory_LootAndRewards(reward.Box, reward.Fact.Difficulty);
                if (reward.Cancelled || reward.Box == null || reward.Box.Inventory == null || reward.Box.Inventory.Content == null) yield break;
                int qualityCount = 0;
                foreach (Item item in reward.Box.Inventory.Content) if (item != null && item.Quality >= 5) qualityCount++;
                if (qualityCount >= reward.Fact.Difficulty) reward.Complete = CoopArenaPersonalLoot.TryPublish(reward.Box);
            }
            finally
            {
                reward.StandardLoading = false;
                if (!reward.Complete) reward.RetryAt = Time.unscaledTime + 1f;
                if (reward.Cancelled && !reward.Complete && reward.Box != null) UnityEngine.Object.Destroy(reward.Box.gameObject);
            }
        }
    }
}
