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
        /// <summary>玩家贴着箱子时不收也不放：官方搜刮界面可能正开着。</summary>
        private const float KeepNearRange = 4f;

        private sealed class Point
        {
            internal Transform Marker;
            internal string Id, Kind;
            internal float Yaw;
            internal InteractableLootbox Box;
            internal bool Built, Failed, Flooded, Opened;
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
        private float nextTick;
        private int opened;
        private bool closed, subscribed;

        internal int OpenedCount { get { return opened; } }
        /// <summary>某个搜刮点第一次被打开（参数是标记名 `LOOT_<种类>_<序号>`）。</summary>
        internal event Action<string> Opened;
        internal int PointCount { get { return points.Count; } }

        internal TidegateLoot(TidegateWorld world, TidegateTide tide, int seed, Func<bool> valid)
        {
            this.world = world;
            this.tide = tide;
            this.seed = seed;
            this.valid = valid;
            foreach (Transform marker in world.LootMarkers)
            {
                // LOOT_<种类>_<序号>
                string[] parts = marker.name.Split('_');
                points.Add(new Point { Marker = marker, Id = marker.name, Kind = parts.Length >= 2 ? parts[1] : "crate", Yaw = marker.eulerAngles.y });
            }
            InteractableLootbox.OnStartLoot += OnStartLoot;
            subscribed = true;
        }

        /// <summary>
        /// 箱子种类 → 档次与格子类别。镇上生活物资为主；要等退潮或开闸才够得着的（滩涂埋货、沉船、金库）和
        /// 有人守的（保险箱）档次更高，这是「跟着潮水走」的回报。
        /// </summary>
        /// <summary>
        /// 四页闸工日志的出处（场景包里固定的四个箱柜，名字来自 three.js 原型 layout.js 的 fixed 字段）。
        /// 翻开就算找到（存档权威在 <see cref="TidegateStory.RecordPage"/>），正文进官方笔记图鉴。
        /// </summary>
        internal static int PageIndexOf(string pointId)
        {
            switch (pointId)
            {
                case "LOOT_safe_68": return 0;   // 第三水厂控制室保险箱
                case "LOOT_house_69": return 1;  // 镇区泵站值班桌
                case "LOOT_house_70": return 2;  // 潮位站记录柜
                case "LOOT_vault_71": return 3;  // 储蓄所所长的保险柜
                default: return -1;
            }
        }

        /// <summary>
        /// 每趟固定放进箱子的潮闸镇物品：船舱储物柜里的阀门手轮、所长保险柜里的潮汐怀表；滩涂埋货有小概率出鸭蹼脚掌。
        /// 返回 0 表示没有。
        /// </summary>
        internal static int FixedItemOf(string pointId, string kind, System.Random random)
        {
            if (pointId == "LOOT_vault_71") return BossRushItemIds.TidegateTideWatch;
            if (kind == "locker") return BossRushItemIds.TidegateHandwheel;
            if (kind == "buried" && random.Next(100) < FlippersChancePercent) return BossRushItemIds.TidegateFlippers;
            return 0;
        }

        /// <summary>滩涂埋货出鸭蹼脚掌的概率（%）。六个埋货点、只在退潮时够得着，一趟大约三成能见到一双。</summary>
        internal const int FlippersChancePercent = 6;

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
                if (point.Built && point.Box != null && distance > KeepNearRange * KeepNearRange)
                {
                    bool flooded = tide.IsDeepAt(point.Marker.position);
                    if (flooded != point.Flooded)
                    {
                        point.Flooded = flooded;
                        point.Box.gameObject.SetActive(!flooded);
                    }
                }
                if (point.Built || point.Failed || distance >= best) continue;
                best = distance;
                nearest = point;
            }
            // 一次只建一个：走进一片新街区时不集中做实例化与物品创建。
            if (nearest != null) Build(nearest);
        }

        private void Build(Point point)
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
            int fixedItem = FixedItemOf(point.Id, point.Kind, random);
            if (fixedItem != 0 && SkyIslandRewardCrate.AddOfficialStack(box, fixedItem, 1, log, "Tidegate")) added++;
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
