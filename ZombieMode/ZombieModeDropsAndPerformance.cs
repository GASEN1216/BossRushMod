using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using Duckov.Scenes;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private readonly List<ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkerScratch = new List<ZombieModeEnemyRuntimeMarker>();
        private static readonly string[] ZombieModeDropTagWeapon = { "Weapon" };
        private static readonly string[] ZombieModeDropTagArmor = { "Armor" };
        private static readonly string[] ZombieModeDropTagAmmo = { "Ammo" };
        private static readonly string[] ZombieModeDropTagMedical = { "Medical" };
        private static readonly string[] ZombieModeDropTagFood = { "Food" };

        private int CollectZombieModeRuntimeEnemyMarkers(
            int runId,
            List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();
            if (!IsZombieModeRunValid(runId))
            {
                return 0;
            }

            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null || record.RunId != runId)
                {
                    continue;
                }

                if (record.Kind != ZombieModeRunOnlyObjectKind.Enemy &&
                    (!includeBosses || record.Kind != ZombieModeRunOnlyObjectKind.Boss))
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                }

                if (marker == null ||
                    marker.RunId != runId ||
                    marker.DeathSettled ||
                    marker.RemovedFromRuntime)
                {
                    continue;
                }

                results.Add(marker);
            }

            return results.Count;
        }

        private bool InitializeZombieModeContainersShell(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            if (zombieModeRunState.MapProfile != null &&
                !zombieModeRunState.MapProfile.ContainerRefillEnabled)
            {
                zombieModeRunState.ContainersRefilled = true;
                return true;
            }

            InteractableLootbox[] lootboxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>(true);
            Scene activeScene = SceneManager.GetActiveScene();
            int refilled = 0;
            for (int i = 0; i < lootboxes.Length; i++)
            {
                InteractableLootbox lootbox = lootboxes[i];
                if (lootbox == null || lootbox.gameObject == null || lootbox.gameObject.scene != activeScene)
                {
                    continue;
                }

                if (!TryCreateZombieModeLootboxLocalInventory(lootbox))
                {
                    continue;
                }

                ClearZombieModeLootboxInventory(lootbox);
                RefillZombieModeLootboxInventory(runId, lootbox, 1, 4, 1, Mathf.Clamp(3 + zombieModeRunState.PollutionTier, 3, 6), false);
                LockZombieModeContainerUntilStarterChoice(runId, lootbox);
                refilled++;
            }

            DevLog("[ZombieMode] 已清空并重填地图容器: " + refilled);
            zombieModeRunState.ContainersRefilled = true;
            return true;
        }

        private bool TryCreateZombieModeLootboxLocalInventory(InteractableLootbox lootbox)
        {
            return InteractableLootboxInventoryHelper.EnsureLocalInventory(lootbox, 24);
        }

        private void ClearZombieModeLootboxInventory(InteractableLootbox lootbox)
        {
            if (lootbox == null || lootbox.Inventory == null)
            {
                return;
            }

            try
            {
                List<Item> content = lootbox.Inventory.Content;
                if (content == null)
                {
                    return;
                }

                for (int i = content.Count - 1; i >= 0; i--)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        content.RemoveAt(i);
                        continue;
                    }

                    try { item.Detach(); } catch (Exception ex) { DevLog("[ZombieMode] item.Detach 失败: " + ex.Message); }
                    try { item.DestroyTree(); } catch (Exception ex) { DevLog("[ZombieMode] item.DestroyTree 失败: " + ex.Message); try { Destroy(item.gameObject); } catch (Exception ex2) { DevLog("[ZombieMode] Destroy item.gameObject 失败: " + ex2.Message); } }
                }
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 清空容器库存失败: " + e.Message);
            }
        }

        private int RefillZombieModeLootboxInventory(
            int runId,
            InteractableLootbox lootbox,
            int minItems,
            int maxItems,
            int minQuality,
            int maxQuality,
            bool highValue)
        {
            if (!IsZombieModeRunValid(runId) || lootbox == null || lootbox.Inventory == null)
            {
                return 0;
            }

            int targetCount = UnityEngine.Random.Range(Mathf.Max(1, minItems), Mathf.Max(minItems + 1, maxItems + 1));
            int added = 0;
            for (int i = 0; i < targetCount; i++)
            {
                string[] tags = highValue
                    ? (UnityEngine.Random.value < 0.5f ? new string[] { "Weapon" } : new string[] { "Armor" })
                    : GetZombieModeContainerRefillTags(i);
                int typeId = FindRandomItemTypeByTags(tags, minQuality, maxQuality);
                if (typeId <= 0)
                {
                    typeId = FindRandomItemTypeByTags(null, minQuality, maxQuality);
                }

                if (typeId <= 0)
                {
                    continue;
                }

                Item item = null;
                try
                {
                    item = ItemAssetsCollection.InstantiateSync(typeId);
                    if (item == null)
                    {
                        continue;
                    }

                    bool addedToInventory = lootbox.Inventory.AddAndMerge(item, 0);
                    if (!addedToInventory)
                    {
                        addedToInventory = lootbox.Inventory.AddItem(item);
                    }

                    if (addedToInventory)
                    {
                        added++;
                        item = null;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] 重填容器物品失败: " + e.Message);
                }
                finally
                {
                    if (item != null)
                    {
                        try { item.DestroyTree(); } catch (Exception ex) { DevLog("[ZombieMode] item.DestroyTree 失败: " + ex.Message); try { Destroy(item.gameObject); } catch (Exception ex2) { DevLog("[ZombieMode] Destroy item.gameObject 失败: " + ex2.Message); } }
                    }
                }
            }

            try
            {
                lootbox.needInspect = true;
                lootbox.Inventory.NeedInspection = true;
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] lootbox needInspect 标记失败: " + e.Message);
            }

            return added;
        }

        private string[] GetZombieModeContainerRefillTags(int index)
        {
            int roll = UnityEngine.Random.Range(0, 100);
            if (roll < 30) return new string[] { "Ammo" };
            if (roll < 55) return new string[] { "Medical" };
            if (roll < 75) return new string[] { "Food" };
            if (roll < 90) return new string[] { "Weapon" };
            return new string[] { "Armor" };
        }

        private void LockZombieModeContainerUntilStarterChoice(int runId, InteractableLootbox lootbox)
        {
            if (!IsZombieModeRunValid(runId) || lootbox == null || lootbox.gameObject == null)
            {
                return;
            }

            ZombieModeContainerLock containerLock = lootbox.gameObject.GetComponent<ZombieModeContainerLock>();
            if (containerLock == null)
            {
                containerLock = lootbox.gameObject.AddComponent<ZombieModeContainerLock>();
            }

            containerLock.Initialize(runId, lootbox);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, null, containerLock, delegate
            {
                if (containerLock != null)
                {
                    containerLock.RestoreZombieModeContainerLock();
                    try { Destroy(containerLock); } catch (Exception e) { DevLog("[ZombieMode] Destroy containerLock 失败: " + e.Message); }
                }
            });
        }

        private void UnlockZombieModeContainersForActiveRun(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ZombieModeContainerLock[] locks = UnityEngine.Object.FindObjectsOfType<ZombieModeContainerLock>(true);
            for (int i = 0; i < locks.Length; i++)
            {
                ZombieModeContainerLock containerLock = locks[i];
                if (containerLock != null && containerLock.RunId == runId)
                {
                    containerLock.UnlockZombieModeContainer();
                }
            }
        }

        private void TrySpawnZombieModeEnemyDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            if (!IsZombieModeRunValid(runId) || marker == null)
            {
                return;
            }

            float chance = GetZombieModeEnemyDropChance(marker);
            if (UnityEngine.Random.value > chance)
            {
                return;
            }

            int minQuality = 1;
            int maxQuality = Mathf.Clamp(2 + zombieModeRunState.PollutionTier, 3, 7);
            string[] tags = GetZombieModeDropTags(marker);
            int typeId = FindRandomItemTypeByTags(tags, minQuality, maxQuality);
            if (typeId <= 0)
            {
                typeId = FindRandomItemTypeByTags(null, minQuality, maxQuality);
            }

            bool highValue = marker.EnemyKind == ZombieModeEnemyKind.Elite;
            TryDropZombieModeItemNearPosition(runId, typeId, position, highValue, false);
        }

        private float GetZombieModeEnemyDropChance(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null)
            {
                return 0f;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                return 0.60f;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                return 0.35f;
            }

            return 0.12f;
        }

        private string[] GetZombieModeDropTags(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker != null && marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                return UnityEngine.Random.value < 0.5f ? ZombieModeDropTagWeapon : ZombieModeDropTagArmor;
            }

            if (marker != null && marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                return UnityEngine.Random.value < 0.5f ? ZombieModeDropTagAmmo : ZombieModeDropTagMedical;
            }

            float roll = UnityEngine.Random.value;
            if (roll < 0.40f) return ZombieModeDropTagAmmo;
            if (roll < 0.75f) return ZombieModeDropTagMedical;
            return ZombieModeDropTagFood;
        }

        private void TrySpawnZombieModeBossDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            if (!IsZombieModeRunValid(runId) || marker == null)
            {
                return;
            }

            int maxQuality = Mathf.Clamp(4 + zombieModeRunState.PollutionTier + 1, 5, 8);
            GameObject drop = TrySpawnZombieModeBossLootbox(runId, marker.BossKind, position, maxQuality);
            if (drop != null)
            {
                ZombieModeBossDrop bossDrop = new ZombieModeBossDrop();
                bossDrop.GameObject = drop;
                bossDrop.WaveAtSpawn = zombieModeRunState.CurrentWave;
                bossDrop.BossKind = marker.BossKind;
                zombieModeRunState.BossDropEntries.Add(bossDrop);
            }
        }

        private GameObject TrySpawnZombieModeBossLootbox(int runId, ZombieModeBossKind bossKind, Vector3 position, int maxQuality)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return null;
            }

            try
            {
                InteractableLootbox prefab = GetDifficultyRewardLootBoxTemplate();
                if (prefab == null)
                {
                    return null;
                }

                InteractableLootbox lootbox = Instantiate(prefab, position + Vector3.up * 0.05f, Quaternion.identity);
                if (lootbox == null || lootbox.gameObject == null)
                {
                    return null;
                }

                lootbox.gameObject.name = "ZombieMode_BossLootbox_" + bossKind.ToString();
                TryCreateZombieModeLootboxLocalInventory(lootbox);
                ClearZombieModeLootboxInventory(lootbox);
                RefillZombieModeLootboxInventory(runId, lootbox, 6, 9, 3, maxQuality, true);
                try { MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex); } catch (Exception e) { DevLog("[ZombieMode] MoveToActiveWithScene 失败: " + e.Message); }
                try { ApplyLootBoxCoverSetting(lootbox, true); } catch (Exception e) { DevLog("[ZombieMode] ApplyLootBoxCoverSetting 失败: " + e.Message); }
                try { BossRushLootboxUtility.DecorateLootbox(lootbox, this, false, true); } catch (Exception e) { DevLog("[ZombieMode] DecorateLootbox 失败: " + e.Message); }
                TryAdjustZombieModeBossLootboxCapacity(lootbox);
                RegisterZombieModeDropCandidate(runId, lootbox.gameObject, true, true);
                return lootbox.gameObject;
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 生成 Boss 奖励箱失败: " + e.Message);
                return null;
            }
        }

        private void TryAdjustZombieModeBossLootboxCapacity(InteractableLootbox lootbox)
        {
            if (lootbox == null || lootbox.Inventory == null)
            {
                return;
            }

            try
            {
                int lastPos = lootbox.Inventory.GetLastItemPosition();
                lootbox.Inventory.SetCapacity(Mathf.Max(16, lastPos + 1));
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 调整 Boss 奖励箱容量失败: " + e.Message);
            }
        }

        private GameObject TryDropZombieModeItemNearPosition(int runId, int typeId, Vector3 position, bool highValue, bool bossDrop)
        {
            if (!IsZombieModeRunValid(runId) || typeId <= 0)
            {
                return null;
            }

            try
            {
                Item item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.gameObject == null)
                {
                    return null;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null && player.CharacterItem.Inventory != null)
                {
                    bool added = player.CharacterItem.Inventory.AddAndMerge(item, 0);
                    if (added)
                    {
                        string itemName = string.IsNullOrEmpty(item.DisplayName) ? "未知物品" : item.DisplayName;
                        int itemQuality = 1;
                        try { itemQuality = Mathf.Clamp(item.Quality, 1, 8); } catch { itemQuality = 1; }
                        TryShowZombieModeInventoryPickupPopText(player, itemName, itemQuality);
                        return item.gameObject;
                    }

                    item.Drop(player, true);
                    GameObject obj = item.gameObject;
                    RegisterZombieModeDropCandidate(runId, obj, highValue, bossDrop);
                    return obj;
                }

                Vector3 dropPosition = position + Vector3.up * 0.35f;
                Vector3 dropDirection = UnityEngine.Random.insideUnitSphere.normalized;
                item.Drop(dropPosition, true, dropDirection, bossDrop ? 30f : 18f);
                GameObject fallbackObject = item.gameObject;
                RegisterZombieModeDropCandidate(runId, fallbackObject, highValue, bossDrop);
                return fallbackObject;
            }
            catch
            {
                return null;
            }
        }

        private void TryShowZombieModeInventoryPickupPopText(CharacterMainControl player, string itemName, int itemQuality)
        {
            if (player == null || string.IsNullOrEmpty(itemName))
            {
                return;
            }

            string color = GetZombieModeDropQualityColorHex(itemQuality);
            string message = L10n.T(
                "搜到了<color=" + color + ">" + itemName + "</color>",
                "Found <color=" + color + ">" + itemName + "</color>");

            try
            {
                player.PopText(message, -1f);
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 玩家拾取气泡显示失败: " + e.Message);
            }
        }

        private string GetZombieModeDropQualityColorHex(int quality)
        {
            switch (Mathf.Clamp(quality, 1, 8))
            {
                case 1: return "#9E9E9E";
                case 2: return "#6B99CC";
                case 3: return "#4D73D9";
                case 4: return "#8C4DD9";
                case 5: return "#D94D99";
                case 6: return "#E64D4D";
                case 7: return "#FFB326";
                case 8: return "#FFD600";
                default: return "#9E9E9E";
            }
        }

        private void RegisterZombieModeDropCandidate(int runId, GameObject gameObject, bool highValue, bool bossDrop)
        {
            if (!IsZombieModeRunValid(runId) || gameObject == null)
            {
                return;
            }

            ZombieModeDropCandidate candidate = new ZombieModeDropCandidate();
            candidate.GameObject = gameObject;
            candidate.WaveAtSpawn = zombieModeRunState.CurrentWave;
            candidate.SpawnTime = GetZombieModeRuntimeNow();
            candidate.HighValue = highValue;
            candidate.BossDrop = bossDrop;
            zombieModeRunState.EntityDropCleanupCandidates.Add(candidate);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Unknown, gameObject, gameObject, null);
        }

        private void TickZombieModeDropsAndPerformance(float deltaTime)
        {
            if (!IsZombieModeActive || zombieModeRunState.RunId <= 0)
            {
                return;
            }

            CleanupZombieModeExpiredDropCandidates();
        }

        private void CleanupZombieModeExpiredDropCandidates()
        {
            if (zombieModeRunState.EntityDropCleanupCandidates.Count <= 0)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;
            float now = GetZombieModeRuntimeNow();
            for (int i = zombieModeRunState.EntityDropCleanupCandidates.Count - 1; i >= 0; i--)
            {
                ZombieModeDropCandidate candidate = zombieModeRunState.EntityDropCleanupCandidates[i];
                if (candidate == null || candidate.GameObject == null)
                {
                    zombieModeRunState.EntityDropCleanupCandidates.RemoveAt(i);
                    continue;
                }

                bool waveExpired = zombieModeRunState.CurrentWave - candidate.WaveAtSpawn >= ZombieModeTuning.DropCleanupWaveAge;
                bool timeExpired = now - candidate.SpawnTime >= ZombieModeTuning.DropCleanupAgeSeconds;
                if ((!waveExpired && !timeExpired) || candidate.HighValue || candidate.BossDrop)
                {
                    continue;
                }

                Vector3 delta = candidate.GameObject.transform.position - playerPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude < 25f)
                {
                    continue;
                }

                try { Destroy(candidate.GameObject); } catch (Exception e) { DevLog("[ZombieMode] Destroy 掉落候选 失败: " + e.Message); }
                zombieModeRunState.EntityDropCleanupCandidates.RemoveAt(i);
            }
        }

        private void RecycleZombieModeTemporaryNpcs(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            // 反向迭代清理走 RunScopedRegistry.ForEachReverse（审查 §1.3）。
            RunScopedRegistry.ForEachReverse(
                zombieModeRunState.TemporaryNpcs,
                npc =>
                {
                    if (npc != null && npc.GameObject != null)
                    {
                        Destroy(npc.GameObject);
                    }
                },
                (e, npc) => DevLog("[ZombieMode] Destroy temporary NPC 失败: " + e.Message));

            zombieModeRunState.TemporaryNpcs.Clear();
        }

        private void RecycleZombieModeTemporaryRealNpcs(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            RunScopedRegistry.ForEachReverse(
                zombieModeRunState.TemporaryRealNpcs,
                npc =>
                {
                    if (npc != null && npc.GameObject != null)
                    {
                        CloseZombieModeTemporaryRealNpcServices(npc);
                        Destroy(npc.GameObject);
                    }
                },
                (e, npc) => DevLog("[ZombieMode] Destroy temporary real NPC 失败: " + e.Message));

            zombieModeRunState.TemporaryRealNpcs.Clear();
        }

        private void CloseZombieModeTemporaryRealNpcServices(ZombieModeTemporaryRealNpcRecord npc)
        {
            if (npc == null)
            {
                return;
            }

            CloseZombieModeTemporaryRealNpcServices(npc.GameObject);
        }

        private void CloseZombieModeTemporaryRealNpcServices(GameObject npcObject)
        {
            if (npcObject == null)
            {
                return;
            }

            Transform npcTransform = npcObject.transform;
            try { NPCShopSystem.CloseShopIfOwnedBy(npcTransform); } catch (Exception e) { DevLog("[ZombieMode] Close temporary real NPC shop failed: " + e.Message); }
            try { ReforgeUIManager.CloseUIIfOwnedBy(npcTransform); } catch (Exception e) { DevLog("[ZombieMode] Close temporary real NPC reforge failed: " + e.Message); }
            try { CourierService.CloseServiceIfOwnedBy(npcTransform); } catch (Exception e) { DevLog("[ZombieMode] Close temporary real NPC courier failed: " + e.Message); }
            try { StorageDepositService.CloseServiceIfOwnedBy(npcTransform); } catch (Exception e) { DevLog("[ZombieMode] Close temporary real NPC storage failed: " + e.Message); }
            try { CourierPaidLootSweepService.CloseServiceIfOwnedBy(npcTransform); } catch (Exception e) { DevLog("[ZombieMode] Close temporary real NPC paid sweep failed: " + e.Message); }
        }

        private void RecycleZombieModeSafeZoneBoundTemporaryNpcs(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.TemporaryNpcs.Count <= 0)
            {
                return;
            }

            for (int i = zombieModeRunState.TemporaryNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc == null)
                {
                    zombieModeRunState.TemporaryNpcs.RemoveAt(i);
                    continue;
                }

                if (npc.ServiceState == null || !npc.ServiceState.SafeZoneBound)
                {
                    continue;
                }

                try
                {
                    if (npc.GameObject != null)
                    {
                        Destroy(npc.GameObject);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] Destroy safe-zone temporary NPC 失败: " + e.Message);
                }

                zombieModeRunState.TemporaryNpcs.RemoveAt(i);
            }
        }

        private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.TemporaryRealNpcs.Count <= 0)
            {
                return;
            }

            for (int i = zombieModeRunState.TemporaryRealNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryRealNpcRecord npc = zombieModeRunState.TemporaryRealNpcs[i];
                if (npc == null)
                {
                    zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
                    continue;
                }

                if (!npc.SafeZoneBound)
                {
                    continue;
                }

                try
                {
                    if (npc.GameObject != null)
                    {
                        CloseZombieModeTemporaryRealNpcServices(npc);
                        Destroy(npc.GameObject);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] Destroy safe-zone temporary real NPC 失败: " + e.Message);
                }

                zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
            }
        }
    }

    public sealed class ZombieModeContainerLock : MonoBehaviour
    {
        public int RunId;
        private InteractableLootbox lootbox;
        private Collider interactCollider;
        private bool originalLootboxEnabled;
        private bool originalColliderEnabled;
        private bool initialized;

        public void Initialize(int runId, InteractableLootbox target)
        {
            RunId = runId;
            lootbox = target;
            interactCollider = target != null ? target.GetComponent<Collider>() : null;
            originalLootboxEnabled = target == null || target.enabled;
            originalColliderEnabled = interactCollider == null || interactCollider.enabled;
            initialized = true;
            ApplyLockedState();
        }

        private void ApplyLockedState()
        {
            if (lootbox != null)
            {
                lootbox.enabled = false;
            }

            if (interactCollider != null)
            {
                interactCollider.enabled = false;
            }
        }

        public void UnlockZombieModeContainer()
        {
            RestoreZombieModeContainerLock();
            try { Destroy(this); } catch (Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy ContainerLock self 失败: " + e.Message); }
        }

        public void RestoreZombieModeContainerLock()
        {
            if (!initialized)
            {
                return;
            }

            if (lootbox != null)
            {
                lootbox.enabled = originalLootboxEnabled;
            }

            if (interactCollider != null)
            {
                interactCollider.enabled = originalColliderEnabled;
            }
        }
    }
}
