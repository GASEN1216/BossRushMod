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
                    marker.RecycledForPerformance)
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
            if (lootbox == null)
            {
                return false;
            }

            try
            {
                MethodInfo createLocalInventoryMethod = BossRush.Common.Utils.ReflectionCache.GetMethod(
                    typeof(InteractableLootbox),
                    "CreateLocalInventory",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (createLocalInventoryMethod != null)
                {
                    createLocalInventoryMethod.Invoke(lootbox, null);
                }
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 创建容器本地库存失败: " + e.Message);
            }

            if (lootbox.Inventory != null)
            {
                return true;
            }

            try
            {
                Inventory inventory = lootbox.gameObject.GetComponent<Inventory>();
                if (inventory == null)
                {
                    inventory = lootbox.gameObject.AddComponent<Inventory>();
                    inventory.SetCapacity(24);
                }

                FieldInfo inventoryReferenceField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(InteractableLootbox),
                    "inventoryReference",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (inventoryReferenceField != null)
                {
                    inventoryReferenceField.SetValue(lootbox, inventory);
                }
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 绑定容器本地库存失败: " + e.Message);
            }

            return lootbox.Inventory != null;
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
                return UnityEngine.Random.value < 0.5f ? new string[] { "Weapon" } : new string[] { "Armor" };
            }

            if (marker != null && marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                return UnityEngine.Random.value < 0.5f ? new string[] { "Ammo" } : new string[] { "Medical" };
            }

            float roll = UnityEngine.Random.value;
            if (roll < 0.40f) return new string[] { "Ammo" };
            if (roll < 0.75f) return new string[] { "Medical" };
            return new string[] { "Food" };
        }

        private void TrySpawnZombieModeBossDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            if (!IsZombieModeRunValid(runId) || marker == null)
            {
                return;
            }

            int count = ZombieModeTuning.BossLootCrateBaseAtWave5 +
                        Mathf.Max(0, zombieModeRunState.CurrentWave / 5 - 1) * ZombieModeTuning.BossLootCrateGrowthEvery5Waves;
            int maxQuality = Mathf.Clamp(4 + zombieModeRunState.PollutionTier + 1, 5, 8);
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 360f * i / Mathf.Max(1, count), 0f) * Vector3.forward * 1.8f;
                GameObject drop = TrySpawnZombieModeBossLootbox(runId, marker.BossKind, position + offset, maxQuality);
                if (drop != null)
                {
                    ZombieModeBossDrop bossDrop = new ZombieModeBossDrop();
                    bossDrop.GameObject = drop;
                    bossDrop.WaveAtSpawn = zombieModeRunState.CurrentWave;
                    bossDrop.BossKind = marker.BossKind;
                    zombieModeRunState.BossDropEntries.Add(bossDrop);
                }
            }

            for (int i = 0; i < 2; i++)
            {
                int typeId = FindRandomItemTypeByTags(null, 3, maxQuality);
                Vector3 offset = UnityEngine.Random.insideUnitSphere;
                offset.y = 0f;
                TryDropZombieModeItemNearPosition(runId, typeId, position + offset.normalized * 2.2f, true, true);
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
                RefillZombieModeLootboxInventory(runId, lootbox, 1, 2, 3, maxQuality, true);
                try { MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex); } catch (Exception e) { DevLog("[ZombieMode] MoveToActiveWithScene 失败: " + e.Message); }
                try { ApplyLootBoxCoverSetting(lootbox, true); } catch (Exception e) { DevLog("[ZombieMode] ApplyLootBoxCoverSetting 失败: " + e.Message); }
                try { BossRushLootboxUtility.DecorateLootbox(lootbox, this, false); } catch (Exception e) { DevLog("[ZombieMode] DecorateLootbox 失败: " + e.Message); }
                RegisterZombieModeDropCandidate(runId, lootbox.gameObject, true, true);
                return lootbox.gameObject;
            }
            catch (Exception e)
            {
                DevLog("[ZombieMode] 生成 Boss 奖励箱失败: " + e.Message);
                return null;
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

                item.Detach();
                GameObject obj = item.gameObject;
                obj.transform.position = position + Vector3.up * 0.35f;
                obj.SetActive(true);
                RegisterZombieModeDropCandidate(runId, obj, highValue, bossDrop);
                return obj;
            }
            catch
            {
                return null;
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
            candidate.SpawnTime = Time.unscaledTime;
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
            if (Time.unscaledTime - zombieModeRunState.LastPerformanceEvalTime < ZombieModeTuning.PerformanceEvalIntervalSeconds)
            {
                return;
            }

            zombieModeRunState.LastPerformanceEvalTime = Time.unscaledTime;
            EvaluateZombieModePerformanceTier();
            if (zombieModeRunState.PerformanceTier == ZombieModePerformanceTier.ExtremeProtect)
            {
                RecycleZombieModeFarEnemiesForPerformance(zombieModeRunState.RunId);
            }
        }

        private void EvaluateZombieModePerformanceTier()
        {
            int count = zombieModeRunState.LivingZombieCount;
            ZombieModePerformanceTier current = zombieModeRunState.PerformanceTier;
            ZombieModePerformanceTier next;
            if (count >= ZombieModeTuning.PerfTierExtreme ||
                (current == ZombieModePerformanceTier.ExtremeProtect &&
                 count > ZombieModeTuning.PerfTierExtreme - ZombieModeTuning.PerfTierHysteresis - 1))
            {
                next = ZombieModePerformanceTier.ExtremeProtect;
            }
            else if (count >= ZombieModeTuning.PerfTierSoft ||
                     (current == ZombieModePerformanceTier.SoftProtect &&
                      count > ZombieModeTuning.PerfTierSoft - ZombieModeTuning.PerfTierHysteresis - 1))
            {
                next = ZombieModePerformanceTier.SoftProtect;
            }
            else if (count >= ZombieModeTuning.PerfTierWatch ||
                     (current == ZombieModePerformanceTier.Watch &&
                      count > ZombieModeTuning.PerfTierWatch - ZombieModeTuning.PerfTierHysteresis - 1))
            {
                next = ZombieModePerformanceTier.Watch;
            }
            else
            {
                next = ZombieModePerformanceTier.Normal;
            }

            if (next != current)
            {
                zombieModeRunState.PerformanceTier = next;
                if (DevModeEnabled && next >= ZombieModePerformanceTier.SoftProtect)
                {
                    ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_PerformanceProtect"));
                }
            }
        }

        private void RecycleZombieModeFarEnemiesForPerformance(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            int recycled = 0;
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count && recycled < ZombieModeTuning.MaxRecyclePerEval; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (!CanRecycleZombieModeEnemyForPerformance(marker, player))
                {
                    continue;
                }

                if (RecycleZombieModeEnemyForPerformance(marker))
                {
                    recycled++;
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
        }

        private bool CanRecycleZombieModeEnemyForPerformance(ZombieModeEnemyRuntimeMarker marker, CharacterMainControl player)
        {
            if (marker == null || player == null || marker.RunId != zombieModeRunState.RunId || marker.IsBoss || marker.DeathSettled || marker.RecycledForPerformance)
            {
                return false;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite &&
                zombieModeRunState.LivingZombieCount < 420)
            {
                return false;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Special &&
                zombieModeRunState.LivingZombieCount < 400)
            {
                return false;
            }

            Vector3 delta = marker.transform.position - player.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < ZombieModeTuning.PerformanceFarDistance * ZombieModeTuning.PerformanceFarDistance)
            {
                return false;
            }

            Vector3 direction = delta.normalized;
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f && Vector3.Dot(forward.normalized, direction) > 0f)
            {
                return false;
            }

            AICharacterController ai = marker.GetComponentInChildren<AICharacterController>();
            if (ai != null)
            {
                try
                {
                    if (player.mainDamageReceiver != null &&
                        ai.searchedEnemy == player.mainDamageReceiver &&
                        (ai.isNoticing(0.5f) || Time.time - ai.hurtTimeMarker < 1.5f)) // scaled-ok: ai.hurtTimeMarker 在 scaled 时间域
                    {
                        return false;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] AI hurt 状态读取失败: " + e.Message);
                }
            }

            return true;
        }

        private bool RecycleZombieModeEnemyForPerformance(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.IsBoss)
            {
                return false;
            }

            try
            {
                marker.RecycledForPerformance = true;
                zombieModeRunState.LivingZombieCount = Mathf.Max(0, zombieModeRunState.LivingZombieCount - 1);
                Destroy(marker.gameObject);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CleanupZombieModeExpiredDropCandidates()
        {
            if (zombieModeRunState.EntityDropCleanupCandidates.Count <= 0)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;
            for (int i = zombieModeRunState.EntityDropCleanupCandidates.Count - 1; i >= 0; i--)
            {
                ZombieModeDropCandidate candidate = zombieModeRunState.EntityDropCleanupCandidates[i];
                if (candidate == null || candidate.GameObject == null)
                {
                    zombieModeRunState.EntityDropCleanupCandidates.RemoveAt(i);
                    continue;
                }

                bool waveExpired = zombieModeRunState.CurrentWave - candidate.WaveAtSpawn >= ZombieModeTuning.DropCleanupWaveAge;
                bool timeExpired = Time.unscaledTime - candidate.SpawnTime >= ZombieModeTuning.DropCleanupAgeSeconds;
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

            for (int i = zombieModeRunState.TemporaryNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc != null && npc.GameObject != null)
                {
                    try { Destroy(npc.GameObject); } catch (Exception e) { DevLog("[ZombieMode] Destroy temporary NPC 失败: " + e.Message); }
                }
            }

            zombieModeRunState.TemporaryNpcs.Clear();
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
            try { Destroy(this); } catch (Exception e) { DevLog("[ZombieMode] Destroy ContainerLock self 失败: " + e.Message); }
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
