using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛采集点 owner（内容批次三）。
    ///
    /// 设计口径：
    /// - 30 个采集点只挂在**已有作者标记**旁（<see cref="SkyIslandFieldcraftRules.Nodes"/>），落点复用
    ///   <see cref="SkyIslandRewardCrate.TryFindCratePosition"/>（地面射线 + 墙体胶囊），不动场景几何、不新增导航顶点。
    ///   与全岛其它静态交互体的净空由 `tests/SkyIslandInteractionCompetitionPropertyTest.py` 按真实几何复算。
    /// - 按 AGENTS 4.12 门控：进图时只做 30 次落点射线；交互体在玩家进入 <see cref="SkyIslandFieldcraftRules.ActivationRange"/>
    ///   时才建，一次推进最多建一个，不在进图时预生成整图。
    /// - 按出击刷新、不进存档：每趟每处只采一次，采完即收掉交互体。
    /// - 视觉只用程序化点光与走近才浮现的浮空字（不重打包、不引入新模型）；风晶簇夜里更亮。
    /// </summary>
    internal sealed class SkyIslandGathering : IDisposable
    {
        private sealed class Spot
        {
            internal SkyIslandGatherNode Node;
            internal Vector3 Position;
            internal bool Placed, Built, Harvested;
            internal GameObject Root;
            internal Light Glow;
        }

        private readonly List<Spot> spots = new List<Spot>();
        private readonly Transform root;
        private readonly Func<SkyIslandGatherNode, bool> harvest;
        private bool disposed, glowNight;

        /// <param name="onHarvest">读条走完时调用；返回 true 表示已经发出产出，这一处随即收掉。</param>
        internal SkyIslandGathering(Transform worldRoot, int groundMask, Func<SkyIslandGatherNode, bool> onHarvest)
        {
            if (worldRoot == null) throw new ArgumentNullException("worldRoot");
            root = worldRoot;
            harvest = onHarvest;
            SkyIslandGatherNode[] nodes = SkyIslandFieldcraftRules.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                var spot = new Spot { Node = nodes[i] };
                spots.Add(spot);
                Transform marker = root.Find(nodes[i].Marker);
                Vector3 position;
                // 找不到站得住的地方就这趟不放这一处（fail-open）：绝不退回锚点本身去抢别人的交互。
                if (marker != null && SkyIslandRewardCrate.TryFindCratePosition(root, marker.position, nodes[i].Bearing,
                    nodes[i].Distance, groundMask, out position))
                {
                    spot.Position = position;
                    spot.Placed = true;
                }
            }
            Debug.Log("[SkyIslandGather] NODES placed=" + PlacedCount + "/" + spots.Count);
        }

        internal int PlacedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < spots.Count; i++) if (spots[i].Placed) count++;
                return count;
            }
        }

        internal int HarvestedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < spots.Count; i++) if (spots[i].Harvested) count++;
                return count;
            }
        }

        /// <summary>离 <paramref name="from"/> 最近、这一趟还没采的某种采集点（罗盘在没有别的要找时指向它）。只在按下罗盘时调一次。</summary>
        internal bool TryNearestUnharvested(SkyIslandGatherKind kind, Vector3 from, out Vector3 position)
        {
            position = Vector3.zero;
            if (disposed) return false;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < spots.Count; i++)
            {
                Spot spot = spots[i];
                if (!spot.Placed || spot.Harvested || spot.Node.Kind != kind) continue;
                Vector3 delta = spot.Position - from;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < best) { best = distance; position = spot.Position; found = true; }
            }
            return found;
        }

        /// <summary>由 <see cref="SkyIslandFieldcraft"/> 按推进间隔调用：建最近的一个未建采集点；昼夜切换时改一次光强。</summary>
        internal void Tick(Vector3 origin, bool night)
        {
            if (disposed) return;
            Spot best = null;
            float bestDistance = SkyIslandFieldcraftRules.ActivationRange * SkyIslandFieldcraftRules.ActivationRange;
            for (int i = 0; i < spots.Count; i++)
            {
                Spot spot = spots[i];
                if (!spot.Placed || spot.Built) continue;
                float distance = (spot.Position - origin).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = spot; }
            }
            // 一次只建一个：走进一片新区域时不在同一帧集中创建交互体。
            if (best != null) Build(best, night);
            if (night == glowNight) return;
            glowNight = night;
            for (int i = 0; i < spots.Count; i++)
                if (spots[i].Glow != null) spots[i].Glow.intensity = GlowIntensity(spots[i].Node.Kind, night);
        }

        private void Build(Spot spot, bool night)
        {
            spot.Built = true;
            try
            {
                SkyIslandGatherNode node = spot.Node;
                GameObject go = new GameObject("SkyIslandGather_" + node.Id);
                spot.Root = go;
                go.transform.SetParent(root, false);
                go.transform.position = spot.Position;
                go.layer = LayerMask.NameToLayer("Interactable");
                BoxCollider trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = Vector3.up;
                trigger.size = new Vector3(SkyIslandFieldcraftRules.NodeTriggerSize, 2f, SkyIslandFieldcraftRules.NodeTriggerSize);
                string label = SkyIslandFieldcraftRules.GatherLabel(node.Kind);
                go.AddComponent<SkyIslandGatherPoint>().Bind(label, SkyIslandFieldcraftRules.InteractSeconds(node.Kind),
                    delegate { OnGathered(spot); });

                GameObject glow = new GameObject("GatherGlow");
                glow.transform.SetParent(go.transform, false);
                glow.transform.localPosition = Vector3.up * 0.8f;
                Light light = glow.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = GlowColor(node.Kind);
                light.intensity = GlowIntensity(node.Kind, night);
                light.range = 6f;
                light.shadows = LightShadows.None;

                GameObject sign = new GameObject("Label", typeof(TextMeshPro));
                sign.transform.SetParent(go.transform, false);
                sign.transform.localPosition = Vector3.up * 2.1f;
                sign.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
                TextMeshPro text = sign.GetComponent<TextMeshPro>();
                text.font = ZombieModeUIHelper.GetGameFont();
                text.text = label;
                text.fontSize = 2.4f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = BossRushUIColors.TextPrimary;
                text.rectTransform.sizeDelta = new Vector2(14f, 3f);
                // 字只在走近时浮现，远处只看得到那一点光（口径同纪念物）。
                SkyIslandProximityLabel.Attach(sign, 5f, 10f);

                spot.Root = go;
                spot.Glow = light;
                glowNight = night;
            }
            catch (Exception e)
            {
                if (spot.Root != null) UnityEngine.Object.Destroy(spot.Root);
                spot.Root = null;
                spot.Glow = null;
                Debug.LogWarning("[SkyIslandGather] 采集点 " + spot.Node.Id + " 准备失败：" + e.Message);
            }
        }

        private void OnGathered(Spot spot)
        {
            if (disposed || spot.Harvested || harvest == null) return;
            // 会话此刻无效（返航、死亡、换槽）时不发产出，交互体留着——这一趟本来也就结束了。
            if (!harvest(spot.Node)) return;
            spot.Harvested = true;
            // Destroy 是帧末生效：官方 UpdateInteract 在 OnTimeOut 之后还要走 FinishInteract，本帧对象仍在。
            if (spot.Root != null) UnityEngine.Object.Destroy(spot.Root);
            spot.Root = null;
            spot.Glow = null;
            Debug.Log("[SkyIslandGather] HARVESTED id=" + spot.Node.Id + " total=" + HarvestedCount);
        }

        private static Color GlowColor(SkyIslandGatherKind kind)
        {
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: return new Color(0.72f, 0.95f, 0.55f);
                case SkyIslandGatherKind.Driftwood: return new Color(1f, 0.78f, 0.5f);
                case SkyIslandGatherKind.Moss: return new Color(0.55f, 0.95f, 0.85f);
                case SkyIslandGatherKind.Ore: return new Color(1f, 0.62f, 0.35f);
                default: return new Color(0.62f, 0.9f, 1f);
            }
        }

        /// <summary>夜里整体更亮，风晶簇最亮——「夜里风晶簇更容易出星屑」在场景里看得见。</summary>
        private static float GlowIntensity(SkyIslandGatherKind kind, bool night)
        {
            if (kind == SkyIslandGatherKind.Crystal) return night ? 2f : 1f;
            return night ? 1.3f : 0.8f;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].Root != null) UnityEngine.Object.Destroy(spots[i].Root);
                spots[i].Root = null;
                spots[i].Glow = null;
            }
            spots.Clear();
        }
    }

    /// <summary>采集点的交互体：读条走完即交给 <see cref="SkyIslandGathering"/> 发产出。</summary>
    public sealed class SkyIslandGatherPoint : BossRushBuildingInteractableBase
    {
        private string label;
        private Action gathered;

        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Gather_" + name;
                LocalizationHelper.InjectLocalization(key, label ?? L10n.T("采集", "Gather"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIslandGather] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandGather]"; } }
        protected override float InteractMarkerHeight { get { return 1.4f; } }
        protected override bool IsBuildingInteractable() { return gathered != null; }

        internal void Bind(string title, float seconds, Action action)
        {
            label = title;
            gathered = action;
            // 读条时长是官方 `InteractableBase` 的私有序列化字段，运行时 AddComponent 恒为 0（首帧就完成，见
            // PermanentDuckNpcInteractable 的注释）。复用物品配置的反射写法写进去，再读回公开的 InteractTime 核对：
            // 官方改名时这里「有声」，而不是悄悄变成秒采。
            ModeFItemConfigHelper.SetHiddenMember(this, "interactTime", seconds);
            if (Mathf.Abs(InteractTime - seconds) > 0.01f)
                Debug.LogWarning("[SkyIslandGather] 采集读条时长没有写进官方字段（可能已改名），将变成立即完成：" + name);
        }

        protected override void OnInteractCompleted()
        {
            if (gathered != null) gathered();
        }
    }
}
