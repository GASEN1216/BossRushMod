// ============================================================================
// CollectibleSlotFrame.cs - 6–8 档收集品的格子边框特效（鎏金框 + 档次色内辉，越高越金）
// ============================================================================
// owner 2026-11-19 问答拍板：
//   - 只有 6、7、8 档有，档次越高越华丽：
//       6 档：鎏金框、四角角花、内辉（慢呼吸）、定时扫光、一道绕边流光；
//       7 档：再加外圈呼吸辉光、边上星点闪烁；
//       8 档：再加第二道对向流光、更密的星点、顶边徽记，扫光更勤。
//   - 配色：金框与角花统一鎏金，是「收藏级」的身份；框内那圈柔光 6 档用官方 6 档红，7 档赤金，8 档耀金
//     （owner 追加：金色才是最高级，紫色不够高端；见 TierGlow）。
//   - 范围：所有官方物品格（背包、箱子、仓库、商店、装备槽……都经 ItemDisplay.Setup，见 CollectibleSlotFramePatch）
//     + 收藏册界面里已登记的卡片。收藏册（500219）本身不加。
//   - 开箱出货：这件收集品第一次在格子里露面（搜出来的那一刻）放一次光环扩散，同一件只放一次。
//
// 层级：官方 ItemDisplay 的子物体依次是 BG、Frame、SelectionIndicator、Icon、……、Layout（2026-11-19 UnityPy 读
// resources.assets 的预制体）。金框这一层插在 SelectionIndicator 之前，选中高亮仍压在金框上，选没选中照样看得清；
// 扫光、闪光与出货光环这一层插在 Icon 之后、角标与名字之前。两层根物体都铺满格子、不吃点击、不参与布局。
// 待搜（NeedInspection）时整层收起，搜出来再显示——出货演出就挂在这一刻。
//
// 性能（AGENTS.md §4.12）：格子里不是 6–8 档收集品时，组件所在的根物体是停用的，没有任何每帧成本；
// 补丁对非收集品只做一次按下标取表和一次字典查找。显示时每件每帧只改十几个小图元的位置与透明度，
// 走 unscaled 时间，官方暂停菜单打开时停推进。贴图全部程序化烘焙（CollectibleSlotFrameArt）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class CollectibleSlotFrame : MonoBehaviour
    {
        private const string UnderRootName = "BossRush_CollectibleFrame";
        private const string OverRootName = "BossRush_CollectibleFrameGleam";
        /// <summary>官方格子的金框往里收 1 单位，压住官方底框的外沿；收藏册卡片收 3 单位，留出卡片描边做外圈。</summary>
        private const float SlotInset = 1f;
        private const float CardInset = 3f;
        private const float CornerOverhang = 2f;
        private const int TrailLength = 7;
        private const float TrailSpacing = 4.5f;
        private const float SheenSweepSeconds = 0.9f;
        private const float SheenTilt = -24f;
        private const float BurstFlashSeconds = 0.35f;
        private const float BurstRingSeconds = 0.6f;
        private const float BurstRingStagger = 0.12f;
        private const float MaxStep = 0.1f;
        private const float TwoPi = Mathf.PI * 2f;

        private static readonly Color GlintHead = new Color(1f, 0.93f, 0.72f, 1f);
        private static readonly Color SparkColor = new Color(1f, 0.96f, 0.85f, 1f);
        private static readonly Color SheenColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color RingGold = new Color(1f, 0.85f, 0.45f, 1f);
        /// <summary>收藏册卡片描边换成暗铜色，做金框外面那一圈。</summary>
        private static readonly Color CardStrokeColor = new Color(0.55f, 0.40f, 0.16f, 0.9f);

        private sealed class Look
        {
            internal float InnerMin, InnerMax, BreathHz, OuterMin, OuterMax, SheenPeriod, SheenAlpha, LapSeconds, SparkGap;
            internal int Comets, Sparks, BurstRings;
            internal bool Crest;
        }

        /// <summary>下标 = 边框档（1 = 6 档、2 = 7 档、3 = 8 档）。</summary>
        private static readonly Look[] Looks =
        {
            null,
            new Look { InnerMin = 0.42f, InnerMax = 0.62f, BreathHz = 0.35f, OuterMin = 0f, OuterMax = 0f, SheenPeriod = 5.2f, SheenAlpha = 0.30f,
                LapSeconds = 3.6f, SparkGap = 1f, Comets = 1, Sparks = 0, BurstRings = 1, Crest = false },
            new Look { InnerMin = 0.48f, InnerMax = 0.70f, BreathHz = 0.45f, OuterMin = 0.26f, OuterMax = 0.46f, SheenPeriod = 4.2f, SheenAlpha = 0.34f,
                LapSeconds = 3.0f, SparkGap = 1f, Comets = 1, Sparks = 3, BurstRings = 2, Crest = false },
            new Look { InnerMin = 0.52f, InnerMax = 0.78f, BreathHz = 0.55f, OuterMin = 0.34f, OuterMax = 0.60f, SheenPeriod = 3.2f, SheenAlpha = 0.38f,
                LapSeconds = 2.6f, SparkGap = 0.7f, Comets = 2, Sparks = 5, BurstRings = 3, Crest = true },
        };

        /// <summary>
        /// 框内柔光、外辉、流光尾巴与出货光环的主色，下标 = 边框档。owner 2026-11-19：金色才是最高级，档次越高越金。
        ///   6 档：官方 6 档光晕红 #ff2e2e，和物品本身的官方光晕一致；
        ///   7 档：琥珀金（比官方 5 档橙 #ff7d25 偏黄，免得和没边框的 5 档撞色）；8 档：耀金，最亮。
        /// 官方品质光晕表（2026-11-19 按字节读 resources.assets 的 UIStyle.displayQualityLooks）只到 6 档，
        /// 7、8 档走官方默认的黑色投影（0,0,0,0.72），没有颜色可跟，所以三档都在这里写死，不在运行时查官方样式。
        /// </summary>
        private static readonly Color[] TierGlow =
        {
            new Color(0f, 0f, 0f, 0f),
            new Color(1f, 0.18f, 0.18f, 1f),
            new Color(1f, 0.68f, 0.22f, 1f),
            new Color(1f, 0.86f, 0.36f, 1f),
        };

        private static readonly Dictionary<int, CollectibleSlotFrame> liveByDisplay = new Dictionary<int, CollectibleSlotFrame>();
        private static readonly HashSet<int> freshDrops = new HashSet<int>();

        private ItemDisplay display;
        private int displayId;
        private Item target;
        private RectTransform underRoot, overRoot, underContent, overContent, sheenMask;
        private float inset;
        private int builtTier;
        private Look look;
        private Color qualityColor;
        private Image outerGlow, innerGlow, frame, crest, sheen, flash;
        private readonly Image[] corners = new Image[4];
        private Image[] trail, sparks, rings;
        private float[] sparkStart, sparkDuration;
        private bool[] sparkIdle;
        private System.Random random;
        private float clock, phase, sheenOffset, sheenTravel, burstClock = -1f;
        private bool contentShown, sheenParked;
        private Vector2 laidOutSize;
        private float pathHalfW, pathHalfH, pathRadius, pathLength, sparkSize;

        // ====================================================================
        // 对外入口
        // ====================================================================

        /// <summary>收集品品质 → 边框档：6、7、8 档各一级，其余没有。</summary>
        internal static int FrameTier(int quality)
        {
            if (quality >= 8) return 3;
            if (quality == 7) return 2;
            return quality == 6 ? 1 : 0;
        }

        internal static bool HasFrame(int quality)
        {
            return FrameTier(quality) > 0;
        }

        /// <summary>ItemDisplay.Setup 的后置补丁调用：是 6–8 档收集品就显示边框，否则收起这个格子上已有的边框。</summary>
        internal static void BindItemDisplay(ItemDisplay display, Item item, Image icon, GameObject selectionIndicator)
        {
            if (display == null) return;
            int id = display.GetInstanceID();
            CollectibleSlotFrame frame;
            if (liveByDisplay.TryGetValue(id, out frame) && frame == null)
            {
                liveByDisplay.Remove(id);
                frame = null;
            }
            int quality = QualityOf(item);
            if (quality <= 0)
            {
                if (frame != null) frame.Hide();
                return;
            }
            if (frame == null)
            {
                frame = Create(display.transform, selectionIndicator != null ? selectionIndicator.transform : null,
                    icon != null ? icon.transform : null, SlotInset);
                frame.display = display;
                frame.displayId = id;
                liveByDisplay[id] = frame;
            }
            frame.Show(item, quality);
        }

        /// <summary>收藏册里已登记的 6–8 档卡片：金框插在图标之下，扫光在图标之上；卡片描边换暗铜色做外圈。</summary>
        internal static void AttachToCard(RectTransform card, int quality, Transform icon, Image stroke)
        {
            if (card == null || !HasFrame(quality) || CollectiblesRuntimeModule.Current == null) return;
            try
            {
                Create(card, icon, icon, CardInset).Show(null, quality);
                if (stroke != null) stroke.color = CardStrokeColor;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收藏册卡片边框失败: " + e.Message);
            }
        }

        /// <summary>开箱交付的那一件：它第一次在格子里露面时放出货演出。</summary>
        internal static void MarkFreshDrop(Item item)
        {
            if (QualityOf(item) > 0) freshDrops.Add(item.GetInstanceID());
        }

        /// <summary>过图清掉没来得及露面的出货标记（实例 ID 不跨场景复用，留着只会越攒越多）。</summary>
        internal static void ClearFreshDrops()
        {
            freshDrops.Clear();
        }

        internal static void ResetStaticCaches()
        {
            var frames = new List<CollectibleSlotFrame>(liveByDisplay.Values);
            liveByDisplay.Clear();
            for (int i = 0; i < frames.Count; i++)
            {
                try
                {
                    CollectibleSlotFrame frame = frames[i];
                    if (frame == null) continue;
                    if (frame.overRoot != null) Destroy(frame.overRoot.gameObject);
                    Destroy(frame.gameObject);
                }
                catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            }
            freshDrops.Clear();
            CollectibleSlotFrameArt.ResetStaticCaches();
        }

        private static int QualityOf(Item item)
        {
            if (item == null || CollectiblesRuntimeModule.Current == null) return 0;
            CollectibleDefinition def = CollectibleCatalog.Get(item.TypeID);
            return def != null && HasFrame(def.Quality) ? def.Quality : 0;
        }

        // ====================================================================
        // 创建与绑定
        // ====================================================================

        /// <summary>在 host 下建两层根：金框层插在 drawBefore 之前，扫光层插在 drawAfter 之后（都按 host 的直接子物体算）。</summary>
        private static CollectibleSlotFrame Create(Transform host, Transform drawBefore, Transform drawAfter, float inset)
        {
            RectTransform under = NewRoot(UnderRootName, host);
            RectTransform over = NewRoot(OverRootName, host);
            Transform before = TopChild(host, drawBefore);
            Transform after = TopChild(host, drawAfter);
            if (before != null) under.SetSiblingIndex(before.GetSiblingIndex());
            else if (after != null) under.SetSiblingIndex(after.GetSiblingIndex());
            if (after != null) over.SetSiblingIndex(after.GetSiblingIndex() + 1);
            CollectibleSlotFrame frame = under.gameObject.AddComponent<CollectibleSlotFrame>();
            frame.underRoot = under;
            frame.overRoot = over;
            frame.inset = inset;
            frame.random = new System.Random(under.GetInstanceID());
            return frame;
        }

        private void Show(Item item, int quality)
        {
            target = item;
            int tier = FrameTier(quality);
            qualityColor = TierGlow[tier];
            if (tier != builtTier) Build(tier);
            underRoot.gameObject.SetActive(true);
            overRoot.gameObject.SetActive(true);
            phase = (float)random.NextDouble() * 10f;
            sheenOffset = (float)random.NextDouble() * look.SheenPeriod;
            StopBurst();
            bool shown = IsContentVisible();
            SetContentShown(shown);
            if (shown) TryPlayFreshBurst();
        }

        private void Hide()
        {
            target = null;
            StopBurst();
            if (underRoot != null) underRoot.gameObject.SetActive(false);
            if (overRoot != null) overRoot.gameObject.SetActive(false);
        }

        private bool IsContentVisible()
        {
            // 收藏册卡片没有物品实例，常显；官方格子里待搜的物品先收起，搜出来再亮。
            return target == null || !target.NeedInspection;
        }

        private void SetContentShown(bool shown)
        {
            contentShown = shown;
            if (underContent != null) underContent.gameObject.SetActive(shown);
            if (overContent != null) overContent.gameObject.SetActive(shown);
        }

        private void TryPlayFreshBurst()
        {
            if (target != null && freshDrops.Remove(target.GetInstanceID())) StartBurst();
        }

        private void OnDestroy()
        {
            if (overRoot != null) Destroy(overRoot.gameObject);
            CollectibleSlotFrame live;
            if (displayId != 0 && liveByDisplay.TryGetValue(displayId, out live) && ReferenceEquals(live, this)) liveByDisplay.Remove(displayId);
        }

        // ====================================================================
        // 每帧
        // ====================================================================

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 格子边框更新异常，已收起: " + e.Message);
                Hide();
            }
        }

        private void Tick()
        {
            if (CollectiblesRuntimeModule.Current == null) { Hide(); return; }
            // 官方回收格子（NotifyReleased）不走 Setup：发现格子换了物品或空了就收起。
            if (display != null && (target == null || display.Target != target)) { Hide(); return; }
            if (BossRushUI.IsGamePaused()) return;
            bool shown = IsContentVisible();
            if (shown != contentShown)
            {
                SetContentShown(shown);
                if (shown) TryPlayFreshBurst();
            }
            if (!shown || look == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            clock += dt;
            if (!EnsureLayout()) return;
            Animate(dt);
        }

        private void Animate(float dt)
        {
            float breath = 0.5f + 0.5f * Mathf.Sin((clock + phase) * look.BreathHz * TwoPi);
            float boost = burstClock >= 0f ? 1f - BossRushUI.EaseOut(burstClock / BurstRingSeconds) : 0f;
            SetAlpha(innerGlow, Mathf.Lerp(look.InnerMin, look.InnerMax, breath) + boost * 0.4f);
            if (outerGlow != null) SetAlpha(outerGlow, Mathf.Lerp(look.OuterMin, look.OuterMax, breath) + boost * 0.4f);
            if (crest != null) SetAlpha(crest, 0.82f + 0.18f * breath);

            float lap = (clock + phase) / look.LapSeconds;
            for (int c = 0; c < look.Comets; c++)
            {
                float head = lap + (float)c / look.Comets;
                for (int j = 0; j < TrailLength; j++)
                {
                    Image dot = trail[c * TrailLength + j];
                    if (dot != null) dot.rectTransform.anchoredPosition = PathPoint(head - j * TrailSpacing / pathLength);
                }
            }

            AnimateSheen();
            AnimateSparks();
            AnimateBurst(dt);
        }

        private void AnimateSheen()
        {
            if (sheen == null) return;
            float local = Mathf.Repeat(clock + sheenOffset, look.SheenPeriod);
            if (local < SheenSweepSeconds)
            {
                float p = BossRushUI.EaseOut(local / SheenSweepSeconds);
                sheen.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-sheenTravel, sheenTravel, p), 0f);
                sheenParked = false;
            }
            else if (!sheenParked)
            {
                // 停在遮罩外面：RectMask2D 直接剔掉，两次扫光之间不画、也不再改顶点。
                sheen.rectTransform.anchoredPosition = new Vector2(-sheenTravel * 1.5f, 0f);
                sheenParked = true;
            }
        }

        private void AnimateSparks()
        {
            if (sparks == null) return;
            for (int i = 0; i < sparks.Length; i++)
            {
                Image spark = sparks[i];
                if (spark == null) continue;
                if (clock >= sparkStart[i] + sparkDuration[i]) ScheduleSpark(i);
                float local = clock - sparkStart[i];
                if (local < 0f)
                {
                    if (!sparkIdle[i]) { spark.rectTransform.localScale = Vector3.zero; sparkIdle[i] = true; }
                    continue;
                }
                float p = local / sparkDuration[i];
                float k = Mathf.Sin(p * Mathf.PI);
                spark.rectTransform.localScale = new Vector3(k, k, 1f);
                spark.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f * p);
                sparkIdle[i] = false;
            }
        }

        private void ScheduleSpark(int i)
        {
            sparkStart[i] = clock + Mathf.Lerp(0.25f, 1.4f, (float)random.NextDouble()) * look.SparkGap;
            sparkDuration[i] = Mathf.Lerp(0.55f, 0.85f, (float)random.NextDouble());
            RectTransform rt = sparks[i].rectTransform;
            Vector2 jitter = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 3f;
            rt.anchoredPosition = PathPoint((float)random.NextDouble()) + jitter;
            float size = sparkSize * Mathf.Lerp(0.8f, 1.25f, (float)random.NextDouble());
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = Vector3.zero;
            sparkIdle[i] = true;
        }

        private void StartBurst()
        {
            if (look == null) return;
            burstClock = 0f;
            sheenOffset = -clock;   // 出货这一刻立刻扫一道光
            if (flash != null) flash.gameObject.SetActive(true);
            if (rings != null) for (int i = 0; i < rings.Length; i++) if (rings[i] != null) rings[i].gameObject.SetActive(true);
        }

        private void StopBurst()
        {
            burstClock = -1f;
            if (flash != null) flash.gameObject.SetActive(false);
            if (rings != null) for (int i = 0; i < rings.Length; i++) if (rings[i] != null) rings[i].gameObject.SetActive(false);
        }

        private void AnimateBurst(float dt)
        {
            if (burstClock < 0f) return;
            burstClock += dt;
            if (flash != null) SetAlpha(flash, 0.55f * (1f - BossRushUI.SmoothStep(burstClock / BurstFlashSeconds)));
            if (rings != null)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    Image ring = rings[i];
                    if (ring == null) continue;
                    float local = burstClock - i * BurstRingStagger;
                    float q = Mathf.Clamp01(local / BurstRingSeconds);
                    float scale = 1f + 0.5f * BossRushUI.EaseOut(q);
                    ring.rectTransform.localScale = new Vector3(scale, scale, 1f);
                    SetAlpha(ring, local < 0f ? 0f : 0.9f * (1f - q) * (1f - q));
                }
            }
            int ringCount = rings != null ? rings.Length : 1;
            if (burstClock >= BurstRingSeconds + BurstRingStagger * (ringCount - 1)) StopBurst();
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            alpha = Mathf.Clamp01(alpha);
            Color color = graphic.color;
            if (Mathf.Abs(color.a - alpha) < 0.004f) return;
            color.a = alpha;
            graphic.color = color;
        }

        // ====================================================================
        // 构建与布局
        // ====================================================================

        private void Build(int tier)
        {
            ClearContent();
            builtTier = tier;
            look = Looks[tier];
            underContent = NewContainer("Content", underRoot);
            overContent = NewContainer("Content", overRoot);

            if (look.OuterMax > 0f) outerGlow = NewImage("OuterGlow", underContent, CollectibleSlotFrameArt.OuterGlow, true, Tint(qualityColor, 0f));
            innerGlow = NewImage("InnerGlow", underContent, CollectibleSlotFrameArt.InnerGlow, true, Tint(qualityColor, look.InnerMin));
            frame = NewImage("Gilt", underContent, CollectibleSlotFrameArt.Frame, true, Color.white);
            frame.gameObject.AddComponent<CollectibleFrameShading>();
            for (int i = 0; i < corners.Length; i++) corners[i] = NewImage("Corner" + i, underContent, CollectibleSlotFrameArt.Corner, false, Color.white);
            if (look.Crest) crest = NewImage("Crest", underContent, CollectibleSlotFrameArt.Crest, false, Color.white);

            trail = new Image[look.Comets * TrailLength];
            for (int c = 0; c < look.Comets; c++)
            {
                for (int j = 0; j < TrailLength; j++)
                {
                    float k = (float)j / (TrailLength - 1);
                    Color color = Color.Lerp(GlintHead, qualityColor, k * 0.6f);
                    trail[c * TrailLength + j] = NewImage("Glint", underContent, CollectibleSlotFrameArt.Dot, false, Tint(color, Mathf.Lerp(1f, 0.12f, k)));
                }
            }

            sparks = new Image[look.Sparks];
            sparkStart = new float[look.Sparks];
            sparkDuration = new float[look.Sparks];
            sparkIdle = new bool[look.Sparks];
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = NewImage("Spark", underContent, CollectibleSlotFrameArt.Spark, false, SparkColor);
                sparks[i].rectTransform.localScale = Vector3.zero;
                sparkStart[i] = -1f;
                sparkIdle[i] = true;
            }

            sheenMask = NewContainer("SheenMask", overContent);
            sheenMask.gameObject.AddComponent<RectMask2D>();
            sheen = NewImage("Sheen", sheenMask, CollectibleSlotFrameArt.Sheen, false, Tint(SheenColor, look.SheenAlpha));
            sheen.rectTransform.localEulerAngles = new Vector3(0f, 0f, SheenTilt);
            sheenParked = false;

            flash = NewImage("Flash", overContent, CollectibleSlotFrameArt.Dot, false, Tint(Color.Lerp(GlintHead, qualityColor, 0.3f), 0f));
            rings = new Image[look.BurstRings];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = NewImage("Ring", overContent, CollectibleSlotFrameArt.OuterGlow, true, Tint(Color.Lerp(RingGold, qualityColor, 0.35f), 0f));
            StopBurst();
            laidOutSize = Vector2.zero;
        }

        private void ClearContent()
        {
            if (underContent != null) Destroy(underContent.gameObject);
            if (overContent != null) Destroy(overContent.gameObject);
            underContent = overContent = sheenMask = null;
            outerGlow = innerGlow = frame = crest = sheen = flash = null;
            for (int i = 0; i < corners.Length; i++) corners[i] = null;
            trail = sparks = rings = null;
        }

        private bool EnsureLayout()
        {
            Rect rect = underRoot.rect;
            Vector2 size = new Vector2(rect.width, rect.height);
            if (size.x < 16f || size.y < 16f) return false;
            if ((size - laidOutSize).sqrMagnitude < 0.01f) return true;
            laidOutSize = size;
            Layout(size);
            return true;
        }

        private void Layout(Vector2 size)
        {
            float w = size.x - inset * 2f, h = size.y - inset * 2f;
            float shortSide = Mathf.Min(w, h);
            float extent = CollectibleSlotFrameArt.OuterGlowExtent;
            if (outerGlow != null) Stretch(outerGlow.rectTransform, inset - extent);
            Stretch(innerGlow.rectTransform, inset);
            Stretch(frame.rectTransform, inset);
            for (int i = 0; i < rings.Length; i++) Stretch(rings[i].rectTransform, inset - extent);
            Stretch(sheenMask, inset + 1.5f);

            float cornerSize = Mathf.Clamp(shortSide * 0.2f, 12f, 22f);
            Vector2[] anchors = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };
            float[] angles = { 0f, -90f, 180f, 90f };
            for (int i = 0; i < corners.Length; i++)
            {
                // 角花按左上角画：枢轴放在图的左上角，转 0 / -90 / 180 / 90 度分给四个角，金枝都朝格子里伸。
                RectTransform rt = corners[i].rectTransform;
                Vector2 outward = new Vector2(anchors[i].x * 2f - 1f, anchors[i].y * 2f - 1f);
                rt.anchorMin = rt.anchorMax = anchors[i];
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(cornerSize, cornerSize);
                rt.localEulerAngles = new Vector3(0f, 0f, angles[i]);
                rt.anchoredPosition = outward * (CornerOverhang - inset);
            }
            if (crest != null)
            {
                RectTransform rt = crest.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(cornerSize * 1.05f, cornerSize * 0.63f);
                rt.anchoredPosition = new Vector2(0f, -(inset + CollectibleSlotFrameArt.LineDepth));
            }

            float line = CollectibleSlotFrameArt.LineDepth;
            pathHalfW = w * 0.5f - line;
            pathHalfH = h * 0.5f - line;
            pathRadius = Mathf.Clamp(CollectibleSlotFrameArt.CornerRadius - line, 1f, Mathf.Min(pathHalfW, pathHalfH));
            pathLength = 2f * (2f * (pathHalfW - pathRadius) + 2f * (pathHalfH - pathRadius)) + TwoPi * pathRadius;

            float head = Mathf.Clamp(shortSide * 0.11f, 7f, 11f);
            for (int j = 0; j < trail.Length; j++)
            {
                float k = (float)(j % TrailLength) / (TrailLength - 1);
                float dot = Mathf.Lerp(head, head * 0.35f, k);
                Center(trail[j].rectTransform, new Vector2(dot, dot));
            }
            sparkSize = Mathf.Clamp(shortSide * 0.14f, 9f, 15f);
            for (int i = 0; i < sparks.Length; i++)
            {
                Center(sparks[i].rectTransform, new Vector2(sparkSize, sparkSize));
                sparkStart[i] = -1f;
                sparkDuration[i] = 0f;
            }

            float sheenWidth = w * 0.36f, sheenHeight = Mathf.Max(w, h) * 2.4f;
            Center(sheen.rectTransform, new Vector2(sheenWidth, sheenHeight));
            float tilt = Mathf.Abs(SheenTilt) * Mathf.Deg2Rad;
            sheenTravel = w * 0.5f + 0.5f * (sheenWidth * Mathf.Cos(tilt) + sheenHeight * Mathf.Sin(tilt));
            sheenParked = false;
            Center(flash.rectTransform, new Vector2(w * 1.25f, h * 1.25f));
        }

        /// <summary>金线中线上的点：u 从左上角起顺时针走一圈（0..1），坐标相对格子中心，圆角按弧线走。</summary>
        private Vector2 PathPoint(float u)
        {
            float r = pathRadius, hw = pathHalfW, hh = pathHalfH;
            float straightW = 2f * (hw - r), straightH = 2f * (hh - r), arc = Mathf.PI * 0.5f * r;
            float d = Mathf.Repeat(u, 1f) * pathLength;
            if (d < straightW) return new Vector2(-hw + r + d, hh);
            d -= straightW;
            if (d < arc) return Arc(hw - r, hh - r, Mathf.PI * 0.5f - d / r);
            d -= arc;
            if (d < straightH) return new Vector2(hw, hh - r - d);
            d -= straightH;
            if (d < arc) return Arc(hw - r, -hh + r, -d / r);
            d -= arc;
            if (d < straightW) return new Vector2(hw - r - d, -hh);
            d -= straightW;
            if (d < arc) return Arc(-hw + r, -hh + r, -Mathf.PI * 0.5f - d / r);
            d -= arc;
            if (d < straightH) return new Vector2(-hw, -hh + r + d);
            d -= straightH;
            return Arc(-hw + r, hh - r, Mathf.PI - Mathf.Min(d, arc) / r);
        }

        private Vector2 Arc(float cx, float cy, float angle)
        {
            return new Vector2(cx + Mathf.Cos(angle) * pathRadius, cy + Mathf.Sin(angle) * pathRadius);
        }

        // ====================================================================
        // 小工具
        // ====================================================================

        private static Color Tint(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Transform TopChild(Transform host, Transform descendant)
        {
            Transform t = descendant;
            while (t != null && t.parent != host) t = t.parent;
            return t;
        }

        private static RectTransform NewRoot(string name, Transform parent)
        {
            RectTransform rt = NewContainer(name, parent);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return rt;
        }

        private static RectTransform NewContainer(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, 0f);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, bool sliced, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.color = color;
            if (sliced && sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.fillCenter = false;   // 这几张 9 宫格中心都是透明的，不画中间那块
            }
            // 贴图烘焙失败时 sprite 为空，Image 会画成白方块：宁可少一层，也不盖住物品。
            if (sprite == null) image.enabled = false;
            Center(image.rectTransform, Vector2.zero);
            return image;
        }

        /// <summary>铺满父物体，四边各往里收 inset（负数就是往外扩）。</summary>
        private static void Stretch(RectTransform rt, float insetUnits)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(insetUnits, insetUnits);
            rt.offsetMax = new Vector2(-insetUnits, -insetUnits);
        }

        private static void Center(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }
    }

    /// <summary>金框的竖向明暗：顶边保持贴图原色，往下逐渐压暗到 74%，像光从上方打在金属上。</summary>
    internal sealed class CollectibleFrameShading : BaseMeshEffect
    {
        private const float BottomShade = 0.74f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh == null || vh.currentVertCount == 0) return;
            UIVertex vertex = default(UIVertex);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                min = Mathf.Min(min, vertex.position.y);
                max = Mathf.Max(max, vertex.position.y);
            }
            float span = Mathf.Max(0.001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                float k = Mathf.Lerp(BottomShade, 1f, (vertex.position.y - min) / span);
                Color32 c = vertex.color;
                vertex.color = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
