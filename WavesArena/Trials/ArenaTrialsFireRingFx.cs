// ============================================================================
// ArenaTrialsFireRingFx.cs - 焚城火墙的表现
// ============================================================================
// 质感口径（owner 2026-11-25：高级、大气、史诗，不要塑料廉价感）：
//   - 地上三笔画在真实判定半径上：圈外一道焦墨（烧过的地）、一道余烬床、一线白金熔缝；余烬床与熔缝按噪声明灭；
//   - 火舌（拉伸的烟缕：白金芯 → 余烬 → 暗红）、圈外翻起的墨色浓烟、被热风卷进圈里的火星，只在玩家看得见的那段弧上发：
//     固定俯视一屏约 28×20 米，整圈都发既看不见又白花开销；
//   - 三盏跟着那段弧走的暖光，把火光真的照在地上（URP Deferred 下点光照得到地面）；
//   - 开波火从离玩家最近的一点燃起、分两头绕圈合拢（先慢后快，看得清火是怎么烧开的）；
//     收紧前地上落一线金丝与十二枚符印标出落点，火墙收到落点时符印一齐亮起、盖印后隐去；
//     收波火墙向外退、慢慢熄灭，最后整棵交给 ArenaTrialsFx.Retire 淡尽。
// 不放碰撞体；粒子全部手动 Emit（世界坐标）；材质取共享工厂（BossRushFxKit），本文件不 new 材质、不持有静态缓存。
// 地面高度按角度取样：建圈时整圈取一次，圈在动时每帧轮转补取至多 HeightSamplesPerFrame 次向下射线，圈不动就停。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed class ArenaTrialsFireRingFx
    {
        private const int Segments = 160;
        /// <summary>发火舌的那段弧（以玩家所在方位为中）半长，米。</summary>
        private const float ArcHalfLength = 22f;
        /// <summary>玩家离圈线 VisibleNear 米内全量发，到 VisibleFar 米渐停（再远一屏里也看不到火墙）。</summary>
        private const float VisibleNear = 18f, VisibleFar = 32f;
        private const int SealCount = 12;
        private const float SealRadius = 0.9f, SealHaloRadius = 1.5f;
        private const int HeightSamplesPerFrame = 12;
        private const float CharOffset = 3.0f, CharWidth = 6.4f;
        private const float BedOffset = 0.35f, BedWidth = 2.2f;
        private const float SeamWidth = 0.3f, TargetWidth = 0.14f;
        private const float FlamesPerMeter = 2.4f, SmokePerMeter = 0.3f, EmbersPerMeter = 0.45f;
        /// <summary>燃起时每个火头每秒多窜的火舌、火星（按秒计，不随帧率变）。</summary>
        private const float HeadFlamesPerSecond = 54f, HeadEmbersPerSecond = 22f;
        private const float LightSpacing = 11f, LightIntensity = 2.6f, LightRange = 11f;
        private const float SealFlashSeconds = 1.4f;

        private static readonly Color CharColor = new Color(0.08f, 0.05f, 0.04f, 1f);
        private static readonly Color SeamColor = new Color(1f, 0.87f, 0.62f, 1f);
        private static readonly Color GoldColor = new Color(0.90f, 0.74f, 0.42f, 1f);
        private static readonly Color FireLight = new Color(1f, 0.56f, 0.26f, 1f);
        private static readonly Color SmokeColor = new Color(0.10f, 0.08f, 0.07f, 1f);

        private GameObject root;
        private readonly Vector3 center;
        private readonly float targetRadius;
        private readonly int groundMask;
        private readonly float[] cosTable = new float[Segments];
        private readonly float[] sinTable = new float[Segments];
        private readonly float[] heights = new float[Segments];
        private readonly Vector3[] loop = new Vector3[Segments];
        private readonly Light[] lights = new Light[3];
        private LineRenderer charLine, bedLine, seamLine, targetLine;
        private ParticleSystem flames, smoke, embers;
        private SpriteRenderer[] seals;
        private SpriteRenderer[] halos;

        private float radius;
        private float clock;
        private float strength = 1f;
        private float flare;
        private int heightCursor;
        private int settle;
        private bool drawn;
        private bool igniting;
        private float igniteClock;
        private float igniteSeconds;
        private int igniteOrigin;
        private float targetAlpha;
        private float targetGoal;
        private bool targetShown;
        private float sealFlash = -1f;
        private float flameAcc, smokeAcc, emberAcc;
        private float headFlameAcc, headEmberAcc;

        internal static ArenaTrialsFireRingFx Create(Vector3 center, float radius, float targetRadius)
        {
            try
            {
                return new ArenaTrialsFireRingFx(center, radius, targetRadius);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 焚城火墙表现创建失败（不影响规则）: " + e.Message);
                return null;
            }
        }

        private ArenaTrialsFireRingFx(Vector3 center, float startRadius, float targetRadius)
        {
            this.center = center;
            this.targetRadius = targetRadius;
            radius = startRadius;
            try { groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask; }
            catch (Exception) { groundMask = Physics.DefaultRaycastLayers; }
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                cosTable[i] = Mathf.Cos(a);
                sinTable[i] = Mathf.Sin(a);
                heights[i] = GroundY(center.x + cosTable[i] * startRadius, center.z + sinTable[i] * startRadius, center.y);
            }
            root = new GameObject("BossRush_TrialFireRing");
            root.transform.position = center;
            Build();
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        private void Build()
        {
            Transform t = root.transform;
            // 叠放：焦墨在下、余烬床、熔缝与落点金丝在上
            charLine = ArenaTrialsFx.GroundLine(t, "Char", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft, CharWidth, 0);
            bedLine = ArenaTrialsFx.GroundLine(t, "Bed", BossRushFxBlend.Additive, BossRushFxKit.GainBright, BedWidth, 1);
            seamLine = ArenaTrialsFx.GroundLine(t, "Seam", BossRushFxBlend.Additive, BossRushFxKit.GainHot, SeamWidth, 2);
            targetLine = ArenaTrialsFx.GroundLine(t, "Target", BossRushFxBlend.Additive, BossRushFxKit.GainBright, TargetWidth, 2);
            BuildTarget();

            flames = CreateFlames(t);
            smoke = CreateSmoke(t);
            embers = CreateEmbers(t);

            for (int i = 0; i < lights.Length; i++)
            {
                GameObject go = new GameObject("FireLight" + i);
                go.transform.SetParent(t, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = FireLight;
                light.range = LightRange;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights[i] = light;
            }
        }

        /// <summary>落点：一线金丝（静止，只建一次）与十二枚符印（各带一团柔光），先全透明，ShowTarget 后淡入。</summary>
        private void BuildTarget()
        {
            Transform t = root.transform;
            if (targetLine != null)
            {
                targetLine.loop = true;
                targetLine.positionCount = Segments;
                for (int i = 0; i < Segments; i++)
                {
                    float x = center.x + cosTable[i] * targetRadius;
                    float z = center.z + sinTable[i] * targetRadius;
                    loop[i] = new Vector3(x, GroundY(x, z, center.y) + ArenaTrialsFx.GroundLift + 0.14f, z);
                }
                targetLine.SetPositions(loop);
            }
            seals = new SpriteRenderer[SealCount];
            halos = new SpriteRenderer[SealCount];
            for (int i = 0; i < SealCount; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / SealCount;
                float x = center.x + Mathf.Cos(a) * targetRadius;
                float z = center.z + Mathf.Sin(a) * targetRadius;
                Vector3 at = new Vector3(x, GroundY(x, z, center.y) + ArenaTrialsFx.GroundLift + 0.03f, z);
                SpriteRenderer halo = ArenaTrialsFx.FlatSoft(t, "SealHalo" + i, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                if (halo != null)
                {
                    halo.transform.position = at;
                    ArenaTrialsFx.SetRadius(halo, SealHaloRadius, ArenaTrialsFx.SoftRadius);
                }
                SpriteRenderer seal = ArenaTrialsFx.FlatShape(t, "Seal" + i, BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                if (seal != null)
                {
                    seal.transform.position = at + Vector3.up * 0.01f;
                    ArenaTrialsFx.SetRadius(seal, SealRadius, ArenaTrialsFx.RuneInnerRadius);
                    ArenaTrialsFx.Spin(seal, i * 17f);
                }
                halos[i] = halo;
                seals[i] = seal;
            }
        }

        /// <summary>火舌：拉伸的烟缕向上窜，白金芯 → 余烬 → 暗红淡尽，越往上越细。</summary>
        private static ParticleSystem CreateFlames(Transform parent)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("Flames", parent, Vector3.zero, material, 220, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.9f;
            main.startSize = 1.4f;
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(1f, 0.90f, 0.66f, 0.95f), new Color(1f, 0.52f, 0.20f, 0.8f),
                new Color(0.50f, 0.12f, 0.05f, 0f), 0.08f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.2f;
                renderer.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        /// <summary>浓烟：圈外翻起的墨色烟团，慢慢升、向外飘，淡入后长大淡出。</summary>
        private static ParticleSystem CreateSmoke(Transform parent)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("Smoke", parent, Vector3.zero, material, 90, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 3.6f;
            main.startSize = 3f;
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(SmokeColor.r, SmokeColor.g, SmokeColor.b, 0.30f),
                new Color(SmokeColor.r, SmokeColor.g, SmokeColor.b, 0.38f), new Color(SmokeColor.r, SmokeColor.g, SmokeColor.b, 0f), 0.25f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.4f));
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            ps.Play();
            return ps;
        }

        /// <summary>火星：被热风卷进圈里的细亮点，带一点拖长，慢慢熄灭。</summary>
        private static ParticleSystem CreateEmbers(Transform parent)
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("Embers", parent, Vector3.zero, material, 110, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 2f;
            main.startSize = 0.08f;
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(1f, 0.95f, 0.80f, 1f), new Color(ArenaTrialsFx.Ember.r, ArenaTrialsFx.Ember.g, ArenaTrialsFx.Ember.b, 0.85f),
                new Color(0.45f, 0.12f, 0.04f, 0f), 0f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.05f;
                renderer.lengthScale = 1f;
            }
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------
        // 规则层调进来的节点
        // ------------------------------------------------------------------

        /// <summary>开波燃起：从离玩家最近的那一点分两头绕圈，seconds 秒合拢。</summary>
        internal void Ignite(Vector3 player, bool hasPlayer, float seconds)
        {
            igniting = true;
            igniteClock = 0f;
            igniteSeconds = Mathf.Max(0.2f, seconds);
            igniteOrigin = hasPlayer ? IndexOf(Mathf.Atan2(player.z - center.z, player.x - center.x)) : 0;
            flare = 0.6f;
        }

        internal void ShowTarget()
        {
            targetGoal = 1f;
        }

        /// <summary>开始收紧：火墙一旺，离玩家最近的那段火光一闪。</summary>
        internal void Flare()
        {
            flare = 1f;
        }

        /// <summary>收到落点：符印一齐亮起、放大、盖印后隐去，金丝一起淡掉；火墙再旺一下。</summary>
        internal void Seal()
        {
            sealFlash = 0f;
            targetGoal = 0f;
            flare = Mathf.Max(flare, 0.8f);
        }

        internal void BeginRecede()
        {
            targetGoal = 0f;
            sealFlash = -1f;
        }

        /// <summary>收波时的整体强度（1 → 0）：线变淡、火舌变稀、灯变暗。</summary>
        internal void SetStrength(float value)
        {
            strength = Mathf.Clamp01(value);
        }

        /// <summary>圈外灼烧一跳：玩家身上冒一撮火星；刚出圈的第一跳再亮一下、响一声。</summary>
        internal void Scorch(Vector3 at, bool first)
        {
            try
            {
                BossRushFxBurst spark = BossRushFxKit.Sparks(ArenaTrialsFx.Ember, first ? 14 : 6);
                spark.Upward = true;
                spark.SpeedMin = 1.5f;
                spark.SpeedMax = 3.5f;
                spark.SizeMin = 0.05f;
                spark.SizeMax = 0.09f;
                spark.LifeMin = 0.4f;
                spark.LifeMax = 0.8f;
                spark.Drag = 2f;
                spark.ShapeRadius = 0.35f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.7f, spark);
                if (first)
                {
                    ArenaTrialsFx.Flash(at + Vector3.up * 1.2f, ArenaTrialsFx.Ember, 1.6f, 4.5f, 0.35f);
                    ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxEmberFlare, at);
                }
            }
            catch (Exception) { }
        }

        /// <summary>整棵退场（淡尽后销毁）；之后本对象不再可用。</summary>
        internal void Retire(float seconds)
        {
            GameObject r = root;
            root = null;
            if (r != null) ArenaTrialsFx.Retire(r, seconds);
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        internal void Step(float dt, float currentRadius, Vector3 player, bool hasPlayer)
        {
            if (root == null) return;
            clock += dt;
            flare = Mathf.MoveTowards(flare, 0f, dt * 0.8f);
            bool moved = Mathf.Abs(currentRadius - radius) > 0.001f;
            radius = currentRadius;
            if (moved) settle = Segments;
            if (settle > 0)
            {
                for (int k = 0; k < HeightSamplesPerFrame && settle > 0; k++, settle--)
                {
                    int i = heightCursor;
                    heightCursor = (heightCursor + 1) % Segments;
                    float y = GroundY(center.x + cosTable[i] * radius, center.z + sinTable[i] * radius, heights[i]);
                    heights[i] = Mathf.Lerp(heights[i], y, 0.6f);
                }
            }
            // 燃起结束的这一帧也要重画：上一帧画的还是开口的弧，不补这一笔就要留着缺口等到收紧
            bool wasIgniting = igniting;
            if (igniting)
            {
                igniteClock += dt;
                if (igniteClock >= igniteSeconds) igniting = false;
            }
            if (moved || wasIgniting || settle > 0 || !drawn) DrawLines();

            float bearing = 0f;
            float visibility = 0f;
            if (hasPlayer)
            {
                float dx = player.x - center.x;
                float dz = player.z - center.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                bearing = Mathf.Atan2(dz, dx);
                visibility = 1f - Mathf.Clamp01((Mathf.Abs(distance - radius) - VisibleNear) / (VisibleFar - VisibleNear));
            }
            TintLines();
            TickTarget(dt);
            Emit(dt, bearing, visibility * strength, player, hasPlayer);
            TickLights(bearing, visibility * strength);
        }

        // ------------------------------------------------------------------
        // 地上三笔
        // ------------------------------------------------------------------

        /// <summary>燃起期间点亮的半跨度（段数）：先慢后快（t^1.6），合拢时等于半圈。</summary>
        private int LitHalf()
        {
            if (!igniting) return Segments / 2;
            float t = Mathf.Clamp01(igniteClock / igniteSeconds);
            return Mathf.Min(Segments / 2, Mathf.FloorToInt(Mathf.Pow(t, 1.6f) * (Segments / 2 + 1)));
        }

        private void DrawLines()
        {
            drawn = true;
            int half = LitHalf();
            bool full = half >= Segments / 2;
            DrawLine(charLine, radius + CharOffset, 0.05f, half, full);
            DrawLine(bedLine, radius + BedOffset, 0.10f, half, full);
            DrawLine(seamLine, radius, 0.13f, half, full);
        }

        private void DrawLine(LineRenderer line, float r, float lift, int half, bool full)
        {
            if (line == null) return;
            if (full)
            {
                line.loop = true;
                if (line.positionCount != Segments) line.positionCount = Segments;
                for (int i = 0; i < Segments; i++) loop[i] = Point(i, r, lift);
                line.SetPositions(loop);
                return;
            }
            line.loop = false;
            int count = half * 2 + 1;
            line.positionCount = count;
            for (int k = 0; k < count; k++) line.SetPosition(k, Point(Wrap(igniteOrigin - half + k), r, lift));
        }

        private void TintLines()
        {
            float flicker = 0.82f + 0.18f * Mathf.PerlinNoise(clock * 1.7f, 0.3f);
            float boost = 1f + flare * 0.6f;
            ArenaTrialsFx.Tint(charLine, CharColor, 0.6f * strength);
            ArenaTrialsFx.Tint(bedLine, ArenaTrialsFx.EmberDeep, 0.55f * strength * flicker * boost);
            ArenaTrialsFx.Tint(seamLine, SeamColor, 0.85f * strength * (0.9f + 0.1f * flicker) * boost);
        }

        // ------------------------------------------------------------------
        // 落点金丝与符印
        // ------------------------------------------------------------------

        private void TickTarget(float dt)
        {
            if (seals == null) return;
            float scale = 1f;
            float peak = 0f;
            if (sealFlash >= 0f)
            {
                sealFlash += dt;
                float t = Mathf.Clamp01(sealFlash / SealFlashSeconds);
                // 先亮到顶（0.18 秒）再随放大慢慢隐去：盖印是「落下去」，不弹
                peak = t < 0.13f ? t / 0.13f : 1f - BossRushUI.SmoothStep((t - 0.13f) / 0.87f);
                scale = Mathf.Lerp(1f, 1.35f, BossRushUI.SmoothStep(t));
                if (t >= 1f) sealFlash = -1f;
            }
            targetAlpha = Mathf.MoveTowards(targetAlpha, targetGoal, dt / 1.2f);
            if (Mathf.Max(targetAlpha, peak) <= 0.001f && sealFlash < 0f)
            {
                if (targetShown) HideTarget();
                return;
            }
            targetShown = true;
            float pulse = 0.85f + 0.15f * Mathf.Sin(clock * 2.2f);
            ArenaTrialsFx.Tint(targetLine, GoldColor, 0.55f * targetAlpha * pulse);
            for (int i = 0; i < seals.Length; i++)
            {
                SpriteRenderer seal = seals[i];
                if (seal != null)
                {
                    ArenaTrialsFx.Tint(seal, GoldColor, Mathf.Clamp01(0.75f * targetAlpha * pulse + peak));
                    ArenaTrialsFx.Spin(seal, 14f * dt);
                    if (sealFlash >= 0f) ArenaTrialsFx.SetRadius(seal, SealRadius * scale, ArenaTrialsFx.RuneInnerRadius);
                }
                if (halos[i] != null) ArenaTrialsFx.Tint(halos[i], GoldColor, 0.22f * targetAlpha + 0.45f * peak);
            }
        }

        private void HideTarget()
        {
            targetShown = false;
            ArenaTrialsFx.Tint(targetLine, GoldColor, 0f);
            for (int i = 0; i < seals.Length; i++)
            {
                ArenaTrialsFx.Tint(seals[i], GoldColor, 0f);
                ArenaTrialsFx.Tint(halos[i], GoldColor, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 火舌、浓烟、火星、火光
        // ------------------------------------------------------------------

        private void Emit(float dt, float bearing, float amount, Vector3 player, bool hasPlayer)
        {
            if (igniting && hasPlayer) EmitHeads(dt, player);
            if (amount <= 0.001f) return;
            float half = Mathf.Min(Mathf.PI, ArcHalfLength / Mathf.Max(1f, radius));
            float arc = 2f * half * radius * amount * dt;
            float boost = 1f + flare * 1.5f;
            flameAcc += FlamesPerMeter * arc * boost;
            smokeAcc += SmokePerMeter * arc;
            emberAcc += EmbersPerMeter * arc * boost;
            int guard = 0;
            while (flameAcc >= 1f && guard++ < 24)
            {
                flameAcc -= 1f;
                float a = bearing + UnityEngine.Random.Range(-half, half);
                if (IsLit(a)) EmitFlame(a, radius + UnityEngine.Random.Range(-0.2f, 0.8f));
            }
            guard = 0;
            while (smokeAcc >= 1f && guard++ < 6)
            {
                smokeAcc -= 1f;
                float a = bearing + UnityEngine.Random.Range(-half, half);
                if (IsLit(a)) EmitSmoke(a);
            }
            guard = 0;
            while (emberAcc >= 1f && guard++ < 10)
            {
                emberAcc -= 1f;
                float a = bearing + UnityEngine.Random.Range(-half, half);
                if (IsLit(a)) EmitEmber(a);
            }
        }

        /// <summary>
        /// 燃起时的两个火头：看得见的那头多窜火舌、火星。按秒计量（不按帧），
        /// 高帧率不会把火舌的粒子上限占满、压掉弧上的常规火舌，低帧率也不稀。
        /// </summary>
        private void EmitHeads(float dt, Vector3 player)
        {
            int half = LitHalf();
            if (half >= Segments / 2) return;
            headFlameAcc = Mathf.Min(headFlameAcc + HeadFlamesPerSecond * dt, 4f);
            headEmberAcc = Mathf.Min(headEmberAcc + HeadEmbersPerSecond * dt, 2f);
            int flameCount = (int)headFlameAcc;
            int emberCount = (int)headEmberAcc;
            headFlameAcc -= flameCount;
            headEmberAcc -= emberCount;
            if (flameCount == 0 && emberCount == 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                int index = Wrap(igniteOrigin + side * half);
                float a = index * Mathf.PI * 2f / Segments;
                float x = center.x + Mathf.Cos(a) * radius;
                float z = center.z + Mathf.Sin(a) * radius;
                float dx = x - player.x;
                float dz = z - player.z;
                if (dx * dx + dz * dz > VisibleFar * VisibleFar) continue;
                for (int k = 0; k < flameCount; k++) EmitFlame(a, radius + UnityEngine.Random.Range(-0.1f, 0.4f));
                for (int k = 0; k < emberCount; k++) EmitEmber(a);
            }
        }

        private void EmitFlame(float angle, float r)
        {
            if (flames == null) return;
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            e.position = new Vector3(center.x + Mathf.Cos(angle) * r, HeightAt(angle) + 0.15f, center.z + Mathf.Sin(angle) * r);
            e.velocity = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(1.8f, 3.0f), UnityEngine.Random.Range(-0.3f, 0.3f));
            e.startSize = UnityEngine.Random.Range(0.9f, 1.9f);
            e.startLifetime = UnityEngine.Random.Range(0.6f, 1.05f);
            e.startColor = Color.white;
            flames.Emit(e, 1);
        }

        private void EmitSmoke(float angle)
        {
            if (smoke == null) return;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            float r = radius + UnityEngine.Random.Range(1f, 5.5f);
            float outward = UnityEngine.Random.Range(0.25f, 0.6f);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            e.position = new Vector3(center.x + c * r, HeightAt(angle) + 0.8f, center.z + s * r);
            e.velocity = new Vector3(c * outward, UnityEngine.Random.Range(0.6f, 1.1f), s * outward);
            e.startSize = UnityEngine.Random.Range(2.4f, 4f);
            e.startLifetime = UnityEngine.Random.Range(3f, 4.2f);
            e.rotation = UnityEngine.Random.Range(0f, 360f);
            e.startColor = Color.white;
            smoke.Emit(e, 1);
        }

        private void EmitEmber(float angle)
        {
            if (embers == null) return;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            float r = radius + UnityEngine.Random.Range(-0.3f, 1.2f);
            float inward = UnityEngine.Random.Range(0.5f, 1.4f);
            float drift = UnityEngine.Random.Range(-0.5f, 0.5f);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            e.position = new Vector3(center.x + c * r, HeightAt(angle) + 0.3f, center.z + s * r);
            // 热风往圈里卷：向心 + 一点切向
            e.velocity = new Vector3(-c * inward - s * drift, UnityEngine.Random.Range(1f, 2.2f), -s * inward + c * drift);
            e.startSize = UnityEngine.Random.Range(0.05f, 0.11f);
            e.startLifetime = UnityEngine.Random.Range(1.4f, 2.4f);
            e.startColor = Color.white;
            embers.Emit(e, 1);
        }

        /// <summary>三盏暖光：玩家方位上一盏、两侧沿弧各一盏，按噪声明灭；燃起时没烧到的那段不亮。</summary>
        private void TickLights(float bearing, float amount)
        {
            float step = LightSpacing / Mathf.Max(1f, radius);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                float a = bearing + (i - 1) * step;
                float intensity = amount > 0.001f && IsLit(a)
                    ? LightIntensity * amount * (0.78f + 0.22f * Mathf.PerlinNoise(clock * 3.1f, i * 5.7f)) * (1f + flare * 0.6f)
                    : 0f;
                if (intensity <= 0.01f)
                {
                    // 关灯前亮度清零：退场淡出按灯此刻的亮度起算，还会把关着的灯打开，不清零就会在旧位置闪一下
                    light.intensity = 0f;
                    light.enabled = false;
                    continue;
                }
                light.intensity = intensity;
                light.enabled = true;
                light.transform.position = new Vector3(center.x + Mathf.Cos(a) * (radius + 0.6f), HeightAt(a) + 1.8f,
                    center.z + Mathf.Sin(a) * (radius + 0.6f));
            }
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        private bool IsLit(float angle)
        {
            if (!igniting) return true;
            int half = LitHalf();
            int offset = Mathf.Abs(Wrap(IndexOf(angle) - igniteOrigin + Segments / 2) - Segments / 2);
            return offset <= half;
        }

        private Vector3 Point(int i, float r, float lift)
        {
            return new Vector3(center.x + cosTable[i] * r, heights[i] + ArenaTrialsFx.GroundLift + lift, center.z + sinTable[i] * r);
        }

        private float HeightAt(float angle)
        {
            return heights[IndexOf(angle)];
        }

        private static int IndexOf(float angle)
        {
            return Wrap(Mathf.RoundToInt(angle / (Mathf.PI * 2f) * Segments));
        }

        private static int Wrap(int index)
        {
            index %= Segments;
            return index < 0 ? index + Segments : index;
        }

        /// <summary>(x, z) 处的地面高度：从圈心高度上方 5 米往下打一条地面层射线；打不到就用 fallback。</summary>
        private float GroundY(float x, float z, float fallback)
        {
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(x, center.y + 5f, z), Vector3.down, out hit, 14f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    return hit.point.y;
                }
            }
            catch (Exception) { }
            return fallback;
        }
    }
}
