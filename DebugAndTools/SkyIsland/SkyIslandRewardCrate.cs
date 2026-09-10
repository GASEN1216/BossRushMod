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
    /// 3. `InstantiateSync` 缺资源时返回空壳 FallbackItem（官方 `InstantiateFallbackItem` 把**同一个 TypeID**
    ///    写回去），既不为 null 也不抛——回读 `TypeID` 分辨不出它。必须在实例化**之前**用
    ///    `ItemAssetsCollection.GetPrefab` 确认 prefab 在（它同时覆盖官方静态条目与 Mod 动态条目）。
    /// </summary>
    internal static class SkyIslandRewardCrate
    {
        internal const int InventoryCapacity = 12;

        /// <summary>落点退避的尝试次数与角度步进：绕锚点转一整圈。</summary>
        private const int PlacementAttempts = 6;
        private const float PlacementBearingStep = 60f;

        /// <summary>
        /// 任何「摆在已有玩法标记旁边」的对象与该标记之间的最小间距。
        ///
        /// 官方 `CA_Interact.SearchInteractableAround` 按**玩家到 collider 的距离严格小于**取
        /// 唯一交互目标，同点的两个交互体距离完全相等，谁赢由 `Physics.OverlapSphereNonAlloc`
        /// 的返回顺序决定，而且不同组之间滚轮切不过去。3.2 米足以让两边的触发盒分开
        /// （纪念物半边 1.5 + 见闻点半边 1.1 = 2.6），玩家站在谁跟前就选中谁。
        ///
        /// 这个常量与 <see cref="TryFindCratePosition"/> 一起，被谢礼箱、完成纪念物和航路图三处共用。
        /// </summary>
        internal const float InteractableSeparation = 3.2f;

        /// <summary>
        /// 给箱子找一个「站得住、又不挡别人交互」的落点。
        ///
        /// 裁决沿用搜刮点 <c>SkyIslandScavenging.TryResolve</c> 的同一套口径——那是全岛唯一被
        /// 真实几何回归（`tests/SkyIslandContentPlacementPropertyTest.py`）复算过的落点算法：
        /// 6 次 60° 极坐标退避 → 上方 3 m 向下 7 m 打地面层 → 命中必须属于本图地形 → 胶囊墙检。
        ///
        /// 刻意**不**带「与已放置点净空」那一段：它依赖搜刮点自己的点表。委托谢礼靠调用方
        /// 给每轮不同的方位角来错开，不需要全局点表。
        ///
        /// 失败返回 false，调用方 fail-open 退回锚点本身（宁可压在锚点上，也不能不给奖励）。
        /// </summary>
        internal static bool TryFindCratePosition(Transform root, Vector3 anchor, float bearing,
            float distance, int groundMask, out Vector3 result)
        {
            result = anchor;
            if (root == null || distance <= 0f) return false;
            try
            {
                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    float angle = (bearing + attempt * PlacementBearingStep) * Mathf.Deg2Rad;
                    Vector3 candidate = anchor +
                        new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                    RaycastHit hit;
                    if (!Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out hit, 7f, groundMask,
                        QueryTriggerInteraction.Ignore) || !hit.transform.IsChildOf(root)) continue;
                    Vector3 ground = hit.point + Vector3.up * 0.05f;
                    if (Physics.CheckCapsule(ground + Vector3.up * 0.4f, ground + Vector3.up * 1.2f, 0.45f,
                        GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) continue;
                    result = ground;
                    return true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandCrate] 落点探测失败：" + e.Message); }
            return false;
        }

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
            // 岛上特产（SkyIslandItemRules.IslandExtraFor）：每箱至多多装一件，走独立随机流——原有 count 件的抽样结果一件不变。
            int extra = SkyIslandItemRules.IslandExtraFor(tier,
                SkyIslandLootTables.CreateStream(raidSeed, streamId + "#island").NextDouble());
            int total = extra != 0 ? count + 1 : count;
            int added = 0;
            for (int i = 0; i < total; i++)
            {
                int[] source = (i == 0 && guaranteed != null && guaranteed.Length > 0) ? guaranteed : pool;
                int typeId = i < count ? source[random.Next(source.Length)] : extra;
                Item item = null;
                try
                {
                    // 先问 prefab：缺资源时 InstantiateSync 给的空壳带着同一个 TypeID，下面那道回读拦不住它。
                    if (ItemAssetsCollection.GetPrefab(typeId) == null) throw new InvalidOperationException("物品资源缺失");
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

        /// <summary>
        /// 一步建箱并装填。返回是否建出了**至少装进一件**的箱子。
        ///
        /// 一件都没装进去的箱子直接收掉并返回 false：委托谢礼「先送达再消费」全靠这个返回值，
        /// 旧写法把空箱也算建成，物资池暂时查不出东西时玩家交了单、拿到的是个空箱，委托却已经被消耗。
        /// </summary>
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
            if (added == 0)
            {
                box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(box.gameObject);
                Debug.LogWarning("[SkyIslandCrate] " + name + " 一件物品都没装进去，已收回空箱 tier=" + tier);
                return false;
            }
            Debug.Log("[SkyIslandCrate] CRATE_READY name=" + name + " tier=" + tier + " items=" + added);
            return true;
        }
    }
}
