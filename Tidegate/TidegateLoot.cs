using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的搜刮箱 owner。箱子是官方 `InteractableLootbox`（`SkyIslandRewardCrate.Build`，交互名「搜集」），
    /// 物品从官方物资池按档次与类别抽（`SkyIslandLootPools`，带价值上限与黑名单）。走近 60 m 才建，0.5 秒最多建一个；
    /// 泡在深水里的箱子收起来（原型规则：水深超过 0.5 m 不能搜），水退了再放出来——滩涂、沉船、金库的价值来自这一点。
    /// </summary>
    internal sealed class TidegateLoot : IDisposable
    {
        private const float ActivationRange = 60f;
        private const float TickInterval = 0.5f;
        /// <summary>
        /// 玩家贴着箱子时不收起：官方搜刮界面可能正开着。只管「收起」这一个方向——被淹的箱子已经停用、没有界面开着它，
        /// 水退了就马上放出来，站在旁边等退潮的人不用先走开再回来。
        /// </summary>
        private const float KeepNearRange = 4f;
        /// <summary>走到被淹的箱柜跟前这么近，提示一次「等退潮」（模型是场景静态件、照样看得见，交互体却停用了）。</summary>
        private const float FloodHintRange = 3.5f;

        /// <summary>
        /// 剧情箱柜：场景包标记名 → 原型布局（`v2/js/layout.js` 与导出的 `tidegate_layout.json` 的 `fixed`）里这只箱子的固定内容。
        /// 布局重导会重新编号：`tools/build_tidegate_navigation.py --check` 逐条核对这张表与布局，对不上就失败（CR-2026-11-05-026）。
        /// </summary>
        internal static readonly string[,] StoryContainers =
        {
            { "LOOT_safe_68", "page1" },     // 第三水厂控制室保险箱
            { "LOOT_house_69", "page2" },    // 镇区泵站值班桌
            { "LOOT_house_70", "page3" },    // 潮位站记录柜
            { "LOOT_vault_71", "page4" },    // 储蓄所所长的保险柜
            { "LOOT_vault_71", "tidewatch" },
            { "LOOT_locker_72", "wheel" },   // 挖沙船船舱储物柜
            { "LOOT_safe_77", "goldbar" },   // 沉船船长的保险柜：原型放一根小金条，正式版放一件金库档的值钱物
        };

        private sealed class Point
        {
            internal Transform Marker;
            internal string Id, Kind;
            internal float Yaw;
            internal InteractableLootbox Box;
            internal bool Built, Failed, Flooded, Opened, FloodHinted;
        }

        private struct Spec
        {
            internal SkyIslandLootTier Tier;
            internal SkyIslandLootCategory[] Slots;
            internal Spec(SkyIslandLootTier tier, params SkyIslandLootCategory[] slots) { Tier = tier; Slots = slots; }
        }

        private readonly List<Point> points = new List<Point>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly int seed;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> announce;
        private float nextTick;
        private int opened;
        private bool closed, subscribed;

        internal int OpenedCount { get { return opened; } }
        /// <summary>某个搜刮点第一次被打开（参数是标记名 `LOOT_<种类>_<序号>`）。</summary>
        internal event Action<string> Opened;
        internal int PointCount { get { return points.Count; } }

        internal TidegateLoot(TidegateWorld world, TidegateTide tide, int seed, Func<bool> valid, Action<string, bool> announce)
        {
            this.world = world;
            this.tide = tide;
            this.seed = seed;
            this.valid = valid;
            this.announce = announce;
            foreach (Transform marker in world.LootMarkers)
            {
                // LOOT_<种类>_<序号>
                string[] parts = marker.name.Split('_');
                points.Add(new Point { Marker = marker, Id = marker.name, Kind = parts.Length >= 2 ? parts[1] : "crate", Yaw = marker.eulerAngles.y });
            }
            InteractableLootbox.OnStartLoot += OnStartLoot;
            subscribed = true;
        }

        /// <summary>这只箱柜在剧情表里有没有这个固定内容标签。</summary>
        internal static bool HasFixed(string pointId, string tag)
        {
            for (int i = 0; i < StoryContainers.GetLength(0); i++)
                if (StoryContainers[i, 0] == pointId && StoryContainers[i, 1] == tag) return true;
            return false;
        }

        /// <summary>
        /// 四页闸工日志的出处（剧情表里 `page1`～`page4` 的四个箱柜）。
        /// 翻开就算找到（存档权威在 <see cref="TidegateStory.RecordPage"/>），正文进官方笔记图鉴。
        /// </summary>
        internal static int PageIndexOf(string pointId)
        {
            for (int page = 0; page < TidegateStory.PageCount; page++)
                if (HasFixed(pointId, "page" + (page + 1))) return page;
            return -1;
        }

        /// <summary>
        /// 每趟固定放进箱子的潮闸镇物品：船舱储物柜里的阀门手轮、所长保险柜里的潮汐怀表；滩涂埋货有小概率出鸭蹼脚掌。
        /// 怀表只在玩家身上没带着一块时放（带着它进来看潮的人不会再白拿一块 6000 的去卖）。返回 0 表示没有。
        /// </summary>
        internal static int FixedItemOf(string pointId, string kind, System.Random random, bool carryingWatch)
        {
            if (HasFixed(pointId, "tidewatch")) return carryingWatch ? 0 : BossRushItemIds.TidegateTideWatch;
            if (HasFixed(pointId, "wheel")) return BossRushItemIds.TidegateHandwheel;
            if (kind == "buried" && random.Next(100) < FlippersChancePercent) return BossRushItemIds.TidegateFlippers;
            return 0;
        }

        /// <summary>滩涂埋货出鸭蹼脚掌的概率（%）。六个埋货点、只在退潮时够得着，一趟大约三成能见到一双。</summary>
        internal const int FlippersChancePercent = 6;

        /// <summary>
        /// 箱子种类 → 档次与格子类别。镇上生活物资为主；要等退潮或开闸才够得着的（滩涂埋货、沉船、金库）和
        /// 有人守的（保险箱）档次更高，这是「跟着潮水走」的回报。
        /// </summary>
        private static Spec SpecOf(string kind)
        {
            switch (kind)
            {
                case "house": return new Spec(SkyIslandLootTier.Supply, SkyIslandLootCategory.Supplies, SkyIslandLootCategory.General, SkyIslandLootCategory.General);
                case "tool": return new Spec(SkyIslandLootTier.Supply, SkyIslandLootCategory.General, SkyIslandLootCategory.Ammo);
                case "fish": return new Spec(SkyIslandLootTier.Supply, SkyIslandLootCategory.Supplies, SkyIslandLootCategory.General);
                case "med": return new Spec(SkyIslandLootTier.Supply, SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Supplies);
                case "buried": return new Spec(SkyIslandLootTier.Voyage, SkyIslandLootCategory.General, SkyIslandLootCategory.General);
                case "wreck": return new Spec(SkyIslandLootTier.Voyage, SkyIslandLootCategory.Gear, SkyIslandLootCategory.Weapon, SkyIslandLootCategory.Ammo);
                case "safe": return new Spec(SkyIslandLootTier.Voyage, SkyIslandLootCategory.General, SkyIslandLootCategory.Weapon, SkyIslandLootCategory.General);
                case "locker": return new Spec(SkyIslandLootTier.Voyage, SkyIslandLootCategory.General, SkyIslandLootCategory.Gear);
                case "vault": return new Spec(SkyIslandLootTier.Starworks, SkyIslandLootCategory.General, SkyIslandLootCategory.General, SkyIslandLootCategory.General, SkyIslandLootCategory.Gear);
                default: return new Spec(SkyIslandLootTier.Supply, SkyIslandLootCategory.General, SkyIslandLootCategory.Ammo);
            }
        }

        internal void Tick(CharacterMainControl player)
        {
            if (closed || player == null || Time.time < nextTick || valid == null || !valid()) return;
            nextTick = Time.time + TickInterval;
            Vector3 origin = player.transform.position;
            Point nearest = null;
            float best = ActivationRange * ActivationRange;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                float distance = (point.Marker.position - origin).sqrMagnitude;
                if (point.Built && point.Box != null)
                {
                    bool flooded = tide.IsDeepAt(point.Marker.position);
                    if (flooded != point.Flooded && (!flooded || distance > KeepNearRange * KeepNearRange))
                    {
                        point.Flooded = flooded;
                        point.Box.gameObject.SetActive(!flooded);
                        if (!flooded) point.FloodHinted = false;
                    }
                    // 被淹的箱柜：模型还在、交互没了，走到跟前提示一次，水退之前不再说（CR-2026-11-05-024）。
                    if (point.Flooded && !point.FloodHinted && distance <= FloodHintRange * FloodHintRange && announce != null)
                    {
                        point.FloodHinted = true;
                        announce(L10n.T("这只箱子泡在深水里，搜不了，等退潮再来。", "This container is under deep water. Come back at low tide."), false);
                    }
                }
                if (point.Built || point.Failed || distance >= best) continue;
                best = distance;
                nearest = point;
            }
            // 一次只建一个：走进一片新街区时不集中做实例化与物品创建。
            if (nearest != null) Build(nearest, player);
        }

        private void Build(Point point, CharacterMainControl player)
        {
            point.Built = true;
            string error;
            InteractableLootbox box = SkyIslandRewardCrate.Build(world.Root.transform, point.Marker.position, point.Yaw,
                "TidegateLoot_" + point.Id, out error, true);
            if (box == null)
            {
                point.Failed = true;
                Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮点 " + point.Id + " 准备失败：" + error);
                return;
            }
            point.Box = box;
            // 场景包里每个搜刮点本来就摆着自己的箱柜模型（TG_containers）；官方尸体箱的模型藏起来，只留交互与库存。
            foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Spec spec = SpecOf(point.Kind);
            var random = new System.Random(unchecked(seed * 486187739 + SkyIslandLootTables.StableHash(point.Id)));
            var log = new StringBuilder();
            int added = 0;
            foreach (SkyIslandLootCategory category in spec.Slots)
            {
                int typeId = SkyIslandLootPools.PickCategory(category, spec.Tier, random);
                if (typeId == 0) typeId = SkyIslandLootPools.Pick(spec.Tier, false, random);
                int amount = category == SkyIslandLootCategory.Ammo ? 20 + random.Next(31) : 1;
                if (SkyIslandRewardCrate.AddOfficialStack(box, typeId, amount, log, category.ToString())) added++;
            }
            bool carryingWatch = HasFixed(point.Id, "tidewatch") && TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
            int fixedItem = FixedItemOf(point.Id, point.Kind, random, carryingWatch);
            if (fixedItem != 0 && SkyIslandRewardCrate.AddOfficialStack(box, fixedItem, 1, log, "Tidegate")) added++;
            // 沉船船长的保险柜：原型里固定一根小金条（低潮才够得着、涨潮最先淹），正式版从金库档的值钱物里抽一件，同样过价值上限与黑名单。
            if (HasFixed(point.Id, "goldbar"))
            {
                int valuable = SkyIslandLootPools.PickCategory(SkyIslandLootCategory.General, SkyIslandLootTier.Starworks, random);
                if (valuable != 0 && SkyIslandRewardCrate.AddOfficialStack(box, valuable, 1, log, "TidegateWreck")) added++;
            }
            point.Flooded = tide.IsDeepAt(point.Marker.position);
            if (point.Flooded) box.gameObject.SetActive(false);
            Debug.Log(TidegateRaidScene.LogTag + " LOOT_READY id=" + point.Id + " tier=" + spec.Tier + " items=" + added
                + " flooded=" + point.Flooded + " " + log);
        }

        private void OnStartLoot(InteractableLootbox box)
        {
            if (closed || box == null) return;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Box != box || point.Opened) continue;
                point.Opened = true;
                opened++;
                Action<string> handler = Opened;
                if (handler != null) handler(point.Id);
                return;
            }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            if (subscribed) { InteractableLootbox.OnStartLoot -= OnStartLoot; subscribed = false; }
            Opened = null;
            foreach (Point point in points)
            {
                if (point.Box == null) continue;
                // 官方 LootView 仍可能持有引用；先停用再销毁。
                point.Box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(point.Box.gameObject);
                point.Box = null;
            }
            points.Clear();
        }
    }
}
