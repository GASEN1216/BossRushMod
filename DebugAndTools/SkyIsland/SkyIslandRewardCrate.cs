using System;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛所有「箱子」的唯一建造点——搜刮点、Boss 战利品、委托奖励共用。
    ///
    /// 三条关键约束都收在这里，避免三处各写一遍再各漏一条：
    /// 1. 先在**未激活**的暂存父节点下实例化：官方 `LootBoxLoader.Awake` 会按位置哈希随机决定
    ///    `SetActive` 并写 `MultiSceneCore.inLevelData`；本图的箱子不走那条随机通道，
    ///    因此必须在 Awake 之前把它摘掉。
    /// 2. 必须建**独立本地 Inventory**：官方按位置哈希共享库存，靠得近的两个箱子会串味。
    /// 3. `InstantiateSync` 缺资源时返回同 TypeID 的空壳 FallbackItem，既不为 null 也不抛，
    ///    必须回读 `TypeID` 才能确认拿到的是真物品。
    /// </summary>
    internal static class SkyIslandRewardCrate
    {
        internal const int InventoryCapacity = 12;

        /// <summary>建一个空箱。失败返回 null 并给出原因，由调用方 fail-open。</summary>
        internal static InteractableLootbox Build(Transform parent, Vector3 position, float yaw,
            string name, out string error)
        {
            error = null;
            GameObject staging = null;
            InteractableLootbox box = null;
            try
            {
                if (parent == null) { error = "缺少场景根节点"; return null; }
                InteractableLootbox prefab = GameplayDataSettings.Prefabs != null
                    ? GameplayDataSettings.Prefabs.LootBoxPrefab : null;
                if (prefab == null) { error = "官方战利品箱预制体缺失"; return null; }
                staging = new GameObject("SkyIslandCrateStaging");
                staging.transform.SetParent(parent, false);
                staging.SetActive(false);
                box = UnityEngine.Object.Instantiate(prefab, staging.transform);
                LootBoxLoader loader = box.GetComponent<LootBoxLoader>();
                // 必须 DestroyImmediate：`Destroy` 帧末才真正移除组件，而下面 SetParent 到活动父节点
                // 会在**本帧**激活对象并触发 Awake，`LootBoxLoader.Awake` 照样会跑 RandomActive()，
                // 按位置哈希随机把箱子 SetActive(false)——箱子会静默消失，且不报错。
                // 对象此刻仍未激活且不是 prefab 资产，DestroyImmediate 在这里是安全且确定的。
                if (loader != null) UnityEngine.Object.DestroyImmediate(loader);
                box.name = name;
                // 先摆好世界坐标再挂到活动父节点：激活那一刻官方逻辑读到的必须是最终位置，
                // 而不是暂存节点所在的场景原点（官方按位置哈希取 key）。
                box.transform.position = position;
                box.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                box.transform.SetParent(parent, true);
                box.gameObject.SetActive(true);
                if (!InteractableLootboxInventoryHelper.EnsureLocalInventory(box, InventoryCapacity)
                    || box.Inventory == null)
                {
                    error = "箱子库存创建失败";
                    UnityEngine.Object.Destroy(box.gameObject);
                    box = null;
                    return null;
                }
                // 官方 `CreateLocalInventory()` 只做 AddComponent<Inventory>()，既不设容量也不设
                // NeedInspection（字段默认 false）。而 `GetOrCreateInventory` / `CreateFromItem`
                // 两条官方路径都会显式赋值，本 Mod 其余三条建箱路径（Boss 奖励箱、通关奖励、
                // 丧尸模式掉落）同样显式赋值。这里不补就会得到「开箱即全明牌、没有鉴定过程」的
                // 箱子，和全 Mod 其它箱子与原版口径都不一致，且 `Fill` 里的 Inspected=false 变成惰性标记。
                box.needInspect = true;
                box.Inventory.NeedInspection = true;
                // 容量同理：EnsureLocalInventory 的 fallbackCapacity 只在回退路径生效，
                // 成功路径拿到的是 Inventory 默认的 64 格。显式设一次，让常量真正说了算。
                box.Inventory.SetCapacity(InventoryCapacity);
                return box;
            }
            catch (Exception e)
            {
                error = e.Message;
                if (box != null) UnityEngine.Object.Destroy(box.gameObject);
                return null;
            }
            finally
            {
                if (staging != null) UnityEngine.Object.Destroy(staging);
            }
        }

        /// <summary>
        /// 按档次装填。返回实际装进去的件数；单件失败不影响其余件。
        /// `guaranteeTopBand` 只给「赚来的」奖励用（Boss 战利品、委托谢礼）：第 1 件从该档保底带里抽，
        /// 保底带为空时静默退回常规带。地上捡到的搜刮箱一律不开保底。
        /// </summary>
        internal static int Fill(InteractableLootbox box, SkyIslandLootTier tier, int raidSeed,
            string streamId, int count, bool guaranteeTopBand = false)
        {
            if (box == null || box.Inventory == null || count <= 0) return 0;
            int[] pool = SkyIslandLootPools.Get(tier);
            if (pool == null || pool.Length == 0) return 0;
            int[] guaranteed = guaranteeTopBand ? SkyIslandLootPools.GetGuaranteeBand(tier) : null;
            System.Random random = SkyIslandLootTables.CreateStream(raidSeed, streamId);
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                int[] source = (i == 0 && guaranteed != null && guaranteed.Length > 0) ? guaranteed : pool;
                int typeId = source[random.Next(source.Length)];
                Item item = null;
                try
                {
                    item = ItemAssetsCollection.InstantiateSync(typeId);
                    if (item == null || item.TypeID != typeId) throw new InvalidOperationException("物品实例无效");
                    item.Inspected = false;
                    if (!box.Inventory.AddItem(item)) throw new InvalidOperationException("装箱失败");
                    item = null;
                    added++;
                }
                catch (Exception e)
                {
                    if (item != null) { try { item.DestroyTree(); } catch { /* 单件清理失败不影响其余物品 */ } }
                    Debug.LogWarning("[SkyIslandCrate] 物品 " + typeId + " 装箱失败：" + e.Message);
                }
            }
            return added;
        }

        /// <summary>一步建箱并装填。返回是否成功建出箱体（装填件数为 0 仍算建成，玩家会看到空箱）。</summary>
        internal static bool Create(Transform parent, Vector3 position, SkyIslandLootTier tier,
            string name, string streamId, int raidSeed, int count, bool guaranteeTopBand = false)
        {
            string error;
            InteractableLootbox box = Build(parent, position, 0f, name, out error);
            if (box == null)
            {
                Debug.LogWarning("[SkyIslandCrate] " + name + " 创建失败：" + error);
                return false;
            }
            int added = Fill(box, tier, raidSeed, streamId, count, guaranteeTopBand);
            Debug.Log("[SkyIslandCrate] CRATE_READY name=" + name + " tier=" + tier + " items=" + added);
            return true;
        }
    }
}
