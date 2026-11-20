using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>持有自行克隆的物品 prefab，供按需注册与装配期共用。</summary>
    internal static class RuntimeItemFallback
    {
        private static readonly Dictionary<int, Item> OwnedClones = new Dictionary<int, Item>();
        private static readonly HashSet<int> InProgress = new HashSet<int>();

        private static readonly int[] SmallItemSourceIds =
        {
            BossRushItemIds.RelicEgg,
            BossRushItemIds.PortableSafeZoneDevice,
            BossRushItemIds.ZombieTideBeacon,
            BossRushItemIds.ZombieTideInvitation,
        };

        internal static bool Ensure(int typeId, string prefabName, string logPrefix,
            Func<Item> findSource, Action<Item> configureClone, Action<Item> configureExisting = null,
            Action<Item> afterPublish = null)
        {
            Item existing = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
            if (InProgress.Contains(typeId)) return existing != null;
            // 直接调用时也先走注册计划。计划回调时，才进入实际克隆路径。
            if (existing == null && !BossRushDynamicItemRegistry.IsRegistrationInProgress(typeId))
                return BossRushDynamicItemRegistry.EnsureRegistered(typeId);

            InProgress.Add(typeId);
            Item pendingClone = null;
            try
            {
                if (existing != null)
                {
                    ReleaseSupersededClone(typeId, existing);
                    if (configureExisting != null) configureExisting(existing);
                    return true;
                }

                // ItemFactory 的缓存只含已配置过的 bundle prefab，优先复用。
                Item loaded = ItemFactory.GetLoadedItem(typeId);
                if (loaded != null)
                {
                    if (!TryPublish(loaded)) return false;
                    ReleaseSupersededClone(typeId, loaded);
                    return true;
                }

                Item owned;
                if (OwnedClones.TryGetValue(typeId, out owned) && owned != null)
                    return TryPublish(owned);

                Item source = findSource();
                if (source == null)
                {
                    ModBehaviour.DevLog(logPrefix + "找不到兜底克隆源: " + typeId);
                    return false;
                }

                pendingClone = UnityEngine.Object.Instantiate(source);
                if (pendingClone == null || pendingClone.gameObject == null) return false;
                pendingClone.gameObject.name = prefabName;
                pendingClone.gameObject.SetActive(false);
                pendingClone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(pendingClone.gameObject);
                pendingClone.SetTypeID(typeId);
                configureClone(pendingClone);

                // 查找克隆源、配置器和 Unity 回调都可能让其他加载路径先发布。
                Item current = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                if (current != null && current != pendingClone) return true;
                if (current == null && !TryPublish(pendingClone)) return false;
                if (afterPublish != null) afterPublish(pendingClone);
                OwnedClones[typeId] = pendingClone;
                pendingClone = null;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + "兜底注册失败 " + typeId + ": " + e.Message);
                return false;
            }
            finally
            {
                ReleaseClone(pendingClone);
                InProgress.Remove(typeId);
            }
        }

        internal static Item FindSmallItemSource()
        {
            return FindSource(SmallItemSourceIds, false);
        }

        internal static Item FindSource(int[] typeIds, bool allowFactoryPrefab = true)
        {
            for (int i = 0; i < typeIds.Length; i++)
            {
                int typeId = typeIds[i];
                try
                {
                    Item prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                    if (prefab == null && BossRushDynamicItemRegistry.EnsureRegistered(typeId))
                        prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                    if (prefab != null) return prefab;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[RuntimeItemFallback] 查找克隆源失败 " + typeId + ": " + e.Message);
                }

                if (!allowFactoryPrefab) continue;
                Item loaded = ItemFactory.GetLoadedItem(typeId);
                if (loaded != null) return loaded;
            }
            return null;
        }

        private static bool TryPublish(Item prefab)
        {
            return BossRushDynamicItemRegistry.Publish(prefab)
                && BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(prefab.TypeID) == prefab;
        }

        private static void ReleaseSupersededClone(int typeId, Item replacement)
        {
            Item owned;
            if (!OwnedClones.TryGetValue(typeId, out owned) || owned == replacement) return;
            OwnedClones.Remove(typeId);
            ReleaseClone(owned);
        }

        private static void ReleaseClone(Item prefab)
        {
            if (prefab == null) return;
            try
            {
                // 官方只移除与该实例对应的条目，保留同 TypeID 的 bundle 替换件。
                ItemAssetsCollection.RemoveDynamicEntry(prefab);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RuntimeItemFallback] 注销克隆失败: " + e.Message);
            }
            try { UnityEngine.Object.Destroy(prefab.gameObject); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RuntimeItemFallback] 销毁克隆失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            foreach (Item prefab in OwnedClones.Values) ReleaseClone(prefab);
            OwnedClones.Clear();
            InProgress.Clear();
        }
    }
}
