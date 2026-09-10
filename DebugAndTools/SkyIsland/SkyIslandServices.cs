using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛居民提供的实用服务 owner。
    ///
    /// 四项服务都刻意只用**已有系统**，不引入新 TypeID、不接官方商店 UI
    /// （`StockShopView` 是场景内预制体，独立出击关卡不保证存在）：
    /// - 浮舟 · 渡口整备：按**官方维修口径**（价值计价 + 永久磨损）修复随身耐久装备。
    /// - 眠苔 · 苔药调理：现金回满生命，带冷却（`Health.SetHealth`）。
    /// - 晴禾 · 归航菜：交还种植记录后，每次出击免费一份本局属性加成（`RuntimeStatModifierTracker`）。
    /// - 苇白 · 航务委托：交单后在她脚边留一个奖励箱（与搜刮点同一条建箱路径）。
    ///
    /// 本类持有本局挂上的 Modifier 记录，会话销毁时统一摘除；不往 `ModBehaviour` 加 partial。
    /// </summary>
    internal sealed class SkyIslandServices : IDisposable
    {
        /// <summary>
        /// 渡口整备的服务费下限。单价本身走官方维修口径（见 <see cref="RepairPriceFor"/>），
        /// 这里只保证跑一趟至少收这么多，避免「只掉了一点耐久」时白使唤人。
        /// </summary>
        internal const int RepairMinimumPrice = 60;
        /// <summary>
        /// 回满一整条命的苔药价。实际报价按缺失血量比例折算（见 <see cref="HealPriceFor"/>）。
        ///
        /// 旧口径是「120 定额、全额回满、120 秒冷却」，等于岛上没有血量压力：
        /// <see cref="AccountAvailable"/> 让银行存款在这张出击图里也能花（场景包没关
        /// `accountAvailable`），于是只要有钱就能无限满血。按比例计价 + 5 分钟冷却之后，
        /// 一趟出击基本只够用一到两次，受伤重新变成需要付出代价的事。
        /// </summary>
        internal const int HealPriceFull = 480;
        /// <summary>苔药最低服务费：擦破点皮也不能只收零头。</summary>
        internal const int HealPriceMinimum = 60;
        internal const float HealCooldown = 300f;
        /// <summary>
        /// 谢礼箱相对交单锚点的方位角起点与每轮步进。三轮各差 120°、距锚点
        /// <see cref="BountyRewardDistance"/>，彼此至少 5.2 m，不会叠成一摞；
        /// 3 m 也足以避开搜索点 2.2 m 的触发盒与居民胶囊，不再抢同一次交互选择。
        /// </summary>
        internal const float BountyRewardBearing = 30f;
        internal const float BountyRewardBearingStep = 120f;
        internal const float BountyRewardDistance = 3f;
        internal const float MealMaxHealthBonus = 0.12f;
        internal const float MealSpeedBonus = 0.08f;
        /// <summary>遍历随身物品的安全上限，避免异常物品树把服务卡住。</summary>
        internal const int WalkNodeBudget = 240;
        internal const int WalkDepthBudget = 4;
        internal const int BountyRewardItemCount = 3;

        private readonly List<ZombieModeAttributeModifierRecord> records =
            new List<ZombieModeAttributeModifierRecord>();
        private readonly object modifierSource = new object();
        private readonly CharacterMainControl player;
        private readonly GameObject root;
        private readonly int raidSeed;
        private readonly int groundMask;
        private float healReadyAt;
        private bool mealUsed, disposed;

        internal SkyIslandServices(CharacterMainControl mainPlayer, GameObject sceneRoot, int ground, int seed)
        {
            player = mainPlayer;
            root = sceneRoot;
            groundMask = ground;
            raidSeed = seed;
        }

        /// <summary>
        /// 这张图能不能动银行账户。
        ///
        /// 用 `LevelConfig.accountAvailable` 而**不是** `SaveCharacter`：后者是「是否把主角写回存档」，
        /// 出击图里同样是 true（天空岛的官方合同还硬要求它为 true），拿它当账户门控恒真、等于没门控。
        /// `accountAvailable` 才是官方表达「账户在本图可用」的字段——`ContextualMoneyAndCash`
        /// 就是靠它决定要不要显示账户余额。
        ///
        /// 注意：天空岛场景包目前没有显式关掉它（序列化默认 true），所以现在**账户是可以花的**。
        /// 若要让岛上服务只收随身现金，把作者场景的 `LevelConfig.accountAvailable` 设为 false
        /// 即可（见 `ArtSource/SkyIsland/OFFICIAL_SCENE_CONTRACT.md`），这里无需改代码。
        /// </summary>
        private static bool AccountAvailable
        {
            get { return LevelConfig.Instance == null || LevelConfig.Instance.AccountAvailable; }
        }

        #region 浮舟 · 渡口整备

        /// <summary>
        /// 纯计算：单件维修价与本次产生的永久磨损，口径与官方 `ItemRepairView.CalculateRepairPrice`
        /// 完全一致——**按物品价值**计价，而不是按耐久点数。
        ///
        /// 之前用「缺口耐久 × 3」是价值无关的，盈亏平衡点落在 `Value = 6 × MaxDurability`：
        /// 高价值武器会被卖得比官方维修台便宜一到两个数量级，廉价高耐久护甲反而更贵。
        ///
        /// <paramref name="lostPercentage"/> 是本次修理要追加的 `DurabilityLoss`。
        /// 官方每修一次都会永久削一点上限，天空岛不能例外，否则渡口就是「又便宜又无损」的
        /// 修理站，把官方设计的装备损耗沉降口整个架空。
        /// </summary>
        internal static int RepairPriceFor(int itemValue, float maxDurability, float currentDurability,
            float durabilityLoss, float repairLossRatio, out float lostPercentage)
        {
            lostPercentage = 0f;
            if (maxDurability <= 0f) return 0;
            float ceiling = maxDurability * (1f - durabilityLoss);
            float repairAmount = ceiling - currentDurability;
            if (repairAmount <= 0f) return 0;
            float lostAmount = repairAmount * repairLossRatio;
            repairAmount -= lostAmount;
            if (repairAmount <= 0f) return 0;
            lostPercentage = lostAmount / maxDurability;
            return Mathf.CeilToInt(itemValue * (repairAmount / maxDurability) * 0.5f);
        }

        /// <summary>一件待修物品的报价与随之而来的永久磨损。</summary>
        private struct RepairEntry
        {
            internal Item Item;
            internal int Price;
            internal float LostPercentage;
        }

        /// <summary>整备总价：逐件按官方口径报价再求和，最后压上服务费下限。</summary>
        private int Quote(List<Item> items, List<RepairEntry> plan)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                try
                {
                    // 入列条件是「真的缺耐久」而不是「报价大于 0」：官方 RepairItems 会修列表里的
                    // 每一件，价格为 0 也照修。只看报价会把 Value=0 的受损物品永远排除在外。
                    if (item.Durability >= item.MaxDurabilityWithLoss) continue;
                    float lost;
                    int price = RepairPriceFor(item.Value, item.MaxDurability, item.Durability,
                        item.DurabilityLoss, item.GetRepairLossRatio(), out lost);
                    total += price;
                    if (plan != null) plan.Add(new RepairEntry { Item = item, Price = price, LostPercentage = lost });
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandServices] 报价失败：" + e.Message); }
            }
            return total;
        }

        internal string Repair()
        {
            if (disposed || player == null) return L10n.T("现在没法整备。", "No refit is possible right now.");
            var items = new List<Item>();
            CollectCarried(items);
            var plan = new List<RepairEntry>();
            int quoted = Quote(items, plan);
            if (plan.Count == 0)
                return L10n.T("浮舟：你身上的家伙都还结实，用不着我动手。",
                    "Fuzhou: Everything you carry is still sound. Nothing for me to do.");
            int price = quoted < RepairMinimumPrice ? RepairMinimumPrice : quoted;
            bool account = AccountAvailable;
            if (!EconomyManager.IsEnough(new Cost((long)price), account, true))
                return L10n.T("浮舟：整备要 ", "Fuzhou: The refit runs ") + price +
                    L10n.T("，这次凑不够就先记着。", ". You are short this time — I will note it down.");
            if (!EconomyManager.Pay(new Cost((long)price), account, true))
                return L10n.T("浮舟：钱没走通，先别急，回头再来。",
                    "Fuzhou: The payment did not go through. No rush — come back later.");
            int repaired = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                RepairEntry entry = plan[i];
                try
                {
                    // 与官方 `ItemRepairView.Repair` 同一套写法：先累加永久磨损，再补到新的上限。
                    entry.Item.DurabilityLoss += entry.LostPercentage;
                    entry.Item.Durability = entry.Item.MaxDurability * (1f - entry.Item.DurabilityLoss);
                    repaired++;
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandServices] 修复失败：" + e.Message); }
            }
            return L10n.T("浮舟：好了，", "Fuzhou: Done — ") + repaired +
                (repaired == 1
                    ? L10n.T(" 件。云海上的东西，钝一点都不行。（花费 ",
                        " piece. Nothing blunt lasts out on the cloud sea. (cost ")
                    : L10n.T(" 件。云海上的东西，钝一点都不行。（花费 ",
                        " pieces. Nothing blunt lasts out on the cloud sea. (cost ")) + price + L10n.T("）", ")");
        }

        /// <summary>有界遍历随身物品树：只收有耐久的物品，节点与深度都有预算。</summary>
        private void CollectCarried(List<Item> result)
        {
            Item character = player.CharacterItem;
            if (character == null) return;
            var queue = new Queue<KeyValuePair<Item, int>>();
            queue.Enqueue(new KeyValuePair<Item, int>(character, 0));
            int budget = WalkNodeBudget;
            while (queue.Count > 0 && budget-- > 0)
            {
                KeyValuePair<Item, int> entry = queue.Dequeue();
                Item item = entry.Key;
                int depth = entry.Value;
                if (item == null || depth > WalkDepthBudget) continue;
                // 入列门与官方维修台 `ItemRepairView.CanRepair` 一致：带 Repairable 标签（`Item.Repairable`
                // = UseDurability 且含该标签）、剩余上限不低于 1。只看 UseDurability 会把药品、食物、钥匙这类
                // 「用耐久记剩余次数」的物品也收进来（Drug / FoodDrink 用完扣 Durability），按维修价把次数补满，
                // 官方维修台对它们显示「无法维修」。已经修到上限不足 1 的「损坏」装备官方同样拒修。
                if (item != character && item.Repairable && item.MaxDurabilityWithLoss >= 1f) result.Add(item);
                try
                {
                    if (item.Slots != null)
                        foreach (Slot slot in item.Slots)
                            if (slot != null && slot.Content != null)
                                queue.Enqueue(new KeyValuePair<Item, int>(slot.Content, depth + 1));
                    if (item.Inventory != null && item.Inventory.Content != null)
                        foreach (Item child in item.Inventory.Content)
                            if (child != null) queue.Enqueue(new KeyValuePair<Item, int>(child, depth + 1));
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandServices] 遍历随身物品失败：" + e.Message); }
            }
        }

        #endregion

        #region 眠苔 · 苔药调理

        /// <summary>
        /// 纯计算：按**缺失血量比例**报价，而不是定额。
        /// 用比例而不是按血量点数，报价与玩家 `MaxHealth` 的实际量级无关——官方将来调血量、
        /// 或者玩家自己吃了上限加成，这里都不用重算。
        /// </summary>
        internal static int HealPriceFor(float currentHealth, float maxHealth)
        {
            if (maxHealth <= 0f) return HealPriceMinimum;
            float missing = Mathf.Clamp01((maxHealth - currentHealth) / maxHealth);
            return Mathf.Max(HealPriceMinimum, Mathf.CeilToInt(HealPriceFull * missing));
        }

        internal string Heal()
        {
            if (disposed || player == null || player.Health == null)
                return L10n.T("现在没法处理伤口。", "Wounds cannot be treated right now.");
            // 冷却是玩法计时，走游戏时间（AGENTS「玩法计时一律走游戏时间」）：旧写法 unscaledTime 在暂停菜单、
            // 拍照模式与剧情面板（timeScale 压到 0）背后照走，开着暂停菜单挂 5 分钟就能再敷一副。
            if (Time.time < healReadyAt)
            {
                int wait = Mathf.CeilToInt(healReadyAt - Time.time);
                return L10n.T("眠苔：药还在熬，", "Miantai: The remedy is still steeping — ") + wait +
                    (wait == 1
                        ? L10n.T(" 秒后再来。", " second until the next dose.")
                        : L10n.T(" 秒后再来。", " seconds until the next dose."));
            }
            if (player.Health.CurrentHealth >= player.Health.MaxHealth - 0.01f)
                return L10n.T("眠苔：你没受伤，省下这笔吧。", "Miantai: You are not hurt. Save your money.");
            int price = HealPriceFor(player.Health.CurrentHealth, player.Health.MaxHealth);
            bool account = AccountAvailable;
            if (!EconomyManager.IsEnough(new Cost((long)price), account, true))
                return L10n.T("眠苔：这副药要 ", "Miantai: This dose costs ") + price +
                    L10n.T("，这次不够。", ". Not enough this time.");
            if (!EconomyManager.Pay(new Cost((long)price), account, true))
                return L10n.T("眠苔：钱没走通，先歇一会儿。",
                    "Miantai: The payment did not go through. Rest a moment.");
            player.Health.SetHealth(player.Health.MaxHealth);
            healReadyAt = Time.time + HealCooldown;
            return L10n.T("眠苔：苔药敷上了。云海上摔一跤可不好受。（花费 ",
                "Miantai: The moss is on. A fall out here is no small thing. (cost ") + price + L10n.T("）", ")");
        }

        #endregion

        #region 晴禾 · 归航菜

        internal string Meal(bool plantingDelivered)
        {
            if (disposed || player == null) return L10n.T("现在吃不上饭。", "There is no meal to be had right now.");
            if (!plantingDelivered)
                return L10n.T("晴禾：种植记录还没回来，菜畦也就还没重新开张。",
                    "Qinghe: The planting record is not back yet, so the garden has not reopened.");
            if (mealUsed)
                return L10n.T("晴禾：这一顿你已经吃过啦，下次出岛再来。",
                    "Qinghe: You have already had this one. Come back next trip.");
            float maxHealthBeforeMeal = player.Health != null ? player.Health.MaxHealth : 0f;
            bool any = false;
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.MaxHealth,
                MealMaxHealthBonus, modifierSource, records, "SkyIslandMeal");
            // 官方角色只有 WalkSpeed / RunSpeed / Moveability 三个移动 stat，"MoveSpeed" 是 Animator 参数名。
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.RunSpeed,
                MealSpeedBonus, modifierSource, records, "SkyIslandMeal");
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.WalkSpeed,
                MealSpeedBonus, modifierSource, records, "SkyIslandMeal");
            if (!any)
                return L10n.T("晴禾：这顿饭好像没落到实处，回头我再试试。",
                    "Qinghe: That meal did not seem to take. Let me try again later.");
            mealUsed = true;
            // 只补「上限涨出来的那一截」，不做免费回满：一顿免费饭如果能回满血，
            // 眠苔那副按缺失比例计价（最高 HealPriceFull）的苔药就永远没人买了。受伤仍然得花钱治。
            try
            {
                // 基准取不到（吃饭前 Health 不可用）时宁可不补：否则 gained 会退化成整条血量上限，
                // 又变回免费回满。
                if (player.Health != null && maxHealthBeforeMeal > 0f)
                {
                    float gained = player.Health.MaxHealth - maxHealthBeforeMeal;
                    if (gained > 0f)
                        player.Health.SetHealth(Mathf.Min(player.Health.CurrentHealth + gained, player.Health.MaxHealth));
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandServices] 归航菜补血失败：" + e.Message); }
            return L10n.T("晴禾：归航菜，趁热。走远路的人得先吃饱。（本次出击生效）",
                "Qinghe: A homecoming meal — eat it while it is hot. Long roads start on a full stomach. (this raid only)");
        }

        #endregion

        #region 苇白 · 委托奖励

        /// <summary>
        /// 交单奖励：在交单锚点**旁边**留一个对应档次的奖励箱，玩家用官方战利品 UI 领取（背包满也不会丢）。
        ///
        /// 传进来的 <paramref name="position"/> 是锚点（苇白本人或风铃集留言板），**不能**直接当落点：
        /// 那会把箱子生成在 NPC / 告示牌身体里，抢同一次交互选择；而且三轮委托的锚点完全相同，
        /// 箱子会一摞叠在一处，只有最上面那个按得到。这里按轮次分 120° 方位角退开 3 m，
        /// 再走与搜刮点同一套地面/墙体裁决。落点探测失败才退回锚点本身（fail-open，不能不给奖励）。
        /// </summary>
        internal bool DropBountyReward(Vector3 position, SkyIslandLootTier tier, int round)
        {
            if (disposed || root == null) return false;
            if (position.sqrMagnitude < 1f)
            {
                if (player == null) return false;
                position = player.transform.position + player.transform.forward * 1.5f;
            }
            Vector3 drop;
            if (!SkyIslandRewardCrate.TryFindCratePosition(root.transform, position,
                BountyRewardBearing + round * BountyRewardBearingStep, BountyRewardDistance, groundMask, out drop))
                drop = position;
            return SkyIslandRewardCrate.Create(root.transform, drop, tier,
                "SkyIslandBountyReward_" + round, "Bounty" + round, raidSeed, BountyRewardItemCount, true);
        }

        #endregion

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (records.Count > 0) RuntimeStatModifierTracker.RemoveAll(records, "SkyIslandMeal");
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandServices] 摘除本局加成失败：" + e.Message); }
            records.Clear();
        }
    }
}
