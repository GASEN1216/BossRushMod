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
    /// - 视觉只用程序化贴地光斑 + 点光与走近才浮现的浮空字（不重打包、不引入新模型）；风晶簇夜里更亮。
    ///
    /// 【贴地光斑为什么是必需的，不是装饰（CR-2026-09-13-005）】
    ///   设计口径写的是「远处有光、走近浮名字」，但改之前「远处有光」这一半在代码里不存在：
    ///   唯一的远景载体是一盏 <c>range=6m / intensity 0.8（白天）</c> 的**点光源**——
    ///   白天打在被日光照亮的砂岩地面上几乎看不出来；而浮空字 10 m 才开始浮现、5 m 全显
    ///   （<see cref="SkyIslandProximityLabel"/> 的 near/far）。实机一趟真人出击 <c>gathered=1/30</c>。
    ///   默认相机（FOV 20°、俯仰 55°、臂长 45 m）一屏约 28×20 m 地面，所以光斑管的是**屏幕边缘那十几米**：
    ///   字还没浮出来时，靠它先让人看见「那边有东西」。**零每帧开销**：
    ///   躺在地面上不需要 billboard，昼夜只在 <see cref="Tick"/> 的夜晚翻转那一次改颜色。
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
            internal SpriteRenderer GlowDisc;
        }

        /// <summary>
        /// 贴地光斑的直径（米）：够在屏幕边缘认出是一个点，又不至于糊住脚下的地面。
        /// SpriteRenderer 不会把精灵拉到给定尺寸——共享径向图 64 px / PPU 100 原生只有 0.64 m，
        /// 缩放必须按「目标直径 ÷ 精灵原生宽度」算。旧写法直接乘 1.8，实际直径只有 1.15 m（2026-09-14 审核 F-05）。
        /// </summary>
        private const float GlowDiscSize = 1.8f;

        /// <summary>采完那一处的光斑、点光与浮空字淡出的秒数（UE-05）。旧写法当帧 Destroy，光斑和 6 m 点光一起凭空熄灭。</summary>
        private const float HarvestFade = 0.4f;
        /// <summary>风晶簇的光斑：系数 0.45 → 0.35、上限 0.55（UE-05）。旧值夜里 0.9，差不多是一块实心青盘。</summary>
        private const float CrystalDiscFactor = 0.35f;
        private const float CrystalDiscMax = 0.55f;
        private const float DiscFactor = 0.45f;

        private readonly List<Spot> spots = new List<Spot>();
        private readonly Transform root;
        private readonly Func<SkyIslandGatherNode, bool> harvest;
        private bool disposed, glowNight;
        /// <summary>全部光斑共用的一份材质与它的呼吸驱动（<see cref="SkyIslandGatherGlowBreath"/>）；第一次建光斑时才建。</summary>
        private Material discMaterial;
        private GameObject breathDriver;
        private bool discMaterialTried;
        /// <summary>正在淡出的采完的点：本对象销毁时一并收掉（它们用着上面那份共享材质）。</summary>
        private readonly List<GameObject> fading = new List<GameObject>();

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

        /// <summary>已建采集点的浮空字与交互名上一次按哪种语言写的。</summary>
        private bool labelsChinese = L10n.IsChinese;

        /// <summary>玩家在岛上切了语言：已建、还没采的点把浮空字与官方交互名按当前语言重写（语言在取用时解析，AGENTS §4.4）。</summary>
        private void Relabel(bool chinese)
        {
            labelsChinese = chinese;
            for (int i = 0; i < spots.Count; i++)
            {
                Spot spot = spots[i];
                if (spot.Root == null || spot.Harvested) continue;
                string label = SkyIslandFieldcraftRules.GatherLabel(spot.Node.Kind);
                Transform sign = spot.Root.transform.Find("Label");
                TextMeshPro text = sign != null ? sign.GetComponent<TextMeshPro>() : null;
                if (text != null) text.text = label;
                SkyIslandGatherPoint point = spot.Root.GetComponent<SkyIslandGatherPoint>();
                if (point != null) point.Relabel(label);
            }
        }

        /// <summary>由 <see cref="SkyIslandFieldcraft"/> 按推进间隔调用：建最近的一个未建采集点；昼夜切换时改一次光强；切了语言时重写已建点的字。</summary>
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
            bool chinese = L10n.IsChinese;
            if (chinese != labelsChinese) Relabel(chinese);
            if (night == glowNight) return;
            glowNight = night;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].Glow != null) spots[i].Glow.intensity = GlowIntensity(spots[i].Node.Kind, night);
                if (spots[i].GlowDisc != null) spots[i].GlowDisc.color = GlowDiscColor(spots[i].Node.Kind, night);
            }
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

                // 贴地光斑：固定俯视视角下，这是 60 m 外唯一读得出来的「那边有东西」。
                // 躺平在地面上，所以不需要 billboard，也就没有任何每帧工作。
                Sprite disc = SkyIslandUiArt.GetRadialGlow();
                if (disc != null)
                {
                    GameObject discObject = new GameObject("GatherGlowDisc");
                    discObject.transform.SetParent(go.transform, false);
                    discObject.transform.localPosition = Vector3.up * 0.05f;   // 抬离地面，避免 z-fighting
                    // 与 SkyIslandGroundRing 同一个摊平姿态；精灵材质 Cull Off，正反面都画得出来。
                    discObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    // 精灵原生宽度 = 64 px / PPU 100 = 0.64 m，按目标直径反算缩放（见 GlowDiscSize）。
                    discObject.transform.localScale = Vector3.one * (GlowDiscSize / Mathf.Max(0.01f, disc.bounds.size.x));
                    SpriteRenderer renderer = discObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = disc;
                    // 共用一份材质：呼吸只写这一份的 _Color，每帧 O(1)（UE-05）。建不出来就留默认精灵材质，只是不呼吸。
                    Material breathing = DiscMaterial();
                    if (breathing != null) renderer.sharedMaterial = breathing;
                    renderer.color = GlowDiscColor(node.Kind, night);
                    renderer.sortingOrder = SkyIslandGroundRing.SortingOrder;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    spot.GlowDisc = renderer;
                }

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
                // 压在亮地面上的世界字要有描边托住（UE-14），共享材质按字体一份。
                Material outlined = BossRushUIKit.GetOutlinedFontMaterial(text.font);
                if (outlined != null) text.fontSharedMaterial = outlined;
                // 字只在走近时浮现，远处只看得到那一点光（口径同纪念物）。
                SkyIslandProximityLabel.Attach(sign, 5f, 10f);

                spot.Root = go;
                spot.Glow = light;
                // 全局昼夜标记只由 Tick 在全部已建点刷新后维护。
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
            GameObject gathered = spot.Root;
            spot.Root = null;
            spot.Glow = null;
            spot.GlowDisc = null;
            if (gathered != null)
            {
                // 交互体与触发器本帧就摘掉（Destroy 是帧末生效：官方 UpdateInteract 在 OnTimeOut 之后还要走 FinishInteract，
                // 本帧组件仍在）；光斑、点光与字交给 0.4 秒的淡出再销毁根（UE-05）。改名：F3 按 SkyIslandGather_<id> 找「还在不在」。
                SkyIslandGatherPoint point = gathered.GetComponent<SkyIslandGatherPoint>();
                if (point != null) UnityEngine.Object.Destroy(point);
                BoxCollider trigger = gathered.GetComponent<BoxCollider>();
                if (trigger != null) UnityEngine.Object.Destroy(trigger);
                gathered.name = "SkyIslandGatherFading";
                fading.RemoveAll(go => go == null);
                fading.Add(gathered);
                SkyIslandFadeAway.Begin(gathered, HarvestFade, Vector3.zero);
            }
            Debug.Log("[SkyIslandGather] HARVESTED id=" + spot.Node.Id + " total=" + HarvestedCount);
        }

        /// <summary>全部光斑共用的 Sprites/Default 材质，连同驱动它呼吸的那一个组件一起建（每个出击一份，随本对象销毁）。</summary>
        private Material DiscMaterial()
        {
            if (discMaterialTried) return discMaterial;
            discMaterialTried = true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            discMaterial = new Material(shader);
            discMaterial.name = "SkyIslandGatherGlowDisc";
            breathDriver = new GameObject("SkyIslandGatherGlowBreath");
            breathDriver.transform.SetParent(root, false);
            breathDriver.AddComponent<SkyIslandGatherGlowBreath>().Material = discMaterial;
            return discMaterial;
        }

        /// <summary>
        /// 光斑颜色 = 该资源的光色 + 按昼夜定的不透明度。白天要压住，不然满岛都是亮点。
        /// 风晶簇是「魔法物」，保留冷色，但系数与上限都压一档（夜里不再是一块实心青盘）。
        /// </summary>
        private static Color GlowDiscColor(SkyIslandGatherKind kind, bool night)
        {
            Color color = GlowColor(kind);
            color.a = kind == SkyIslandGatherKind.Crystal
                ? Mathf.Min(CrystalDiscMax, GlowIntensity(kind, night) * CrystalDiscFactor)
                : Mathf.Clamp01(GlowIntensity(kind, night) * DiscFactor);
            return color;
        }

        /// <summary>
        /// 光色。草与苔收进暖色基准（UE-05）：旧值是高饱和的荧光绿、薄荷绿，在暖砂岩地面上读作「地上贴了张彩色贴纸」。
        /// 浮木、矿本来就在暖琥珀带里，不动（F3 白天光斑可见度按浮木 A1 / A2 实测过）；风晶簇保留冷青。
        /// </summary>
        private static Color GlowColor(SkyIslandGatherKind kind)
        {
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: return new Color(0.80f, 0.86f, 0.52f);
                case SkyIslandGatherKind.Driftwood: return new Color(1f, 0.78f, 0.5f);
                case SkyIslandGatherKind.Moss: return new Color(0.62f, 0.82f, 0.70f);
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
            for (int i = 0; i < fading.Count; i++)
                if (fading[i] != null) UnityEngine.Object.Destroy(fading[i]);
            fading.Clear();
            if (breathDriver != null) UnityEngine.Object.Destroy(breathDriver);
            breathDriver = null;
            // 运行时 new 的材质要显式销毁；用着它的光斑（含淡出中的）上面都已经一起销毁。
            if (discMaterial != null) UnityEngine.Object.Destroy(discMaterial);
            discMaterial = null;
        }
    }

    /// <summary>
    /// 采集光斑的呼吸（UE-05）：全部光斑共用一份材质，这里每帧只写一次它的 <c>_Color</c>（乘在各自的顶点色上）——
    /// 30 处光斑同相呼吸，成本 O(1)，不给每个点挂 Update（AGENTS §4.12）。区间 0.85–1、周期 2.8 秒：看得出是活的资源点，
    /// 又不削弱屏幕边缘的可见度（F3 白天 11 m 光斑判据按色度偏移判，低谷只少 15%）。
    /// 走游戏时间：暂停菜单与模态面板（timeScale = 0）时停住。
    /// </summary>
    internal sealed class SkyIslandGatherGlowBreath : MonoBehaviour
    {
        private const float Period = 2.8f;
        private const float Low = 0.85f;
        internal Material Material;
        private float age;

        private void LateUpdate()
        {
            if (Material == null) return;
            age += Time.deltaTime;
            float wave = 0.5f + 0.5f * Mathf.Sin(age * (2f * Mathf.PI / Period));
            Material.color = new Color(1f, 1f, 1f, Mathf.Lerp(Low, 1f, wave));
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

        /// <summary>切了语言：换交互名，读条与回调不动（<see cref="InteractNameKey"/> 每次取用时重注入）。</summary>
        internal void Relabel(string title) { label = title; }

        protected override void OnInteractCompleted()
        {
            if (gathered != null) gathered();
        }
    }
}
