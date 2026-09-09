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
        internal const int HealPrice = 120;
        internal const float HealCooldown = 120f;
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
        private float healReadyAt;
        private bool mealUsed, disposed;

        internal SkyIslandServices(CharacterMainControl mainPlayer, GameObject sceneRoot, int seed)
        {
            player = mainPlayer;
            root = sceneRoot;
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
            if (disposed || player == null) return "现在没法整备。";
            var items = new List<Item>();
            CollectCarried(items);
            var plan = new List<RepairEntry>();
            int quoted = Quote(items, plan);
            if (plan.Count == 0) return "浮舟：你身上的家伙都还结实，用不着我动手。";
            int price = quoted < RepairMinimumPrice ? RepairMinimumPrice : quoted;
            bool account = AccountAvailable;
            if (!EconomyManager.IsEnough(new Cost((long)price), account, true))
                return "浮舟：整备要 " + price + "，这次凑不够就先记着。";
            if (!EconomyManager.Pay(new Cost((long)price), account, true))
                return "浮舟：钱没走通，先别急，回头再来。";
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
            return "浮舟：好了，" + repaired + " 件。云海上的东西，钝一点都不行。（花费 " + price + "）";
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
                if (item != character && item.UseDurability && item.MaxDurability > 0f) result.Add(item);
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

        internal string Heal()
        {
            if (disposed || player == null || player.Health == null) return "现在没法处理伤口。";
            if (Time.unscaledTime < healReadyAt)
                return "眠苔：药还在熬，" + Mathf.CeilToInt(healReadyAt - Time.unscaledTime) + " 秒后再来。";
            if (player.Health.CurrentHealth >= player.Health.MaxHealth - 0.01f)
                return "眠苔：你没受伤，省下这笔吧。";
            bool account = AccountAvailable;
            if (!EconomyManager.IsEnough(new Cost((long)HealPrice), account, true))
                return "眠苔：一副药 " + HealPrice + "，这次不够。";
            if (!EconomyManager.Pay(new Cost((long)HealPrice), account, true))
                return "眠苔：钱没走通，先歇一会儿。";
            player.Health.SetHealth(player.Health.MaxHealth);
            healReadyAt = Time.unscaledTime + HealCooldown;
            return "眠苔：苔药敷上了。云海上摔一跤可不好受。（花费 " + HealPrice + "）";
        }

        #endregion

        #region 晴禾 · 归航菜

        internal string Meal(bool plantingDelivered)
        {
            if (disposed || player == null) return "现在吃不上饭。";
            if (!plantingDelivered) return "晴禾：种植记录还没回来，菜畦也就还没重新开张。";
            if (mealUsed) return "晴禾：这一顿你已经吃过啦，下次出岛再来。";
            float maxHealthBeforeMeal = player.Health != null ? player.Health.MaxHealth : 0f;
            bool any = false;
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.MaxHealth,
                MealMaxHealthBonus, modifierSource, records, "SkyIslandMeal");
            // 官方角色只有 WalkSpeed / RunSpeed / Moveability 三个移动 stat，"MoveSpeed" 是 Animator 参数名。
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.RunSpeed,
                MealSpeedBonus, modifierSource, records, "SkyIslandMeal");
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.WalkSpeed,
                MealSpeedBonus, modifierSource, records, "SkyIslandMeal");
            if (!any) return "晴禾：这顿饭好像没落到实处，回头我再试试。";
            mealUsed = true;
            // 只补「上限涨出来的那一截」，不做免费回满：一顿免费饭如果能回满血，
            // 眠苔那副 120 的苔药就永远没人买了。受伤仍然得花钱治。
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
            return "晴禾：归航菜，趁热。走远路的人得先吃饱。（本次出击生效）";
        }

        #endregion

        #region 苇白 · 委托奖励

        /// <summary>
        /// 交单奖励：在指定位置留一个对应档次的奖励箱，玩家用官方战利品 UI 领取（背包满也不会丢）。
        /// 落点无效时退回玩家身前，避免奖励箱掉到世界原点或压在玩家身上。
        /// </summary>
        internal bool DropBountyReward(Vector3 position, SkyIslandLootTier tier, int round)
        {
            if (disposed || root == null) return false;
            if (position.sqrMagnitude < 1f)
            {
                if (player == null) return false;
                position = player.transform.position + player.transform.forward * 1.5f;
            }
            return SkyIslandRewardCrate.Create(root.transform, position, tier,
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
