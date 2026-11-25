using System;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 止语钟罩的表现，随驱动一起生灭（驱动只在主角出击时实际佩戴才存在）：
    /// - 金钟罩（常驻，owner 2026-11-25 要求「带着的时候周身弄个金钟罩」）：戴着就有一口鎏金钟罩在主角身上，
    ///   环线 + 竖肋的描光，钟口最亮、往上渐淡，缓缓自转、轻轻呼吸；脚下一枚淡金钟纹慢转，几点金芒顺着钟壁往上飘。
    ///   钟身高度就是护罩余量：挨打往下矮，回充时一寸寸铸回去（顶上那道亮边就是正在铸的一圈）；
    ///   满罩时隔一会儿有一道流光自钟口升到钟顶。打碎后钟身没了，只剩钟口一圈将熄的铜红余烬，等回充再从钟口铸起。
    /// - 受击：整口钟一亮一胀并低鸣；打碎：钟向外崩开、由金转朱，金屑带墨烟四散一声钟碎；
    ///   回满：钟身一亮，脚下一圈细金环一声「叮」。
    /// - 头顶护罩条：墨底铜条，压在官方血条上沿；挨打闪白金、打碎裂成两半、回充时一道流光扫过，回满停一会儿淡出。
    /// 只用共享 BossRushFxKit 材质与水墨色板，不新建材质或着色器。金钟是跟着人的世界特效：不随官方 HUD 隐藏，暂停时定格；
    /// 头顶条照常驻 HUD 的规矩过官方 HUD 显隐与暂停两道门。每帧成本是十几条线的颜色 + 两个小发射器，
    /// 钟身竖肋只在高度变了（挨打、回充）时重算。
    /// </summary>
    internal sealed class SilenceBellFx : MonoBehaviour
    {
        private const float BarWidth = 76f;
        private const float BarHeight = 6f;
        private const float HitFlashSeconds = 0.18f;
        private const float ShardSeconds = 0.36f;
        private const float SweepPeriod = 0.9f;
        private const int RingSegments = 40;
        private const int RibCount = 10;

        // 金钟罩
        /// <summary>常驻时的亮度：看得出一口钟罩着，又不糊住主角。受击时在这上面再加。</summary>
        private const float BellIdleGlow = 0.34f;
        private const float HitBoostSeconds = 0.25f;
        private const float HitBoostGain = 2.4f;
        private const float ShatterSeconds = 0.4f;
        private const float BreathPeriod = 2.8f;
        private const float BreathDepth = 0.2f;
        private const float SpinDegreesPerSecond = 8f;
        /// <summary>钟身高度追护罩余量的速度（每秒钟高的几成）：戴上时约 0.4 秒从钟口铸到钟顶。</summary>
        private const float CastSpeed = 2.4f;
        private const float FlowPeriod = 3.6f;
        /// <summary>一个流光周期里前六成在往上走，后四成歇着。</summary>
        private const float FlowTravel = 0.6f;
        private const float SealSpinDegreesPerSecond = 10f;
        private const float SealLifetime = 4f;
        private const float SealInterval = 3.2f;
        private const float MoteInterval = 0.34f;
        private const float MoteIntervalCasting = 0.12f;
        private const int MaxMotes = 14;

        // 钟形外廓：u 为自下而上的归一高度，r 为相对钟口的半径（钟口外撇、腰收、肩圆、顶收）。
        private static readonly float[] ProfileU = { 0f, 0.08f, 0.22f, 0.45f, 0.70f, 0.86f, 0.95f, 1f };
        private static readonly float[] ProfileR = { 1f, 0.92f, 0.78f, 0.70f, 0.66f, 0.58f, 0.40f, 0.12f };
        private static readonly float[] RingU = { 0f, 0.22f, 0.55f, 0.86f };
        private static readonly float[] RingGlow = { 1f, 0.55f, 0.45f, 0.6f };

        private static readonly Color Bronze = new Color(0.62f, 0.40f, 0.20f, 1f);
        private static readonly Color Sheen = new Color(1f, 0.90f, 0.66f, 1f);

        private Canvas canvas;
        private RectTransform canvasRect, barRoot, fillRect, sweepRect, shardLeft, shardRight;
        private CanvasGroup barGroup;
        private Image background, fill, fillSheen, flash, sweep, shardLeftImage, shardRightImage;
        private Camera worldCamera;

        private Transform follow, bellAnchor, bellRoot;
        private LineRenderer[] ringLines, ribLines;
        private LineRenderer haloLine, edgeLine, flowLine;
        private ParticleSystem seal, motes;
        private readonly float[] shellU = new float[ProfileU.Length + 1];

        private Vector3 footPosition;
        private float headHeight = 1f, fraction = 1f, barAlpha, lingerLeft, flashAge = 1f, shardAge = 1f, sweepPhase, lastHumAt = -10f;
        private float shownHeight, shellHeight = -1f, shatterAge = 1f, shatterHeight, hitBoost, spin, breath, flowPhase,
            moteTimer, clock, sealMouth;
        private bool broken, recharging, hasState;

        internal static SilenceBellFx Create(Transform parent)
        {
            GameObject go = new GameObject("SilenceBell_Fx");
            try
            {
                go.transform.SetParent(parent, false);
                SilenceBellFx view = go.AddComponent<SilenceBellFx>();
                view.BuildBar();
                view.BuildBell();
                return view;
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 表现创建失败: " + e.Message);
                return null;
            }
        }

        /// <summary>金钟与头顶条跟着谁走（主角）。在 LateUpdate 里读它的位置，人跑起来钟不拖一帧。</summary>
        internal void Follow(Transform target)
        {
            follow = target;
        }

        // ------------------------------------------------------------------
        // 搭建
        // ------------------------------------------------------------------

        private void BuildBar()
        {
            canvas = BossRushUI.CreateCanvasRoot("SilenceBell_WardBar", BossRushUILayers.WorldOverlay, false);
            canvas.transform.SetParent(transform, false);
            canvasRect = (RectTransform)canvas.transform;
            worldCamera = Camera.main;

            GameObject root = new GameObject("WardBar", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(canvas.transform, false);
            barRoot = (RectTransform)root.transform;
            barRoot.anchorMin = barRoot.anchorMax = barRoot.pivot = new Vector2(0.5f, 0.5f);
            barRoot.sizeDelta = new Vector2(BarWidth + 10f, BarHeight + 6f);
            barGroup = root.GetComponent<CanvasGroup>();
            barGroup.interactable = barGroup.blocksRaycasts = false;
            barGroup.alpha = 0f;

            Sprite soft = BossRushFxKit.GetSoftCircleSprite();
            // 墨底两端各一枚软圆当笔锋，读作一笔横画，不是方框。
            Rect(root.transform, "BrushTipL", soft, new Vector2(10f, BarHeight + 4f), new Vector2(-BarWidth * 0.5f, 0f),
                BossRushInkColors.InkWash, 0.5f);
            Rect(root.transform, "BrushTipR", soft, new Vector2(10f, BarHeight + 4f), new Vector2(BarWidth * 0.5f, 0f),
                BossRushInkColors.InkWash, 0.5f);
            background = Rect(root.transform, "Ink", null, new Vector2(BarWidth, BarHeight + 2f), Vector2.zero,
                BossRushInkColors.InkWash, 0.5f);
            background.color = WithAlpha(BossRushInkColors.InkWash, 0.82f);

            fill = Rect(root.transform, "Bronze", null, new Vector2(BarWidth, BarHeight), new Vector2(-BarWidth * 0.5f, 0f),
                Bronze, 0f);
            fillRect = fill.rectTransform;
            fillSheen = Rect(fill.transform, "Sheen", null, new Vector2(0f, 1f), new Vector2(0f, BarHeight * 0.5f - 0.5f),
                WithAlpha(Sheen, 0.55f), 0f);
            fillSheen.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fillSheen.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            fillSheen.rectTransform.sizeDelta = new Vector2(0f, 1f);
            fillSheen.rectTransform.anchoredPosition = new Vector2(0f, BarHeight * 0.5f - 0.5f);

            sweep = Rect(fill.transform, "Sweep", soft, new Vector2(16f, BarHeight + 3f), Vector2.zero, WithAlpha(Sheen, 0f), 0.5f);
            sweepRect = sweep.rectTransform;
            sweepRect.anchorMin = sweepRect.anchorMax = new Vector2(0f, 0.5f);

            flash = Rect(root.transform, "Flash", null, new Vector2(BarWidth, BarHeight), Vector2.zero, WithAlpha(Sheen, 0f), 0.5f);
            shardLeftImage = Rect(root.transform, "ShardL", null, new Vector2(BarWidth * 0.5f, BarHeight), new Vector2(-BarWidth * 0.25f, 0f),
                WithAlpha(BossRushInkColors.Gold, 0f), 0.5f);
            shardRightImage = Rect(root.transform, "ShardR", null, new Vector2(BarWidth * 0.5f, BarHeight), new Vector2(BarWidth * 0.25f, 0f),
                WithAlpha(BossRushInkColors.Gold, 0f), 0.5f);
            shardLeft = shardLeftImage.rectTransform;
            shardRight = shardRightImage.rectTransform;
            canvas.enabled = false;
        }

        private static Image Rect(Transform parent, string name, Sprite sprite, Vector2 size, Vector2 position, Color color, float pivotX)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.color = color;
            RectTransform rect = image.rectTransform;
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return image;
        }

        /// <summary>
        /// 金钟：bellAnchor 跟脚走、不缩放（钟纹与金芒挂在这里）；bellRoot 按钟口半径 × 钟高缩放并慢慢自转（钟线挂在这里）。
        /// </summary>
        private void BuildBell()
        {
            GameObject anchor = new GameObject("GoldenBell");
            anchor.transform.SetParent(transform, false);
            bellAnchor = anchor.transform;
            GameObject shell = new GameObject("Shell");
            shell.transform.SetParent(bellAnchor, false);
            bellRoot = shell.transform;

            Material strip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            // 地上一圈宽而淡的柔光，钟像是落在一汪金光里
            haloLine = UnitRing("Halo", strip, 0.26f, 0.01f, 1.18f);
            ringLines = new LineRenderer[RingU.Length];
            for (int i = 0; i < RingU.Length; i++)
                ringLines[i] = UnitRing("Ring_" + i, strip, i == 0 ? 0.045f : 0.024f, RingU[i], Radius(RingU[i]));
            ribLines = new LineRenderer[RibCount];
            for (int k = 0; k < RibCount; k++)
                ribLines[k] = Line("Rib_" + k, bellRoot, strip, 0.016f);
            // 正在铸的那一圈（回充 / 戴上时）与满罩时上行的流光：单位圆，逐帧挪高度、按外廓缩半径
            edgeLine = UnitRing("CastEdge", strip, 0.05f, 0f, 1f);
            flowLine = UnitRing("Flow", strip, 0.032f, 0f, 1f);

            BuildSeal();
            BuildMotes();
            anchor.SetActive(false);
        }

        private LineRenderer UnitRing(string name, Material material, float width, float u, float radius)
        {
            LineRenderer ring = Line(name, bellRoot, material, width);
            ring.loop = true;
            ring.positionCount = RingSegments;
            for (int s = 0; s < RingSegments; s++)
            {
                float a = s * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(s, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            PlaceRing(ring, u, radius);
            return ring;
        }

        private static void PlaceRing(LineRenderer ring, float u, float radius)
        {
            Transform t = ring.transform;
            t.localPosition = new Vector3(0f, u, 0f);
            t.localScale = new Vector3(radius, 1f, radius);
        }

        private static LineRenderer Line(string name, Transform parent, Material material, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.startColor = line.endColor = WithAlpha(BossRushInkColors.Gold, 0f);
            return line;
        }

        /// <summary>脚下钟纹：每 3.2 秒落一枚 4 秒寿命的水平纹，前后两枚交叉淡入淡出，读作一枚一直在慢转的纹。</summary>
        private void BuildSeal()
        {
            Material rune = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            seal = BossRushFxKit.CreateEmitter("BellSeal", bellAnchor, new Vector3(0f, 0.04f, 0f), rune, 3, false);
            if (seal == null) return;
            ParticleSystem.MainModule main = seal.main;
            main.duration = SealInterval;
            main.startLifetime = SealLifetime;
            main.startSize = 1.6f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = seal.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.RotationOverLifetimeModule rotation = seal.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = SealSpinDegreesPerSecond * Mathf.Deg2Rad;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(BossRushInkColors.Gold, 0f), new GradientColorKey(BossRushInkColors.Gold, 1f) },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.3f, 0.2f),
                    new GradientAlphaKey(0.3f, 0.8f), new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule color = seal.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystemRenderer renderer = seal.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        /// <summary>金芒：不自动发射，由 UpdateBell 按节奏在钟壁上点一颗，沿钟壁斜着往上飘、渐小渐淡。</summary>
        private void BuildMotes()
        {
            Material dot = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            motes = BossRushFxKit.CreateEmitter("BellMotes", bellAnchor, Vector3.zero, dot, MaxMotes, false);
            if (motes == null) return;
            ParticleSystem.ColorOverLifetimeModule color = motes.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(WithAlpha(Sheen, 0.95f), WithAlpha(BossRushInkColors.Gold, 0.75f),
                WithAlpha(BossRushInkColors.Gold, 0f), 0.15f);
            ParticleSystem.SizeOverLifetimeModule size = motes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        }

        private static float Radius(float u)
        {
            for (int i = 1; i < ProfileU.Length; i++)
            {
                if (u > ProfileU[i]) continue;
                float t = Mathf.InverseLerp(ProfileU[i - 1], ProfileU[i], u);
                return Mathf.Lerp(ProfileR[i - 1], ProfileR[i], t);
            }
            return ProfileR[ProfileR.Length - 1];
        }

        // ------------------------------------------------------------------
        // 驱动入口
        // ------------------------------------------------------------------

        internal void SetState(Vector3 foot, float head, float shieldFraction, bool isBroken, bool isRecharging)
        {
            footPosition = foot;
            headHeight = Mathf.Max(0.5f, head);
            // 没满就一直续上停留时间；回满那一刻起倒数，数完才淡出。
            if (isBroken || shieldFraction < 1f) lingerLeft = SilenceBellConfig.HudLingerSeconds;
            fraction = shieldFraction;
            broken = isBroken;
            recharging = isRecharging;
            hasState = true;
        }

        internal void PlayHit(Vector3 foot, float now)
        {
            flashAge = 0f;
            hitBoost = 1f;
            lingerLeft = SilenceBellConfig.HudLingerSeconds;
            if (now - lastHumAt < SilenceBellConfig.HumIntervalSeconds) return;
            lastHumAt = now;
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxBellHum);
        }

        internal void PlayShatter(Vector3 foot)
        {
            shardAge = 0f;
            flashAge = 0f;
            // 钟按打碎前的高度整口崩开（至少四分之一口，薄薄一层被打碎也看得见崩）
            shatterHeight = Mathf.Max(shownHeight, 0.25f);
            shatterAge = 0f;
            hitBoost = 0f;
            broken = true;
            fraction = 0f;
            lingerLeft = SilenceBellConfig.HudLingerSeconds;
            if (seal != null) seal.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            try
            {
                Vector3 chest = foot + Vector3.up * (headHeight * 0.6f);
                BossRushFxBurst shards = new BossRushFxBurst();
                shards.Shape = BossRushParticleShape.GlowDot;
                shards.Blend = BossRushFxBlend.Additive;
                shards.Gain = BossRushFxKit.GainHot;
                shards.Count = 26;
                shards.Cone = 180f;
                shards.Direction = Vector3.up;
                shards.SpeedMin = 3.5f;
                shards.SpeedMax = 7.5f;
                shards.Drag = 1.1f;
                shards.Gravity = 1.4f;
                shards.Stretch = 0.08f;
                shards.SizeMin = 0.08f;
                shards.SizeMax = 0.18f;
                shards.GrowTo = 0.7f;
                shards.LifeMin = 0.45f;
                shards.LifeMax = 0.85f;
                shards.ShapeRadius = 0.55f;
                shards.Core = Sheen;
                shards.Main = BossRushInkColors.Gold;
                shards.End = WithAlpha(Bronze, 0f);
                BossRushFxKit.PlayBurst(chest, shards);

                BossRushFxBurst smoke = new BossRushFxBurst();
                smoke.Shape = BossRushParticleShape.Wisp;
                smoke.Blend = BossRushFxBlend.Alpha;
                smoke.Gain = BossRushFxKit.GainSoft;
                smoke.Count = 12;
                smoke.Cone = 180f;
                smoke.Direction = Vector3.up;
                smoke.SpeedMin = 0.8f;
                smoke.SpeedMax = 1.8f;
                smoke.Drag = 1.6f;
                smoke.SizeMin = 0.6f;
                smoke.SizeMax = 1.0f;
                smoke.GrowTo = 1.8f;
                smoke.LifeMin = 0.7f;
                smoke.LifeMax = 1.1f;
                smoke.ShapeRadius = 0.5f;
                smoke.Spin = 30f;
                smoke.FadeIn = 0.06f;
                smoke.Core = new Color(0.10f, 0.08f, 0.07f, 0.6f);
                smoke.Main = new Color(0.07f, 0.06f, 0.05f, 0.45f);
                smoke.End = new Color(0.03f, 0.02f, 0.02f, 0f);
                BossRushFxKit.PlayBurst(chest, smoke);
            }
            catch (Exception e)
            {
                // 纯表现：碎片放不出来不影响护罩已经打碎这件事。
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 碎罩特效失败: " + e.Message);
            }
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxBellShatter);
        }

        internal void PlayRefill(Vector3 foot)
        {
            lingerLeft = SilenceBellConfig.HudLingerSeconds;
            // 回满：钟身一亮，紧接着一道流光从钟口升起
            hitBoost = 1f;
            flowPhase = 0f;
            try
            {
                BossRushFxBurst ring = BossRushFxKit.Shockwave(WithAlpha(BossRushInkColors.Gold, 0.75f), 0.6f, 2.4f, 0.55f, true);
                BossRushFxKit.PlayBurst(foot + Vector3.up * 0.05f, ring);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 回满金环失败: " + e.Message);
            }
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxBellDing);
        }

        /// <summary>驱动撤掉前调用：头顶条与金钟立刻收起；已放出的碎片、墨烟与钟声各自收尾。</summary>
        internal void Release()
        {
            if (canvas != null) canvas.enabled = false;
            if (bellAnchor != null) bellAnchor.gameObject.SetActive(false);
            hasState = false;
        }

        // ------------------------------------------------------------------
        // 逐帧
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            bool paused = BossRushUI.IsGamePaused();
            float dt = paused ? 0f : Time.deltaTime;
            // 金钟是世界特效：官方界面盖住时照样罩着人，暂停时定格
            UpdateBell(dt);

            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
            if (!visible) return;
            if (!hasState) { canvas.enabled = false; return; }
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) { canvas.enabled = false; return; }

            lingerLeft = Mathf.Max(0f, lingerLeft - dt);
            bool wanted = fraction < 1f || broken || lingerLeft > 0f || shardAge < ShardSeconds;
            barAlpha = Mathf.MoveTowards(barAlpha, wanted ? 1f : 0f, dt * (wanted ? 8f : 2.5f));
            if (barGroup.alpha != barAlpha) barGroup.alpha = barAlpha;
            if (barAlpha <= 0f) return;

            Vector3 point = worldCamera.WorldToViewportPoint(footPosition + Vector3.up * (headHeight + SilenceBellConfig.HudHeadClearance));
            if (point.z <= 0f || point.x < -0.05f || point.x > 1.05f || point.y < -0.05f || point.y > 1.05f)
            {
                barGroup.alpha = 0f;
                return;
            }
            Rect rect = canvasRect.rect;
            Vector2 position = new Vector2((point.x - 0.5f) * rect.width, (point.y - 0.5f) * rect.height);
            if (!barRoot.anchoredPosition.Equals(position)) barRoot.anchoredPosition = position;

            float width = BarWidth * Mathf.Clamp01(fraction);
            fillRect.sizeDelta = new Vector2(width, BarHeight);
            fill.enabled = width > 0.5f;
            fill.color = Color.Lerp(Bronze, BossRushInkColors.Gold, Mathf.Clamp01(fraction));
            background.color = broken
                ? Color.Lerp(WithAlpha(BossRushInkColors.InkWash, 0.82f), WithAlpha(BossRushInkColors.CinnabarFill, 0.9f), 0.6f)
                : WithAlpha(BossRushInkColors.InkWash, 0.82f);

            if (flashAge < HitFlashSeconds)
            {
                flashAge = Mathf.Min(HitFlashSeconds, flashAge + dt);
                flash.color = WithAlpha(Sheen, 0.85f * (1f - BossRushUI.EaseOut(flashAge / HitFlashSeconds)));
            }
            else if (flash.color.a != 0f) flash.color = WithAlpha(Sheen, 0f);

            if (shardAge < ShardSeconds)
            {
                shardAge = Mathf.Min(ShardSeconds, shardAge + dt);
                float t = BossRushUI.EaseOut(shardAge / ShardSeconds);
                float a = 1f - t;
                shardLeft.anchoredPosition = new Vector2(-BarWidth * 0.25f - 7f * t, -2f * t);
                shardRight.anchoredPosition = new Vector2(BarWidth * 0.25f + 7f * t, 2f * t);
                shardLeft.localRotation = Quaternion.Euler(0f, 0f, 6f * t);
                shardRight.localRotation = Quaternion.Euler(0f, 0f, -6f * t);
                shardLeftImage.color = shardRightImage.color = WithAlpha(BossRushInkColors.Gold, a);
            }
            else if (shardLeftImage.color.a != 0f) shardLeftImage.color = shardRightImage.color = WithAlpha(BossRushInkColors.Gold, 0f);

            if (recharging && width > 2f)
            {
                sweepPhase = (sweepPhase + dt / SweepPeriod) % 1f;
                sweepRect.anchoredPosition = new Vector2(sweepPhase * width, 0f);
                sweep.color = WithAlpha(Sheen, 0.55f * Mathf.Sin(sweepPhase * Mathf.PI));
            }
            else if (sweep.color.a != 0f) sweep.color = WithAlpha(Sheen, 0f);
        }

        private void UpdateBell(float dt)
        {
            if (bellAnchor == null) return;
            if (!hasState)
            {
                if (bellAnchor.gameObject.activeSelf) bellAnchor.gameObject.SetActive(false);
                return;
            }
            if (!bellAnchor.gameObject.activeSelf) bellAnchor.gameObject.SetActive(true);
            if (follow != null) footPosition = follow.position;
            bellAnchor.position = footPosition;
            clock += dt;

            float bellHeight = headHeight + 0.45f;
            float mouth = Mathf.Max(0.55f, headHeight * 0.62f);
            if (seal != null && Mathf.Abs(mouth - sealMouth) > 0.02f)
            {
                sealMouth = mouth;
                ParticleSystem.MainModule main = seal.main;
                main.startSize = mouth * 2.7f;
            }

            // 钟身高度 = 护罩余量；打碎那一下按打碎前的高度整口崩开，崩完才归零
            bool shattering = shatterAge < ShatterSeconds;
            float target = broken ? 0f : Mathf.Clamp01(fraction);
            if (shattering)
            {
                shatterAge += dt;
                if (shatterAge >= ShatterSeconds) shownHeight = 0f;
            }
            else shownHeight = Mathf.MoveTowards(shownHeight, target, dt * CastSpeed);
            bool casting = !shattering && !broken && (recharging || shownHeight < target - 0.005f);

            hitBoost = Mathf.Max(0f, hitBoost - dt / HitBoostSeconds);
            spin = Mathf.Repeat(spin + dt * SpinDegreesPerSecond, 360f);
            breath = Mathf.Repeat(breath + dt / BreathPeriod, 1f);
            float shatterT = shattering ? Mathf.Clamp01(shatterAge / ShatterSeconds) : 0f;
            float swell = shattering ? 1f + 0.35f * BossRushUI.EaseOut(shatterT) : 1f + 0.06f * hitBoost;
            bellRoot.localScale = new Vector3(mouth * swell, bellHeight, mouth * swell);
            bellRoot.localRotation = Quaternion.Euler(0f, spin, 0f);

            float h = shattering ? shatterHeight : shownHeight;
            SetShellHeight(h);

            Color tint;
            float glow;
            if (shattering)
            {
                tint = Color.Lerp(Sheen, BossRushInkColors.Cinnabar, shatterT);
                glow = Mathf.Pow(1f - shatterT, 1.5f);
            }
            else
            {
                float breathe = 1f - BreathDepth * 0.5f * (1f + Mathf.Cos(breath * Mathf.PI * 2f));
                glow = BellIdleGlow * breathe * (1f + HitBoostGain * hitBoost);
                tint = Color.Lerp(BossRushInkColors.Gold, Sheen, hitBoost * 0.6f);
            }

            for (int i = 0; i < ringLines.Length; i++)
            {
                bool lit = h > 0.001f && RingU[i] <= h + 0.001f;
                if (i == 0 && !lit && broken && !shattering)
                {
                    // 打碎后钟口一圈将熄的铜红余烬，告诉人「钟还在，等它回」
                    float ember = 0.2f + 0.07f * Mathf.Sin(clock * 7.3f) * Mathf.Sin(clock * 3.1f);
                    SetLine(ringLines[0], WithAlpha(Color.Lerp(Bronze, BossRushInkColors.Cinnabar, 0.5f), ember));
                    continue;
                }
                SetLine(ringLines[i], WithAlpha(tint, lit ? glow * RingGlow[i] : 0f));
            }
            for (int k = 0; k < ribLines.Length; k++)
            {
                // 竖肋钟口最亮、往钟顶渐淡
                LineRenderer rib = ribLines[k];
                float a = h > 0.001f ? glow : 0f;
                rib.enabled = a > 0.002f;
                rib.startColor = WithAlpha(tint, a * 0.4f);
                rib.endColor = WithAlpha(tint, a * 0.16f);
            }
            SetLine(haloLine, WithAlpha(tint, shattering ? 0f : glow * 0.22f * h));

            // 正在铸的一圈：回充 / 戴上时是亮白金，只是被打矮了就是一道暗金的钟沿
            if (!shattering && h > 0.02f && h < 0.995f)
            {
                PlaceRing(edgeLine, h, Radius(h));
                float edge = casting ? 0.95f * (0.85f + 0.15f * Mathf.Sin(clock * 12f)) : glow * 0.9f;
                SetLine(edgeLine, WithAlpha(casting ? Sheen : tint, edge));
            }
            else SetLine(edgeLine, WithAlpha(tint, 0f));

            // 满罩时隔一会儿一道流光自钟口升到钟顶
            if (!shattering && !broken && !casting && h >= 0.995f)
            {
                flowPhase = Mathf.Repeat(flowPhase + dt / FlowPeriod, 1f);
                if (flowPhase < FlowTravel)
                {
                    float t = flowPhase / FlowTravel;
                    float u = Mathf.SmoothStep(0f, 0.96f, t);
                    PlaceRing(flowLine, u, Radius(u));
                    SetLine(flowLine, WithAlpha(Sheen, 0.5f * Mathf.Sin(t * Mathf.PI)));
                }
                else SetLine(flowLine, WithAlpha(Sheen, 0f));
            }
            else
            {
                if (!casting) flowPhase = 0f;
                SetLine(flowLine, WithAlpha(Sheen, 0f));
            }

            // 钟纹：钟在就慢转着；碎了就停发（打碎那一下已经清掉）
            if (seal != null)
            {
                bool wantSeal = !broken && !shattering;
                if (wantSeal && !seal.isEmitting) seal.Play();
                else if (!wantSeal && seal.isEmitting) seal.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            // 金芒：钟在就隔一会儿点一颗，回充 / 戴上时点得勤
            if (motes != null && dt > 0f && !shattering && h > 0.05f)
            {
                if (!motes.isPlaying) motes.Play();
                moteTimer -= dt;
                if (moteTimer <= 0f)
                {
                    moteTimer = casting ? MoteIntervalCasting : MoteInterval;
                    EmitMote(mouth, bellHeight, h);
                }
            }
        }

        /// <summary>竖肋只画到钟身当前高度；高度按 1/250 量化，挨打、回充时才重算。</summary>
        private void SetShellHeight(float h)
        {
            float q = Mathf.Round(Mathf.Clamp01(h) * 250f) / 250f;
            if (q == shellHeight) return;
            shellHeight = q;
            int n = 0;
            if (q > 0.001f)
            {
                for (int p = 0; p < ProfileU.Length && ProfileU[p] < q; p++) shellU[n++] = ProfileU[p];
                shellU[n++] = q;
            }
            for (int k = 0; k < ribLines.Length; k++)
            {
                LineRenderer rib = ribLines[k];
                rib.positionCount = n < 2 ? 0 : n;
                if (n < 2) continue;
                float a = k * Mathf.PI * 2f / RibCount;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int i = 0; i < n; i++) rib.SetPosition(i, dir * Radius(shellU[i]) + Vector3.up * shellU[i]);
            }
        }

        private void EmitMote(float mouth, float bellHeight, float h)
        {
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            float u = UnityEngine.Random.Range(0f, h * 0.7f);
            float r = Radius(u) * mouth * 1.02f;
            ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
            p.position = new Vector3(Mathf.Cos(a) * r, u * bellHeight, Mathf.Sin(a) * r);
            // 贴着钟壁斜着往上飘：一点切向速度跟着钟转的方向
            p.velocity = new Vector3(-Mathf.Sin(a) * 0.15f, UnityEngine.Random.Range(0.35f, 0.65f), Mathf.Cos(a) * 0.15f);
            p.startLifetime = UnityEngine.Random.Range(1.1f, 1.7f);
            p.startSize = UnityEngine.Random.Range(0.045f, 0.085f);
            p.startColor = Color.white;
            motes.Emit(p, 1);
        }

        private static void SetLine(LineRenderer line, Color color)
        {
            bool on = color.a > 0.002f;
            if (line.enabled != on) line.enabled = on;
            if (!on) return;
            line.startColor = line.endColor = color;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
