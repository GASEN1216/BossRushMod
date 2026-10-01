using System;
using System.Reflection;
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
        /// 失败返回 false，由调用方保留待领取资格或跳过可选装饰；不要把未经检测的锚点当成安全落点。
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

        /// <summary>搜刮点箱子的交互名（中英由 <see cref="InjectLocalizations"/> 注入，取用时按当前语言解析）。</summary>
        internal const string SearchNameKey = "BossRush_SkyIsland_LootSearch";

        // 官方 InteractableLootbox.displayNameKey 是私有序列化字段：Start 里 `InteractName = displayNameKey` 会盖掉任何事先写的
        // 交互名，Inventory 取值时也把它抄成库存标题。只能在 Start 之前写这个字段（口径同 NPCGiftContainerService / CourierService）。
        private static readonly FieldInfo LootboxDisplayNameKeyField =
            typeof(InteractableLootbox).GetField("displayNameKey", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool displayNameFieldWarned;

        /// <summary>语言注入链（`InjectLocalization_Extra_Integration`）每次切语言都会调一次。</summary>
        internal static void InjectLocalizations()
        {
            LocalizationHelper.InjectLocalization(SearchNameKey, L10n.T("搜集", "Search"));
        }

        /// <summary>
        /// 建一个空箱。失败返回 null 并给出原因，由调用方 fail-open。
        /// <paramref name="searchCache"/> 只给地上的搜刮点（<see cref="SkyIslandScavenging"/>）：交互名改成「搜集」、去掉官方尸体箱
        /// 预制体自带的「搬起」（<see cref="PrepareSearchCache"/>）。Boss 战利品、委托谢礼、回响遗存这些「赚来的」箱子保持官方原样。
        /// </summary>
        internal static InteractableLootbox Build(Transform parent, Vector3 position, float yaw,
            string name, out string error, bool searchCache = false)
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
                // 同样必须在激活之前：激活那一刻官方 InteractableBase.Awake 会把「搬起」的触发盒挪到交互层并编进交互组。
                if (searchCache) PrepareSearchCache(box);
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
                // 组里只剩箱子自己时把交互组关掉：交互提示只有「搜集」一行，滚轮也无可切换。
                if (searchCache && box.GetInteractableList().Count <= 1) box.interactableGroup = false;
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
            System.Random random = SkyIslandLootTables.CreateStream(raidSeed, streamId);
            // 岛上特产（SkyIslandItemRules.IslandExtraFor）：每箱至多多装一件，走独立随机流——原有 count 件的抽样结果一件不变。
            // 悬根猎装任两件（SkyIslandBossGearWorn，头目 R2）：同一个抽样数压一半，特产机会翻倍、不多抽随机数。
            int extra = SkyIslandItemRules.IslandExtraFor(tier, SkyIslandBossRules.HunterIslandExtraRoll(
                SkyIslandLootTables.CreateStream(raidSeed, streamId + "#island").NextDouble(), SkyIslandBossGearWorn.RootweavePieces));
            int total = extra != 0 ? count + 1 : count;
            int added = 0;
            for (int i = 0; i < total; i++)
            {
                // 抽样按品质加权（CR-2026-09-12-019）：旧写法在 TypeID 清单上均匀抽，
                // 于是一档被抽中的概率正比于「这一档有多少种物品」——而星工带里高半段的种类比低半段还多。
                // `Pick` 内部处理保底带与「保底带为空退回常规带」的降级，口径与旧写法一致。
                int typeId = i < count
                    ? SkyIslandLootPools.Pick(tier, i == 0 && guaranteeTopBand, random)
                    : extra;
                if (typeId == 0) continue;
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
                    if (item != null && item.InInventory == box.Inventory) { added++; continue; }
                    if (item != null && item.InInventory == null && item.PluggedIntoSlot == null)
                    { try { item.DestroyTree(); } catch { /* 只清理未交付的物品 */ } }
                    Debug.LogWarning("[SkyIslandCrate] 物品 " + typeId + " 装箱失败：" + e.Message);
                }
            }
            return added;
        }

        /// <summary>
        /// 搜刮点箱子（只在未激活暂存阶段调用）：
        /// 1. 去掉「搬起」。官方 `LootBoxPrefab` 是敌人尸体箱，子物体 `Carriable` 上挂着 <see cref="InteractableCarriable"/>（交互名「搬起」）、
        ///    <see cref="Carriable"/> 与它自己的触发盒，并登记在箱子的交互组列表里。这里把这三样 `DestroyImmediate` 掉、子物体本身留着
        ///    （不赌它下面没挂渲染件）。组列表是官方私有字段，里面那一项变成已销毁引用：官方读这张表的三处——
        ///    `InteractableBase.Awake`（`if (interactableBase)`）、`GetInteractableList`（`== null` 与 `activeInHierarchy`）、
        ///    `InteractHUD.RefreshContent`（`!= null`）——都先判 Unity 空引用再用，不会碰到它；激活后再按
        ///    `GetInteractableList().Count` 把只剩自己的交互组关掉（<see cref="Build"/>）。
        ///    触发盒一起删：留着它会在交互层上占 `CA_Interact` 那 5 个重叠槽位之一，`GetComponent&lt;InteractableBase&gt;` 为空白白被跳过。
        /// 2. 交互名换成「搜集」：写私有 `displayNameKey`，官方 Start 读它当交互名，搜刮界面标题也随之显示「搜集」。
        /// 反射字段缺失时保留官方名字（「战利品」）并警告一次，箱子照样能搜。
        /// </summary>
        private static void PrepareSearchCache(InteractableLootbox box)
        {
            foreach (InteractableCarriable carry in box.GetComponentsInChildren<InteractableCarriable>(true))
            {
                if (carry == null) continue;
                Collider trigger = carry.interactCollider;
                if (trigger != null && trigger.gameObject == carry.gameObject && carry.gameObject != box.gameObject)
                    UnityEngine.Object.DestroyImmediate(trigger);
                UnityEngine.Object.DestroyImmediate(carry);
            }
            foreach (Carriable carriable in box.GetComponentsInChildren<Carriable>(true))
                if (carriable != null) UnityEngine.Object.DestroyImmediate(carriable);
            if (LootboxDisplayNameKeyField != null && LootboxDisplayNameKeyField.FieldType == typeof(string))
            {
                InjectLocalizations();
                LootboxDisplayNameKeyField.SetValue(box, SearchNameKey);
            }
            else if (!displayNameFieldWarned)
            {
                displayNameFieldWarned = true;
                Debug.LogWarning("[SkyIslandCrate] 官方 InteractableLootbox.displayNameKey 字段签名已变化，搜刮箱保留官方交互名");
            }
        }

        /// <summary>
        /// 地上搜刮箱的装填（owner 2026-10-01）。返回实际装进去的格数（一堆子弹、一堆材料都算一格）。
        ///
        /// - 总格数 <see cref="SkyIslandLootTables.RollCount"/>：按档次与岛区富裕程度在 3–7 格里取。
        /// - 第 1 格保底一堆岛上特产（<see cref="SkyIslandItemRules.IslandStapleFor"/>）。
        /// - 额外特产（<see cref="SkyIslandItemRules.IslandExtraFor"/>，悬根猎装任两件机会翻倍）照旧追加一格，
        ///   只有总格数顶到 7 时才顶掉一格官方物品（<see cref="SkyIslandLootTables.OfficialSlots"/>）。
        /// - 其余格按本档类别牌（<see cref="SkyIslandLootTables.DealCategories"/>）逐格从武器 / 子弹 / 装备 / 物资 / 通用池按品质加权抽；
        ///   类别池为空退回通用池；子弹一格一堆（<see cref="SkyIslandLootTables.RollAmmoStack"/>）。地上的箱子一律不开保底品质带。
        /// - 各段走各自的随机流（raidSeed + 点 id + 后缀），同一趟同一点内容固定。
        /// <paramref name="summary"/> 给日志：每格「类别:TypeID×堆数」。
        /// </summary>
        internal static int FillScavenge(InteractableLootbox box, SkyIslandLootAnchor anchor, int raidSeed, out string summary)
        {
            summary = string.Empty;
            if (box == null || box.Inventory == null || anchor == null) return 0;
            SkyIslandLootTier tier = anchor.Tier;
            int slots = SkyIslandLootTables.RollCount(tier, anchor.Region,
                SkyIslandLootTables.CreateStream(raidSeed, "count:" + anchor.Id));
            var log = new System.Text.StringBuilder(96);
            int added = 0;
            System.Random stapleStream = SkyIslandLootTables.CreateStream(raidSeed, anchor.Id + "#staple");
            int stapleCount;
            int staple = SkyIslandItemRules.IslandStapleFor(tier, stapleStream.NextDouble(), stapleStream.NextDouble(), out stapleCount);
            if (AddStack(box, staple, stapleCount, log, "Island")) added++;
            int extra = SkyIslandItemRules.IslandExtraFor(tier, SkyIslandBossRules.HunterIslandExtraRoll(
                SkyIslandLootTables.CreateStream(raidSeed, anchor.Id + "#island").NextDouble(), SkyIslandBossGearWorn.RootweavePieces));
            if (extra != 0 && AddStack(box, extra, 1, log, "Island")) added++;
            System.Random random = SkyIslandLootTables.CreateStream(raidSeed, anchor.Id);
            SkyIslandLootCategory[] categories = SkyIslandLootTables.DealCategories(tier,
                SkyIslandLootTables.OfficialSlots(slots, extra != 0), random);
            for (int i = 0; i < categories.Length; i++)
            {
                SkyIslandLootCategory category = categories[i];
                int typeId = SkyIslandLootPools.PickCategory(category, tier, random);
                if (typeId == 0 && category != SkyIslandLootCategory.General)
                {
                    category = SkyIslandLootCategory.General;
                    typeId = SkyIslandLootPools.Pick(tier, false, random);
                }
                if (typeId == 0) continue;
                int amount = category == SkyIslandLootCategory.Ammo ? SkyIslandLootTables.RollAmmoStack(tier, random) : 1;
                if (AddStack(box, typeId, amount, log, category.ToString())) added++;
            }
            summary = "slots=" + slots + " " + log.ToString().TrimEnd();
            return added;
        }

        /// <summary>
        /// 往箱子里放**一格**：可堆叠的按 <paramref name="amount"/> 堆（被官方堆叠上限截住），不可堆叠的就一件。
        /// 先问 prefab、实例化后回读 TypeID，与 <see cref="Fill"/> 同一条防线；失败只跳过这一格。
        /// </summary>
        private static bool AddStack(InteractableLootbox box, int typeId, int amount, System.Text.StringBuilder log, string label)
        {
            if (typeId <= 0) return false;
            Item item = null;
            try
            {
                // 先问 prefab：缺资源时 InstantiateSync 给的空壳带着同一个 TypeID，回读拦不住它。
                if (ItemAssetsCollection.GetPrefab(typeId) == null) throw new InvalidOperationException("物品资源缺失");
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) throw new InvalidOperationException("物品实例无效");
                int stack = 1;
                if (item.Stackable && amount > 1)
                {
                    stack = Mathf.Clamp(amount, 1, Mathf.Max(1, item.MaxStackCount));
                    item.StackCount = stack;
                }
                item.Inspected = false;
                if (!box.Inventory.AddItem(item)) throw new InvalidOperationException("装箱失败");
                item = null;
                log.Append(label).Append(':').Append(typeId).Append('x').Append(stack).Append(' ');
                return true;
            }
            catch (Exception e)
            {
                if (item != null && item.InInventory == box.Inventory) return true;
                if (item != null && item.InInventory == null && item.PluggedIntoSlot == null)
                { try { item.DestroyTree(); } catch { /* 只清理未交付的物品 */ } }
                Debug.LogWarning("[SkyIslandCrate] 物品 " + typeId + " 装箱失败：" + e.Message);
                return false;
            }
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

        /// <summary>
        /// 一步建一个只装岛上东西的箱子（噬风·回响的回响遗存，<see cref="SkyIslandStormEchoReward"/>）：不抽官方物资池，
        /// 物品全是登记过的天空岛物品。实例化前先问 prefab、回读 TypeID，与 <see cref="Fill"/> 同一条防线；
        /// 一件都没装进去就收回空箱并返回 false（口径同 <see cref="Create"/>）。
        /// </summary>
        internal static bool CreateWithGoods(Transform parent, Vector3 position, string name, SkyIslandYield[] goods)
        {
            if (goods == null || goods.Length == 0) return false;
            string error;
            InteractableLootbox box = Build(parent, position, 0f, name, out error);
            if (box == null)
            {
                Debug.LogWarning("[SkyIslandCrate] " + name + " 创建失败：" + error);
                return false;
            }
            int added = 0;
            for (int i = 0; i < goods.Length; i++) added += AddGoods(box, goods[i].TypeId, goods[i].Count);
            if (added == 0)
            {
                box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(box.gameObject);
                Debug.LogWarning("[SkyIslandCrate] " + name + " 一件东西都没装进去，已收回空箱");
                return false;
            }
            Debug.Log("[SkyIslandCrate] CRATE_READY name=" + name + " goods=" + added);
            return true;
        }

        /// <summary>把 <paramref name="count"/> 件同一种岛上物品装进箱子（可堆叠的按堆装）。返回实际装进去的件数。</summary>
        private static int AddGoods(InteractableLootbox box, int typeId, int count)
        {
            if (box == null || box.Inventory == null || count <= 0) return 0;
            int added = 0;
            while (added < count)
            {
                Item item = null;
                int stack = 1;
                try
                {
                    // 先问 prefab：缺资源时 InstantiateSync 给的空壳带着同一个 TypeID，回读拦不住它。
                    if (ItemAssetsCollection.GetPrefab(typeId) == null) throw new InvalidOperationException("物品资源缺失");
                    item = ItemAssetsCollection.InstantiateSync(typeId);
                    if (item == null || item.TypeID != typeId) throw new InvalidOperationException("物品实例无效");
                    if (item.Stackable)
                    {
                        stack = Mathf.Clamp(count - added, 1, Mathf.Max(1, item.MaxStackCount));
                        item.StackCount = stack;
                    }
                    item.Inspected = false;
                    if (!box.Inventory.AddItem(item)) throw new InvalidOperationException("装箱失败");
                    item = null;
                    added += stack;
                }
                catch (Exception e)
                {
                    if (item != null && item.InInventory == box.Inventory) { added += stack; continue; }
                    if (item != null && item.InInventory == null && item.PluggedIntoSlot == null)
                    { try { item.DestroyTree(); } catch { /* 只清理未交付的物品 */ } }
                    Debug.LogWarning("[SkyIslandCrate] 物品 " + typeId + " 装箱失败：" + e.Message);
                    break;
                }
            }
            return added;
        }
    }
}
