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
    /// 4. **每件东西都有岛上的用处**：采集产出读剧情进度（修好的地方长得更旺）、配方随剧情解锁、风晶灯是持久的（写进手记，
    ///    先记后扣），护符替玩家挡噬风的风暴、便当算作晴禾的归航菜、带在身上的噬风之核让大风只算微风。
    /// 5. **内容批次四**：夜里的云蚋由 <see cref="SkyIslandGnats"/> 持有（伤害代码只在那里，本文件不出现），这里把夜风的那次采样、
    ///    灶火的烟与风晶灯的光、驱风香与风灯喂给它；灭蚊灯、蒲扇与药膏止痒经耗材入口转交。
    /// </summary>
    internal sealed class SkyIslandFieldcraft : IDisposable
    {
        /// <summary>
        /// 本趟的 owner。耗材的 `CanBeUsed` / `OnUse` 只在玩家按下使用时读一次（不在每帧路径上），
        /// 离岛时为 null，官方物品界面据此把「使用」按钮置灰，不会白吃掉一件耗材。
        /// </summary>
        internal static SkyIslandFieldcraft Current { get; private set; }

        private const string ModifierContext = "SkyIslandFieldcraft";
        private static readonly Color HearthColor = new Color(1f, 0.64f, 0.32f);
        /// <summary>风晶灯的光：比灶火淡一点、偏金白，远远看得出是点起来的那种灯。</summary>
        private static readonly Color LampColor = new Color(1f, 0.9f, 0.68f);
        private static readonly Color LanternColor = new Color(1f, 0.8f, 0.52f);

        private readonly SkyIslandSession session;
        private readonly SkyIslandStoryService story;
        private readonly Transform root;
        private readonly int groundMask;
        private readonly int seed;
        private readonly SkyIslandGathering gathering;
        private readonly Dictionary<string, SkyIslandYield[]> pendingHarvest = new Dictionary<string, SkyIslandYield[]>(StringComparer.Ordinal);
        private readonly object modifierSource = new object();
        private readonly List<ZombieModeAttributeModifierRecord> chillRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<ZombieModeAttributeModifierRecord> incenseRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<ZombieModeAttributeModifierRecord> charmRecords = new List<ZombieModeAttributeModifierRecord>();
        /// <summary>岛上亮着的灯：三处灶火 + 本存档点起来的风晶灯（<see cref="SkyIslandLights"/>）。只是光，灯旁暖和。</summary>
        private readonly List<Light> fires = new List<Light>();
        /// <summary>同一批灯按来历分开：灶火有烟（云蚋躲开），风晶灯只有光（云蚋循光而来）。</summary>
        private readonly List<Light> hearthFires = new List<Light>();
        private readonly List<Light> lampFires = new List<Light>();
        private readonly HashSet<string> fireMarkers = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>内容批次四：夜里的云蚋（装配失败时为 null，采集、合成与夜风照常）。</summary>
        private readonly SkyIslandGnats gnats;
        private GameObject lanternLight;
        private float nextTick = -1f, lastTick = -1f, lanternUntil = -1f, incenseUntil = -1f, exposure, nextCarryCheck = -1f;
        private int fireNight = -1, lightsLit;
        private bool lanternLowWarned, incenseLowWarned, charmWorn, chilled, exposureWarned, windExplained, coreCarried, coreExplained, disposed, inventoryBusy;

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
            lightsLit = story != null ? SkyIslandLights.LitCount(story.Current) : SkyIslandLights.HearthMarkers.Length;
            PlaceFires();
            // 内容批次四：夜里的云蚋。装配失败只放弃蚊群，不拖垮采集、合成与夜风。
            try { gnats = new SkyIslandGnats(owner, this, story, root, groundMask); }
            catch (Exception e)
            {
                gnats = null;
                Debug.LogWarning("[SkyIslandFieldcraft] 云蚋装配失败：" + e.Message);
            }
            Current = this;
        }

        internal SkyIslandGnats Gnats { get { return gnats; } }
        internal int GatherPlaced { get { return gathering.PlacedCount; } }
        internal int GatherHarvested { get { return gathering.HarvestedCount; } }
        internal float Exposure { get { return exposure; } }
        internal bool Chilled { get { return chilled; } }
        internal int LightsLit { get { return lightsLit; } }

        /// <summary>
        /// 本趟系着晴岚护符：噬风的风暴脉冲按 <see cref="SkyIslandFieldcraftRules.CharmStormWard"/> 减伤。
        /// 噬风每一波风暴读一次（<see cref="SkyIslandStormBoss"/>），不在每帧路径上；离岛时 owner 为 null，自然不减。
        /// </summary>
        internal static bool StormWarded
        {
            get
            {
                SkyIslandFieldcraft current = Current;
                return current != null && !current.disposed && current.charmWorn;
            }
        }

        /// <summary>
        /// 由 <see cref="SkyIslandWorldStory.Tick"/> 每帧调用，内部按 <see cref="SkyIslandFieldcraftRules.TickInterval"/> 游戏秒节流。
        /// 面板开着时 timeScale 为 0、`Time.time` 不走，这里自然不推进。
        /// </summary>
        internal void Tick()
        {
            if (disposed || !session.IsReady) return;
            float now = Time.time;
            // 云蚋每帧都要动（躲闪冲刺按帧推进），不走下面的 0.5 秒节流；夜里没有蚊子时几乎是空转。
            if (gnats != null) gnats.Frame(now, Time.deltaTime, CharacterMainControl.Main);
            if (now < nextTick) return;
            nextTick = now + SkyIslandFieldcraftRules.TickInterval;
            // 两次推进之间最多按 1 秒记：切出切回、长时间卡顿之后不一口气灌满寒意。
            float elapsed = lastTick < 0f ? 0f : Mathf.Min(now - lastTick, 1f);
            lastTick = now;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            bool night = IsNight();
            gathering.Tick(player.transform.position, night);
            UpdateFires(night);
            TickBuffs(now);
            TickWind(player, night, elapsed);
        }

        /// <summary>
        /// 夜里：光照、夜风、云蚋同一个判断（<see cref="SkyIslandNight"/>，钟点只从 <see cref="SkyIslandLighting.ClockHours"/> 读）。
        /// 官方时钟没有实例时 `TimeOfDay` 恒为 00:00，以前这里照读，整趟都被判成夜里。
        /// </summary>
        private static bool IsNight()
        {
            try { return SkyIslandNight.IsNight(SkyIslandLighting.ClockHours()); }
            catch (Exception)
            {
                // 取不到官方时钟就按白天算：宁可这趟没有夜风，也不凭空起风。
                return false;
            }
        }

        #region 采集

        private bool Harvest(SkyIslandGatherNode node)
        {
            if (disposed || inventoryBusy || node == null || !session.IsReady) return false;
            // 修好的地方长得更旺（菜畦、根环、铜脉、风眼、观星镜）：加成读这份存档，只加件数与附带门槛，不多抽随机数。
            SkyIslandStoryData data = story != null ? story.Current : null;
            SkyIslandYield[] yields;
            if (!pendingHarvest.TryGetValue(node.Id, out yields))
            {
                yields = SkyIslandFieldcraftRules.Roll(node,
                    SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id), IsNight(), data);
                pendingHarvest.Add(node.Id, yields);
            }
            var given = new List<SkyIslandYield>(yields.Length);
            bool complete = true;
            inventoryBusy = true;
            try
            {
                for (int i = 0; i < yields.Length; i++)
                {
                    int sent = Give(yields[i].TypeId, yields[i].Count);
                    if (sent > 0) given.Add(new SkyIslandYield(yields[i].TypeId, sent));
                    yields[i] = new SkyIslandYield(yields[i].TypeId, yields[i].Count - sent);
                    if (yields[i].Count > 0) complete = false;
                }
            }
            finally { inventoryBusy = false; }
            // 按已送达数量续发，不重抽昼夜/剧情奖励，也不因资源暂不可用把采集点吃掉。
            if (given.Count > 0)
                session.Announce(SkyIslandFieldcraftRules.HarvestCaption(given.ToArray(), SkyIslandFieldcraftRules.StoryBonusReason(node, data)), false);
            if (!complete)
                session.Announce(L10n.T("还有材料没能装好，采集点保留着，稍后可再取。", "Some materials could not be packed. This gathering spot remains available; try again later."), true);
            if (complete)
            {
                pendingHarvest.Remove(node.Id);
                if (story != null) story.LogTiming("gather", node.Id);
            }
            return complete;
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
                    if (!SkyIslandInventoryTransaction.TryDeliver(item, CharacterMainControl.Main))
                        throw new InvalidOperationException("产出未送达");
                    item = null;
                    sent += stack;
                }
                catch (Exception e)
                {
                    SkyIslandInventoryTransaction.DestroyUnowned(item);
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
            if (disposed || inventoryBusy || recipe == null || !session.IsReady)
            {
                message = L10n.T("现在没法做东西。", "Nothing can be made right now.");
                return false;
            }
            // 配方随剧情解锁：还不会做时居民说为什么，不点材料、不扣东西。
            if (!SkyIslandFieldcraftRules.Unlocked(recipe, story != null ? story.Current : null))
            {
                message = SkyIslandFieldcraftRules.LockedMessage(recipe);
                return false;
            }
            List<SkyIslandIngredient> missing = SkyIslandFieldcraftRules.Missing(recipe, CountInPack);
            if (missing.Count > 0)
            {
                message = SkyIslandFieldcraftRules.MissingMessage(missing);
                return false;
            }
            Item output = null;
            SkyIslandInventoryTransaction materials = null;
            inventoryBusy = true;
            try
            {
                if (ItemAssetsCollection.GetPrefab(recipe.OutputTypeId) == null) throw new InvalidOperationException("成品资源缺失");
                output = ItemAssetsCollection.InstantiateSync(recipe.OutputTypeId);
                if (output == null || output.TypeID != recipe.OutputTypeId) throw new InvalidOperationException("成品实例无效");
                if (output.Stackable) output.StackCount = Mathf.Clamp(recipe.OutputCount, 1, Mathf.Max(1, output.MaxStackCount));
                if (!SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main, recipe.Inputs, out materials))
                    throw new InvalidOperationException("预留材料失败");
                // 预留时触发的外部回调可能已经结束本趟，未发成品前仍能完整归还原件。
                if (disposed || !session.IsReady) throw new InvalidOperationException("本趟已经结束");
                if (!SkyIslandInventoryTransaction.TryDeliver(output, CharacterMainControl.Main))
                    throw new InvalidOperationException("成品未送达");
                output = null;
                materials.Commit();
                message = SkyIslandFieldcraftRules.CraftedMessage(recipe);
                Debug.Log("[SkyIslandCraft] CRAFTED recipe=" + recipe.Id);
                return true;
            }
            catch (Exception e)
            {
                SkyIslandInventoryTransaction.DestroyUnowned(output);
                Debug.LogWarning("[SkyIslandCraft] 配方 " + recipe.Id + " 失败：" + e.Message);
                message = L10n.T("没做成——材料好像没对上，再看一眼背包。", "It did not come together — check your pack again.");
                return false;
            }
            finally
            {
                try { if (materials != null) materials.Dispose(); }
                finally { inventoryBusy = false; }
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

        /// <summary>从背包顶层扣一件（云蚋那边包蛙卵用一把云苔纤维）。口径同 <see cref="ConsumeFromPack"/>。</summary>
        internal bool ConsumeOne(int typeId)
        {
            return !disposed && !inventoryBusy && ConsumeFromPack(typeId, 1);
        }

        #endregion

        #region 岛上的灯

        /// <summary>
        /// 在这处装置旁点起风晶灯（<see cref="SkyIslandLights"/>）。先预留整份材料，记手记成功才提交扣料：
        /// 写屏障、换槽或扣料异常时不点灯，未提交的材料由事务归还。点亮之后立刻补建那盏灯的光（灯旁暖和）；
        /// 第十盏亮起时念一句收尾，此后夜里不再起风。
        /// </summary>
        internal bool LightLamp(SkyIslandLight light, out string message)
        {
            if (disposed || inventoryBusy || light == null || story == null || !session.IsReady)
            {
                message = L10n.T("现在没法点灯。", "No lamp can be lit right now.");
                return false;
            }
            if (SkyIslandLights.Lit(story.Current, light.Id))
            {
                message = SkyIslandLights.AlreadyLit;
                return false;
            }
            if (!story.CanWrite)
            {
                message = story.SaveStatus;
                return false;
            }
            List<SkyIslandIngredient> lampMissing = SkyIslandFieldcraftRules.Missing(light.Inputs, CountInPack);
            if (lampMissing.Count > 0)
            {
                message = SkyIslandFieldcraftRules.MissingMessage(lampMissing);
                return false;
            }
            SkyIslandInventoryTransaction materials = null;
            inventoryBusy = true;
            try
            {
                // 库存事件会同步调用外部代码。先完整预留再写手记，写入失败则归还原件，
                // 不允许灯已永久点亮之后才发现材料扣不下来。
                if (!SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main, light.Inputs, out materials))
                {
                    message = L10n.T("材料在操作时发生变化，请再试一次。", "The materials changed while lighting the lamp. Try again.");
                    return false;
                }
                if (disposed || !session.IsReady)
                {
                    message = L10n.T("现在没法点灯。", "No lamp can be lit right now.");
                    return false;
                }
                string note;
                if (!story.RecordNote(light.Id, out note))
                {
                    message = note;
                    return false;
                }
                materials.Commit();
                AddFire(light.Marker, LampColor);
                int before = lightsLit;
                lightsLit = SkyIslandLights.LitCount(story.Current);
                message = SkyIslandLights.LitCaption(light, lightsLit);
                if (before < SkyIslandLights.Target && lightsLit >= SkyIslandLights.Target) session.Announce(SkyIslandLights.Capstone, false);
                Debug.Log("[SkyIslandLights] LIT id=" + light.Id + " total=" + lightsLit);
                return true;
            }
            finally
            {
                try { if (materials != null) materials.Dispose(); }
                finally { inventoryBusy = false; }
            }
        }

        /// <summary>离 <paramref name="from"/> 最近、这一趟还没采的某种采集点（罗盘用，只在按下时调一次）。</summary>
        internal bool TryNearestUnharvested(SkyIslandGatherKind kind, Vector3 from, out Vector3 position)
        {
            position = Vector3.zero;
            return !disposed && gathering.TryNearestUnharvested(kind, from, out position);
        }

        #endregion

        #region 耗材

        internal bool CanUse(SkyIslandFieldBuff buff)
        {
            if (disposed || buff == SkyIslandFieldBuff.None || !session.IsReady) return false;
            // 归航菜便当：菜畦重新开张之后才算归航菜，与晴禾那一顿共用本趟一次。不成立时官方只跳过这一项，便当照样当饭吃。
            if (buff == SkyIslandFieldBuff.Meal)
                return session.HasPlantingDelivered && session.Services != null && !session.Services.MealEaten;
            // 晴岚航徽：这一趟还没拉过缆绳才亮；附近有没有敌人在按下时判断，给出原因。
            if (buff == SkyIslandFieldBuff.Recall) return session.RecallAvailable;
            // 内容批次四：灭蚊灯同时至多两盏、蒲扇扇过要缓一口气、药膏止痒要真的在痒；都问蚊群 owner。不成立时按钮置灰，不白吃一件。
            if (buff == SkyIslandFieldBuff.Zapper) return gnats != null && gnats.CanDeployZapper;
            if (buff == SkyIslandFieldBuff.Fan) return gnats != null && gnats.FanReady;
            if (buff == SkyIslandFieldBuff.Soothe) return gnats != null && gnats.Itching;
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
                }
                else session.Announce(SkyIslandFieldcraftRules.OffIsland, true);
                return false;
            }
            if (buff == SkyIslandFieldBuff.Meal)
            {
                // 便当那一顿与晴禾的归航菜同一份加成、共用本趟一次：加成与补血都在服务 owner 里，这里不碰 stat。
                session.Announce(session.Services.PackedMeal(), false);
                if (story != null) story.LogTiming("consumable", buff.ToString());
                return true;
            }
            if (buff == SkyIslandFieldBuff.Recall)
            {
                // 航徽不消耗：战斗门、落点地面核对与每趟一次都在会话那边，这里只把回话读出来（拉不动时走警示）。
                string pulled;
                bool moved = session.TryRecallToDock(out pulled);
                session.Announce(pulled, !moved);
                return true;
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
                // 内容批次四：伤害与判定都在蚊群 owner 里（本文件不出现伤害），这里只转交并读出它的回话。
                case SkyIslandFieldBuff.Zapper:
                case SkyIslandFieldBuff.Fan:
                case SkyIslandFieldBuff.Soothe:
                    string said;
                    bool applied = UseAgainstGnats(buff, player, out said);
                    ReportConsumable(buff, said, applied);
                    return applied;
            }
            ReportConsumable(buff, SkyIslandFieldcraftRules.BuffStarted(buff), true);
            return true;
        }

        private void ReportConsumable(SkyIslandFieldBuff buff, string message, bool applied)
        {
            // 效果已经提交，字幕或统计失败不能把成功变成退款。
            try
            {
                if (!string.IsNullOrEmpty(message)) session.Announce(message, !applied);
                if (applied && story != null) story.LogTiming("consumable", buff.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 耗材提示失败：" + e.Message); }
        }

        /// <summary>灭蚊灯放下、蒲扇扇一下、药膏止痒：交给蚊群 owner，返回要读给玩家的那一句。</summary>
        private bool UseAgainstGnats(SkyIslandFieldBuff buff, CharacterMainControl player, out string said)
        {
            if (buff == SkyIslandFieldBuff.Zapper) return gnats.DeployZapper(player, out said);
            if (buff == SkyIslandFieldBuff.Fan) return gnats.SwingFan(player, out said);
            said = gnats.Soothe();
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
            string region = null;
            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.25f, Vector3.down, out hit, 1.4f, groundMask,
                QueryTriggerInteraction.Ignore) && hit.collider != null && hit.transform.IsChildOf(root))
            {
                string colliderName = hit.collider.name;
                region = SkyIslandStoryService.GroundRegionOf(colliderName);
                // 生成器把桥与中继平台切成 COL_Ground_<桥 ID>，解析不出区域的地面就是桥。
                onBridge = region == null && colliderName.StartsWith("COL_Ground_", StringComparison.Ordinal);
                onBoardwalk = region == "E";
            }
            bool stormPending = session.BothBeaconsLit && !session.StormResolved;
            // 岛上的灯凑满十盏之后夜里不再起风；带着噬风之核时大风只算微风。
            int gale = SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(night, lightsLit), onBridge, onBoardwalk, stormPending);
            int level = SkyIslandFieldcraftRules.CoreEased(gale, CarriesCore());
            // 灶火与风晶灯旁、驱风香什么风都挡；只有风灯时大风里只挡一半。
            SkyIslandWarmth warmth = SkyIslandFieldcraftRules.Warmth(NearFire(player.transform.position), incenseUntil > 0f, lanternUntil > 0f);
            // 内容批次四：云蚋读同一次采样——环境风（噬风之核只改你身上的寒意、不改空气）、灶火的烟、点起来的风晶灯、驱风香与风灯。
            if (gnats != null)
                gnats.Sample(night, gale, region, player, incenseUntil > 0f, lanternUntil > 0f, NearHearth(player.transform.position),
                    NearLamp(player.transform.position), elapsed);
            if (level > 0 && warmth == SkyIslandWarmth.None && !windExplained)
            {
                windExplained = true;
                session.Announce(SkyIslandFieldcraftRules.WindExplain(stormPending), false);
            }
            if (level < gale && !coreExplained)
            {
                coreExplained = true;
                session.Announce(SkyIslandFieldcraftRules.CoreEasesGale, false);
            }
            exposure = SkyIslandFieldcraftRules.StepExposure(exposure, level, warmth, elapsed);
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

        /// <summary>
        /// 岛上的灯（<see cref="SkyIslandLights"/>）：三处居民的灶火一直亮着，其余是本存档点起来的风晶灯。只是光、没有碰撞体，
        /// 不参与交互竞争；灶火与风晶灯旁一样暖和。进岛时建一次，点起一盏新灯时补建那一盏。
        /// </summary>
        private void PlaceFires()
        {
            for (int i = 0; i < SkyIslandLights.HearthMarkers.Length; i++) AddFire(SkyIslandLights.HearthMarkers[i], HearthColor);
            if (story == null) return;
            SkyIslandLight[] lamps = SkyIslandLights.All;
            for (int i = 0; i < lamps.Length; i++)
                if (SkyIslandLights.Lit(story.Current, lamps[i].Id)) AddFire(lamps[i].Marker, LampColor);
        }

        private void AddFire(string markerName, Color color)
        {
            if (!fireMarkers.Add(markerName)) return;
            Transform marker = root.Find(markerName);
            if (marker == null)
            {
                // 锚点不在场景里（布局改名或场景包不对）就少一处火：说出来，别让「这里该有灶火」静默消失。
                Debug.LogWarning("[SkyIslandFieldcraft] 灯的锚点不在场景里：" + markerName);
                return;
            }
            bool hearth = Array.IndexOf(SkyIslandLights.HearthMarkers, markerName) >= 0;
            GameObject go = new GameObject("SkyIslandFire_" + markerName);
            go.transform.SetParent(root, false);
            go.transform.position = marker.position + Vector3.up * 1.2f;
            if (hearth)
            {
                // 灶火看得见：火苗与烟生在装置旁的地面上，点光、取暖与驱蚋的判定一起挪过去（SkyIslandHearthFx）。风晶灯仍只是光。
                try
                {
                    Vector3 spot = SkyIslandHearthFx.FindSpot(root, marker, groundMask);
                    go.transform.position = spot + Vector3.up * 1.2f;
                    SkyIslandHearthFx.Build(go.transform, spot);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 灶火的火与烟没建起来：" + markerName + " " + e.Message); }
            }
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 9f;
            light.intensity = fireNight == 1 ? 2.4f : 1f;
            light.shadows = LightShadows.None;
            fires.Add(light);
            if (hearth) hearthFires.Add(light);
            else lampFires.Add(light);
        }

        /// <summary>灯夜里更亮：只在昼夜切换时写一次光强（半路点起的灯按当时的昼夜建）。</summary>
        private void UpdateFires(bool night)
        {
            int state = night ? 1 : 0;
            if (state == fireNight) return;
            fireNight = state;
            for (int i = 0; i < fires.Count; i++)
                if (fires[i] != null) fires[i].intensity = night ? 2.4f : 1f;
        }

        private bool NearFire(Vector3 position)
        {
            return NearAny(fires, position, SkyIslandFieldcraftRules.CampfireRadius);
        }

        /// <summary>站在灶火的烟里（比取暖半径大一圈）：云蚋不刷、不近身。</summary>
        private bool NearHearth(Vector3 position)
        {
            return NearAny(hearthFires, position, SkyIslandMosquitoRules.SmokeRadius);
        }

        /// <summary>点起来的风晶灯附近：光招云蚋。</summary>
        private bool NearLamp(Vector3 position)
        {
            return NearAny(lampFires, position, SkyIslandMosquitoRules.LampRadius);
        }

        private static bool NearAny(List<Light> lights, Vector3 position, float radius)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null) continue;
                Vector3 delta = lights[i].transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }

        /// <summary>
        /// 背包顶层有没有噬风之核（与合成台数材料同一口径）。按 <see cref="SkyIslandFieldcraftRules.CarryCheckInterval"/> 游戏秒数一次，
        /// 夜风推进本来就 0.5 秒才走一趟，不在每帧路径上。
        /// </summary>
        private bool CarriesCore()
        {
            float now = Time.time;
            if (now < nextCarryCheck) return coreCarried;
            nextCarryCheck = now + SkyIslandFieldcraftRules.CarryCheckInterval;
            try { coreCarried = CountInPack(BossRushItemIds.SkyIslandWindeaterCore) > 0; }
            catch (Exception) { coreCarried = false; }
            return coreCarried;
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
            // 蚊群先收：它的痒 Modifier、灭蚊灯与嗡声都是本趟的。
            if (gnats != null) gnats.Dispose();
            for (int i = 0; i < fires.Count; i++)
                if (fires[i] != null) UnityEngine.Object.Destroy(fires[i].gameObject);
            fires.Clear();
            hearthFires.Clear();
            lampFires.Clear();
            fireMarkers.Clear();
            pendingHarvest.Clear();
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
