using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的水体与游泳（复用官方游泳，不自写）：官方 `CharacterMainControl.CheckInWater` 只认「Water」层碰撞体，
    /// 水面高度取碰撞体包围盒顶；`CanSwim` 属性 &gt; 0.01 才浮上水面游，否则下沉、头没进水后官方缺氧 Buff 掉血。
    /// 这里按真实泻湖遮罩合并一组互不重叠的 Water 层触发盒，海与湖各自共用父节点，顶面跟着对应水位走；
    /// 再按负重给主角挂 / 摘 `CanSwim`：轻装能游，背重了下不了深水，被涨潮困住会往下沉——原型的核心规则。
    /// 敌人不检测入水（官方只给主角和受控角色测），由潮汐 owner 封掉被淹的导航三角形。
    /// </summary>
    internal sealed partial class TidegateWater : IDisposable
    {
        /// <summary>空手能游的负重上限（千克，原型 14）。</summary>
        internal const float SwimWeightLimit = 14f;
        /// <summary>身上带着防水袋时多出的负重上限（千克，原型 +6）。</summary>
        internal const float DryBagBonus = 6f;
        /// <summary>带着鸭蹼脚掌在水里时的移速加成（PercentageAdd，官方游泳本身是 ×0.8）。</summary>
        internal const float FlippersSpeedBonus = 0.35f;
        /// <summary>
        /// 游泳时敌人发现距离减半（原型规则，CR-2026-11-05-026）：官方 AI 索敌按目标的 `VisableDistanceFactor` 缩放视距
        /// （`AIMainBrain`：距离 ≤ searchDistance × 系数才算看见）。PercentageMultiply -0.5 = 最终值 ×0.5，不受装备加减影响。
        /// </summary>
        internal const float SwimVisibilityMultiplier = -0.5f;
        private const float BoxBottom = -14f;
        private const float CheckInterval = 0.5f;
        private const string CanSwimStat = "CanSwim", VisibilityStat = "VisableDistanceFactor";

        private readonly List<BoxCollider> seaBoxes = new List<BoxCollider>();
        private readonly List<BoxCollider> lagoonBoxes = new List<BoxCollider>();
        private readonly List<BossRushStatModifierRecord> swimRecords = new List<BossRushStatModifierRecord>();
        private readonly List<BossRushStatModifierRecord> flipperRecords = new List<BossRushStatModifierRecord>();
        private readonly List<BossRushStatModifierRecord> stealthRecords = new List<BossRushStatModifierRecord>();
        private readonly object modifierSource = new object();
        private readonly TidegateTide tide;
        private readonly CharacterMainControl player;
        private readonly Func<bool> valid;
        private GameObject root;
        private Transform seaVolume, lagoonVolume;
        private float appliedSea = float.NaN, appliedLagoon = float.NaN, nextCheck;
        private bool swimGranted, flippersOn, stealthOn, warnedStat, warnedStealth, disposed, hasDryBag;

        internal bool CanSwimNow { get { return swimGranted; } }
        // 抓缆以及最终离岸时重查一次：玩家可能刚装上防水袋，也可能刚把重物拾回来。
        internal bool CheckBoardingLoad()
        {
            if (disposed || player == null || player.CharacterItem == null || !valid()) return false;
            ApplySwim();
            return swimGranted;
        }
        /// <summary>正在游泳、敌人发现距离减半中（HUD 小标用）。</summary>
        internal bool Stealthy { get { return stealthOn; } }
        internal float WeightLimit { get { return SwimWeightLimit + (hasDryBag ? DryBagBonus : 0f); } }

        internal TidegateWater(TidegateWorld world, TidegateTide tide, CharacterMainControl player, Func<bool> valid)
        {
            this.tide = tide;
            this.player = player;
            this.valid = valid;
            int layer = LayerMask.NameToLayer("Water");
            if (layer < 0) throw new InvalidOperationException("官方 Water 层缺失");
            root = new GameObject("TidegateWaterVolumes");
            root.transform.SetParent(world.Root.transform, false);
            root.layer = layer;
            seaVolume = Volume("Sea", layer);
            lagoonVolume = Volume("Lagoon", layer);
            int samples;
            foreach (TidegateWaterRegion region in world.CreateWaterRegions(out samples))
            {
                List<BoxCollider> boxes = region.Lagoon ? lagoonBoxes : seaBoxes;
                boxes.Add(Box(region.Lagoon ? lagoonVolume : seaVolume, region));
            }
            Debug.Log(TidegateRaidScene.LogTag + " WATER_REGIONS sea=" + seaBoxes.Count + " lagoon=" + lagoonBoxes.Count + " samples=" + samples);
            Tick();
        }

        private Transform Volume(string name, int layer)
        {
            var go = new GameObject("WaterVolume_" + name);
            go.layer = layer;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * BoxBottom;
            return go.transform;
        }

        private static BoxCollider Box(Transform volume, TidegateWaterRegion region)
        {
            // 固定 XZ 只需 collider.center，同水位的所有盒共用一个对象，不为每片矩形再建 Transform。
            BoxCollider box = volume.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(region.MaxX - region.MinX, 1f, region.MaxZ - region.MinZ);
            box.center = new Vector3((region.MinX + region.MaxX) * 0.5f, 0.5f, (region.MinZ + region.MaxZ) * 0.5f);
            return box;
        }

        private static void SetTop(Transform volume, float top)
        {
            // 同水位的盒子共用父级 Y 缩放：不逐盒写 size / center，XZ 分界不动。
            volume.localScale = new Vector3(1f, Mathf.Max(0.1f, top - BoxBottom), 1f);
        }

        internal void Tick()
        {
            if (disposed || tide == null) return;
            float sea = tide.Level, lagoon = tide.LagoonLevel;
            if (float.IsNaN(appliedSea) || Math.Abs(sea - appliedSea) > 0.005f)
            {
                appliedSea = sea;
                SetTop(seaVolume, sea);
            }
            if (float.IsNaN(appliedLagoon) || Math.Abs(lagoon - appliedLagoon) > 0.005f)
            {
                appliedLagoon = lagoon;
                SetTop(lagoonVolume, lagoon);
            }
            if (Time.unscaledTime < nextCheck || player == null || valid == null || !valid()) return;
            nextCheck = Time.unscaledTime + CheckInterval;
            ApplySwim();
        }

        /// <summary>负重不超上限才给游泳能力。负重跨线才增删修饰，平时 O(1)。</summary>
        private void ApplySwim()
        {
            Item item = player.CharacterItem;
            if (item == null) return;
            hasDryBag = TidegateInventory.Has(player, BossRushItemIds.TidegateDryBag);
            ApplyFlippers(player.Swimming && TidegateInventory.Has(player, BossRushItemIds.TidegateFlippers));
            ApplyStealth(player.Swimming);
            bool light = item.TotalWeight <= WeightLimit;
#if BOSSRUSH_DEV
            // Dev 演练（TidegateDrillHooks.cs）：不发物品也能把轻装 / 超重两头都演一遍。
            if (DevSwimOverride != 0) light = DevSwimOverride == 1;
#endif
            if (light == swimGranted) return;
            if (light)
            {
                swimGranted = RuntimeStatModifierTracker.TryAdd(player, CanSwimStat, 1f, modifierSource, swimRecords, "TidegateSwim",
                    ModifierType.Add);
                if (!swimGranted && !warnedStat)
                {
                    warnedStat = true;
                    Debug.LogWarning(TidegateRaidScene.LogTag + " 主角没有 CanSwim 属性，潮闸镇按「不能游」处理");
                }
                return;
            }
            RuntimeStatModifierTracker.RemoveAll(swimRecords, "TidegateSwim");
            swimGranted = false;
        }

        /// <summary>鸭蹼：在水里才加速，上岸就摘，跨状态才增删修饰。</summary>
        private void ApplyFlippers(bool on)
        {
            if (on == flippersOn) return;
            flippersOn = on;
            if (on)
            {
                RuntimeStatModifierTracker.TryAdd(player, "WalkSpeed", FlippersSpeedBonus, modifierSource, flipperRecords, "TidegateFlippers");
                RuntimeStatModifierTracker.TryAdd(player, "RunSpeed", FlippersSpeedBonus, modifierSource, flipperRecords, "TidegateFlippers");
                return;
            }
            RuntimeStatModifierTracker.RemoveAll(flipperRecords, "TidegateFlippers");
        }

        /// <summary>游泳时敌人发现距离减半，上岸就摘；跨状态才增删修饰。主角没有这项属性时按「不减」处理、只警告一次。</summary>
        private void ApplyStealth(bool on)
        {
            if (on == stealthOn) return;
            if (on)
            {
                stealthOn = RuntimeStatModifierTracker.TryAdd(player, VisibilityStat, SwimVisibilityMultiplier, modifierSource, stealthRecords,
                    "TidegateSwimStealth", ModifierType.PercentageMultiply);
                if (!stealthOn && !warnedStealth)
                {
                    warnedStealth = true;
                    Debug.LogWarning(TidegateRaidScene.LogTag + " 主角没有 VisableDistanceFactor 属性，游泳时敌人发现距离不减半");
                }
                return;
            }
            RuntimeStatModifierTracker.RemoveAll(stealthRecords, "TidegateSwimStealth");
            stealthOn = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RuntimeStatModifierTracker.RemoveAll(flipperRecords, "TidegateFlippers");
            flippersOn = false;
            RuntimeStatModifierTracker.RemoveAll(stealthRecords, "TidegateSwimStealth");
            stealthOn = false;
            // 游泳能力只属于这一趟：离开潮闸镇必须摘掉，否则跟着主角回基地。
            RuntimeStatModifierTracker.RemoveAll(swimRecords, "TidegateSwim");
            swimGranted = false;
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            seaBoxes.Clear();
            lagoonBoxes.Clear();
            seaVolume = lagoonVolume = null;
        }
    }
}
