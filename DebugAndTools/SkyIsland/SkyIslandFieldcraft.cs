using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using SodaCraft;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛内容批次三的局内 owner——采集点、合成台、局内耗材、夜风与营火。
    ///
    /// 生命周期挂在 <see cref="SkyIslandWorldStory"/> 上（会话就绪后的第一次推进时创建、随它销毁），
    /// 不往已贴着 1200 行预算的 `SkyIslandSession.cs` 里加接线。纯规则全部在 <see cref="SkyIslandFieldcraftRules"/>。
    ///
    /// 三条纪律：
    /// 1. **不进存档、离岛即清**：本趟的增益 Modifier、风灯的灯、营火与采集点都由本类持有，Dispose 一并摘除。
    /// 2. **玩法计时走游戏时间**（`Time.time`）：暂停菜单、拍照模式与剧情面板把 timeScale 压到 0 时，
    ///    耗材不会在背后燃尽，寒意也不会在背后积满。
    /// 3. **出击里得到的东西承担出击风险**：产出与成品放进背包、放不下落在脚边，绝不寄回基地仓库。
    /// </summary>
    internal sealed class SkyIslandFieldcraft : IDisposable
    {
        /// <summary>
        /// 本趟的 owner。耗材的 `CanBeUsed` / `OnUse` 只在玩家按下使用时读一次（不在每帧路径上），
        /// 离岛时为 null，官方物品界面据此把「使用」按钮置灰，不会白吃掉一件耗材。
        /// </summary>
        internal static SkyIslandFieldcraft Current { get; private set; }

        private const string ModifierContext = "SkyIslandFieldcraft";
        /// <summary>三处营火：渡口工台（码头装置）、晴禾的灶台（菜畦）、眠苔的药臼（她的站位）。只是光，没有碰撞体，不参与交互竞争。</summary>
        private static readonly string[] CampfireMarkers = { "Search_A", "Search_C", "POI_D" };
        private static readonly Color CampfireColor = new Color(1f, 0.64f, 0.32f);
        private static readonly Color LanternColor = new Color(1f, 0.8f, 0.52f);

        private readonly SkyIslandSession session;
        private readonly SkyIslandStoryService story;
        private readonly Transform root;
        private readonly int groundMask;
        private readonly int seed;
        private readonly SkyIslandGathering gathering;
        private readonly object modifierSource = new object();
        private readonly List<ZombieModeAttributeModifierRecord> chillRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<ZombieModeAttributeModifierRecord> incenseRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<ZombieModeAttributeModifierRecord> charmRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<Light> campfires = new List<Light>();
        private GameObject lanternLight;
        private float nextTick = -1f, lastTick = -1f, lanternUntil = -1f, incenseUntil = -1f, exposure;
        private int campfireNight = -1;
        private bool lanternLowWarned, incenseLowWarned, charmWorn, chilled, exposureWarned, windExplained, disposed;

        internal SkyIslandFieldcraft(SkyIslandSession owner, SkyIslandStoryService storyService, Transform worldRoot)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (worldRoot == null) throw new ArgumentNullException("worldRoot");
            session = owner;
            story = storyService;
            root = worldRoot;
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            // 本趟自己的种子：采集点每趟每处只采一次，产出只需要「这一趟之内确定」，不必借会话的搜刮种子。
            seed = unchecked(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f) ^ 0x2F6B3D);
            gathering = new SkyIslandGathering(root, groundMask, Harvest);
            PlaceCampfires();
            Current = this;
        }

        internal int GatherPlaced { get { return gathering.PlacedCount; } }
        internal int GatherHarvested { get { return gathering.HarvestedCount; } }
        internal float Exposure { get { return exposure; } }
        internal bool Chilled { get { return chilled; } }

        /// <summary>
        /// 由 <see cref="SkyIslandWorldStory.Tick"/> 每帧调用，内部按 <see cref="SkyIslandFieldcraftRules.TickInterval"/> 游戏秒节流。
        /// 面板开着时 timeScale 为 0、`Time.time` 不走，这里自然不推进。
        /// </summary>
        internal void Tick()
        {
            if (disposed || !session.IsReady) return;
            float now = Time.time;
            if (now < nextTick) return;
            nextTick = now + SkyIslandFieldcraftRules.TickInterval;
            // 两次推进之间最多按 1 秒记：切出切回、长时间卡顿之后不一口气灌满寒意。
            float elapsed = lastTick < 0f ? 0f : Mathf.Min(now - lastTick, 1f);
            lastTick = now;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            bool night = IsNight();
            gathering.Tick(player.transform.position, night);
            UpdateCampfires(night);
            TickBuffs(now);
            TickWind(player, night, elapsed);
        }

        private static bool IsNight()
        {
            try { return SkyIslandFieldcraftRules.IsNight(GameClock.TimeOfDay.TotalHours); }
            catch (Exception)
            {
                // 取不到官方时钟就按白天算：宁可这趟没有夜风，也不凭空起风。
                return false;
            }
        }

        #region 采集

        private bool Harvest(SkyIslandGatherNode node)
        {
            if (disposed || node == null || !session.IsReady) return false;
            SkyIslandYield[] yields = SkyIslandFieldcraftRules.Roll(node,
                SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id), IsNight());
            var given = new List<SkyIslandYield>(yields.Length);
            for (int i = 0; i < yields.Length; i++)
            {
                int sent = Give(yields[i].TypeId, yields[i].Count);
                if (sent > 0) given.Add(new SkyIslandYield(yields[i].TypeId, sent));
            }
            // 物品资源缺失（部署损坏）时这一处照样收掉：玩家反复读条也拿不到东西，那不是重试能好的状态。
            session.Announce(SkyIslandFieldcraftRules.HarvestCaption(given.ToArray()), false);
            if (story != null) story.LogTiming("gather", node.Id);
            return true;
        }

        /// <summary>
        /// 把产出放进背包、放不下落在脚边（官方 `ItemUtilities.SendToPlayer` 的 sendToStorage:false 分支即 `item.Drop`）。
        /// **刻意不寄回基地仓库**：出击里得到的东西要跟着这一趟的背包承担风险，寄仓库等于免死金牌。
        /// 实例化之前先问 prefab——缺资源时官方给的空壳带着同一个 TypeID，回读分辨不出来。
        /// </summary>
        private static int Give(int typeId, int count)
        {
            int sent = 0;
            while (sent < count)
            {
                Item item = null;
                try
                {
                    if (ItemAssetsCollection.GetPrefab(typeId) == null) throw new InvalidOperationException("物品资源缺失");
                    item = ItemAssetsCollection.InstantiateSync(typeId);
                    if (item == null || item.TypeID != typeId) throw new InvalidOperationException("物品实例无效");
                    int stack = 1;
                    if (item.Stackable)
                    {
                        stack = Mathf.Clamp(count - sent, 1, Mathf.Max(1, item.MaxStackCount));
                        item.StackCount = stack;
                    }
                    ItemUtilities.SendToPlayer(item, false, false);
                    item = null;
                    sent += stack;
                }
                catch (Exception e)
                {
                    if (item != null) { try { item.DestroyTree(); } catch { /* 清理失败不影响其余产出 */ } }
                    Debug.LogWarning("[SkyIslandGather] 物品 " + typeId + " 发放失败：" + e.Message);
                    break;
                }
            }
            return sent;
        }

        #endregion

        #region 合成

        /// <summary>背包顶层里某件物品的件数（不数基地仓库，也不数背包里的容器）。</summary>
        internal int CountInPack(int typeId)
        {
            return ItemFactory.GetItemCountInInventory(typeId);
        }

        /// <summary>
        /// 按配方做一件东西。先在背包里点清材料，再把成品造出来，最后才扣材料：
        /// 成品资源缺失（部署损坏）时材料原样留着。成品放进背包、放不下落在脚边。
        /// </summary>
        internal bool Craft(SkyIslandRecipe recipe, out string message)
        {
            if (disposed || recipe == null || !session.IsReady)
            {
                message = L10n.T("现在没法做东西。", "Nothing can be made right now.");
                return false;
            }
            List<SkyIslandIngredient> missing = SkyIslandFieldcraftRules.Missing(recipe, CountInPack);
            if (missing.Count > 0)
            {
                message = SkyIslandFieldcraftRules.MissingMessage(missing);
                return false;
            }
            Item output = null;
            try
            {
                if (ItemAssetsCollection.GetPrefab(recipe.OutputTypeId) == null) throw new InvalidOperationException("成品资源缺失");
                output = ItemAssetsCollection.InstantiateSync(recipe.OutputTypeId);
                if (output == null || output.TypeID != recipe.OutputTypeId) throw new InvalidOperationException("成品实例无效");
                if (output.Stackable) output.StackCount = Mathf.Clamp(recipe.OutputCount, 1, Mathf.Max(1, output.MaxStackCount));
                for (int i = 0; i < recipe.Inputs.Length; i++)
                    if (!ConsumeFromPack(recipe.Inputs[i].TypeId, recipe.Inputs[i].Count))
                        throw new InvalidOperationException("扣除材料失败：" + recipe.Inputs[i].TypeId);
                ItemUtilities.SendToPlayer(output, false, false);
                output = null;
                message = SkyIslandFieldcraftRules.CraftedMessage(recipe);
                Debug.Log("[SkyIslandCraft] CRAFTED recipe=" + recipe.Id);
                return true;
            }
            catch (Exception e)
            {
                if (output != null) { try { output.DestroyTree(); } catch { /* 成品清理失败不影响回话 */ } }
                Debug.LogWarning("[SkyIslandCraft] 配方 " + recipe.Id + " 失败：" + e.Message);
                message = L10n.T("没做成——材料好像没对上，再看一眼背包。", "It did not come together — check your pack again.");
                return false;
            }
        }

        /// <summary>
        /// 从背包顶层扣材料，计数口径与 `ItemFactory.GetItemCountInInventory` 一致。
        /// 形态照 `ItemFactory.ConsumeItem`，但整堆拿走的物品要 DestroyTree——只 RemoveItem 会留下脱离背包的孤儿物品对象。
        /// </summary>
        private static bool ConsumeFromPack(int typeId, int count)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || ItemFactory.GetItemCountInInventory(typeId) < count) return false;
            var inventory = player.CharacterItem.Inventory;
            if (inventory == null || inventory.Content == null) return false;
            int remaining = count;
            for (int i = inventory.Content.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Item item = inventory.Content[i];
                if (item == null || item.TypeID != typeId) continue;
                int stack = item.Stackable ? item.StackCount : 1;
                if (stack > remaining)
                {
                    item.StackCount = stack - remaining;
                    remaining = 0;
                    break;
                }
                inventory.RemoveItem(item);
                item.DestroyTree();
                remaining -= stack;
            }
            return remaining == 0;
        }

        #endregion

        #region 耗材

        internal bool CanUse(SkyIslandFieldBuff buff)
        {
            if (disposed || buff == SkyIslandFieldBuff.None || !session.IsReady) return false;
            // 护符不叠加：已经系着一枚时按钮置灰，不吃掉第二枚。
            return buff != SkyIslandFieldBuff.Charm || !charmWorn;
        }

        /// <summary>耗材生效。返回 false 表示不在一趟有效的出击里（由物品自己提示）。</summary>
        internal bool UseConsumable(SkyIslandFieldBuff buff)
        {
            if (!CanUse(buff))
            {
                if (buff == SkyIslandFieldBuff.Charm && charmWorn && !disposed)
                {
                    session.Announce(SkyIslandFieldcraftRules.CharmAlreadyWorn, true);
                    return true;
                }
                return false;
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return false;
            float now = Time.time;
            switch (buff)
            {
                case SkyIslandFieldBuff.Lantern:
                    lanternUntil = now + SkyIslandFieldcraftRules.LanternSeconds;
                    lanternLowWarned = false;
                    EnsureLanternLight(player);
                    break;
                case SkyIslandFieldBuff.Incense:
                    incenseUntil = now + SkyIslandFieldcraftRules.IncenseSeconds;
                    incenseLowWarned = false;
                    // 续香只续时长，不叠第二份 Modifier。
                    if (incenseRecords.Count == 0)
                        RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.StaminaRecoverRate,
                            SkyIslandFieldcraftRules.IncenseStaminaRecover, modifierSource, incenseRecords, ModifierContext);
                    break;
                case SkyIslandFieldBuff.Charm:
                    WearCharm(player);
                    break;
            }
            session.Announce(SkyIslandFieldcraftRules.BuffStarted(buff), false);
            if (story != null) story.LogTiming("consumable", buff.ToString());
            return true;
        }

        private void WearCharm(CharacterMainControl player)
        {
            charmWorn = true;
            float maxBefore = player.Health != null ? player.Health.MaxHealth : 0f;
            RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.MaxHealth, SkyIslandFieldcraftRules.CharmMaxHealth,
                modifierSource, charmRecords, ModifierContext);
            RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.StaminaRecoverRate, SkyIslandFieldcraftRules.CharmStaminaRecover,
                modifierSource, charmRecords, ModifierContext);
            // 与归航菜同口径：只补「上限涨出来的那一截」，不做免费回满（眠苔的苔药按缺失比例收钱）。
            try
            {
                if (player.Health != null && maxBefore > 0f)
                {
                    float gained = player.Health.MaxHealth - maxBefore;
                    if (gained > 0f)
                        player.Health.SetHealth(Mathf.Min(player.Health.CurrentHealth + gained, player.Health.MaxHealth));
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 护符补血失败：" + e.Message); }
        }

        private void EnsureLanternLight(CharacterMainControl player)
        {
            if (lanternLight != null) return;
            lanternLight = new GameObject("SkyIslandLanternLight");
            lanternLight.transform.SetParent(player.transform, false);
            lanternLight.transform.localPosition = new Vector3(0f, 2.2f, 0.4f);
            Light light = lanternLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LanternColor;
            light.intensity = 1.6f;
            light.range = 11f;
            light.shadows = LightShadows.None;
        }

        private void DestroyLantern()
        {
            if (lanternLight != null) UnityEngine.Object.Destroy(lanternLight);
            lanternLight = null;
        }

        private void TickBuffs(float now)
        {
            if (lanternUntil > 0f)
            {
                float left = lanternUntil - now;
                if (left <= 0f)
                {
                    lanternUntil = -1f;
                    DestroyLantern();
                    session.Announce(SkyIslandFieldcraftRules.BuffEnded(SkyIslandFieldBuff.Lantern), false);
                }
                else if (left <= SkyIslandFieldcraftRules.BuffLowSeconds && !lanternLowWarned)
                {
                    lanternLowWarned = true;
                    session.Announce(SkyIslandFieldcraftRules.BuffLow(SkyIslandFieldBuff.Lantern), false);
                }
            }
            if (incenseUntil > 0f)
            {
                float left = incenseUntil - now;
                if (left <= 0f)
                {
                    incenseUntil = -1f;
                    RuntimeStatModifierTracker.RemoveAll(incenseRecords, ModifierContext);
                    session.Announce(SkyIslandFieldcraftRules.BuffEnded(SkyIslandFieldBuff.Incense), false);
                }
                else if (left <= SkyIslandFieldcraftRules.BuffLowSeconds && !incenseLowWarned)
                {
                    incenseLowWarned = true;
                    session.Announce(SkyIslandFieldcraftRules.BuffLow(SkyIslandFieldBuff.Incense), false);
                }
            }
        }

        #endregion

        #region 夜风与营火

        private void TickWind(CharacterMainControl player, bool night, float elapsed)
        {
            bool onBridge = false, onBoardwalk = false;
            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.25f, Vector3.down, out hit, 1.4f, groundMask,
                QueryTriggerInteraction.Ignore) && hit.collider != null && hit.transform.IsChildOf(root))
            {
                string colliderName = hit.collider.name;
                string region = SkyIslandStoryService.GroundRegionOf(colliderName);
                // 生成器把桥与中继平台切成 COL_Ground_<桥 ID>，解析不出区域的地面就是桥。
                onBridge = region == null && colliderName.StartsWith("COL_Ground_", StringComparison.Ordinal);
                onBoardwalk = region == "E";
            }
            bool stormPending = session.BothBeaconsLit && !session.StormResolved;
            int level = SkyIslandFieldcraftRules.WindLevel(night, onBridge, onBoardwalk, stormPending);
            bool warm = lanternUntil > 0f || incenseUntil > 0f || NearCampfire(player.transform.position);
            if (level > 0 && !warm && !windExplained)
            {
                windExplained = true;
                session.Announce(SkyIslandFieldcraftRules.WindExplain(stormPending), false);
            }
            exposure = SkyIslandFieldcraftRules.StepExposure(exposure, level, warm, elapsed);
            if (exposure <= SkyIslandFieldcraftRules.ExposureClear) exposureWarned = false;
            else if (!chilled && !exposureWarned && exposure >= SkyIslandFieldcraftRules.ExposureWarn)
            {
                exposureWarned = true;
                session.Announce(SkyIslandFieldcraftRules.ExposureWarning, true);
            }
            bool next = SkyIslandFieldcraftRules.NextChilled(chilled, exposure);
            if (next == chilled) return;
            chilled = next;
            if (chilled)
            {
                // 只动耐力恢复与饥饿速度：不掉血、不减跑速（噬风第一圈靠跑出去，减跑速就成了数值墙）。
                RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.StaminaRecoverRate,
                    SkyIslandFieldcraftRules.ChillStaminaRecover, modifierSource, chillRecords, ModifierContext);
                RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.EnergyCost,
                    SkyIslandFieldcraftRules.ChillEnergyCost, modifierSource, chillRecords, ModifierContext);
                session.Announce(SkyIslandFieldcraftRules.ChillStarted, true);
            }
            else
            {
                RuntimeStatModifierTracker.RemoveAll(chillRecords, ModifierContext);
                session.Announce(SkyIslandFieldcraftRules.ChillEnded, false);
            }
        }

        private void PlaceCampfires()
        {
            for (int i = 0; i < CampfireMarkers.Length; i++)
            {
                Transform marker = root.Find(CampfireMarkers[i]);
                if (marker == null) continue;
                GameObject go = new GameObject("SkyIslandCampfire_" + CampfireMarkers[i]);
                go.transform.SetParent(root, false);
                go.transform.position = marker.position + Vector3.up * 1.2f;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = CampfireColor;
                light.range = 9f;
                light.shadows = LightShadows.None;
                campfires.Add(light);
            }
        }

        /// <summary>营火夜里更旺：只在昼夜切换时写一次光强。</summary>
        private void UpdateCampfires(bool night)
        {
            int state = night ? 1 : 0;
            if (state == campfireNight) return;
            campfireNight = state;
            for (int i = 0; i < campfires.Count; i++)
                if (campfires[i] != null) campfires[i].intensity = night ? 2.4f : 1f;
        }

        private bool NearCampfire(Vector3 position)
        {
            float radius = SkyIslandFieldcraftRules.CampfireRadius;
            for (int i = 0; i < campfires.Count; i++)
            {
                if (campfires[i] == null) continue;
                Vector3 delta = campfires[i].transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }

        #endregion

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Current == this) Current = null;
            try
            {
                RuntimeStatModifierTracker.RemoveAll(chillRecords, ModifierContext);
                RuntimeStatModifierTracker.RemoveAll(incenseRecords, ModifierContext);
                RuntimeStatModifierTracker.RemoveAll(charmRecords, ModifierContext);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 摘除本趟增益失败：" + e.Message); }
            DestroyLantern();
            for (int i = 0; i < campfires.Count; i++)
                if (campfires[i] != null) UnityEngine.Object.Destroy(campfires[i].gameObject);
            campfires.Clear();
            Debug.Log("[SkyIslandFieldcraft] CLOSE gathered=" + gathering.HarvestedCount + "/" + gathering.PlacedCount);
            gathering.Dispose();
        }

        /// <summary>模块销毁时兜底清掉静态引用（会话销毁时 Dispose 已经清过）。</summary>
        internal static void ResetStaticCaches()
        {
            Current = null;
        }
    }
}
