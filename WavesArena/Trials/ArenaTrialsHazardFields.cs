// ============================================================================
// ArenaTrialsHazardFields.cs - 出征令天灾本体（二）：「场地类」五种——墨潮横扫、狂风、旋涡、冰封、雾泽
// ============================================================================
// 推力（墨潮击退、狂风、旋涡）经 ArenaTrialsHazards.Push / Knock 累进同一张表；冰封 / 雾泽只报 Covers，
// 减速与 AI 致盲由 ArenaTrialsHazards 按进出统一挂上、统一还原。基类在 ArenaTrialsHazardRuns.cs。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // ----------------------------------------------------------------------
    // 墨潮横扫
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialInkTide : ArenaTrialHazardRun
    {
        private Vector3 start, dir, side;
        private float groundY;
        private float travel;
        private float gapCenter, gapHalf;
        private float halfWidth;
        private readonly LineRenderer[] fronts = new LineRenderer[2];
        private readonly LineRenderer[] crests = new LineRenderer[2];
        private readonly LineRenderer[] lanes = new LineRenderer[2];
        private readonly Transform[] wakeNodes = new Transform[2];
        private readonly ParticleSystem[] wakes = new ParticleSystem[2];
        private readonly float[] segFrom = new float[2];
        private readonly float[] segTo = new float[2];
        private readonly HashSet<Health> hit = new HashSet<Health>();
        private bool released;
        private float end;

        internal ArenaTrialInkTide(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.InkTide; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 anchor = player != null ? player.transform.position : Vector3.zero;
            CreateRoot("InkTide", anchor);
            dir = RandomFlatDirection();
            side = new Vector3(-dir.z, 0f, dir.x);
            travel = ArenaTrialsTuning.InkTideTravel;
            start = anchor - dir * travel;
            groundY = anchor.y + ArenaTrialsFx.GroundLift;
            halfWidth = ArenaTrialsTuning.InkTideWidth * 0.5f;
            gapHalf = ArenaTrialsTuning.InkTideGap * 0.5f;
            gapCenter = UnityEngine.Random.Range(-ArenaTrialsTuning.InkTideGapRange, ArenaTrialsTuning.InkTideGapRange);
            segFrom[0] = -halfWidth;
            segTo[0] = gapCenter - gapHalf;
            segFrom[1] = gapCenter + gapHalf;
            segTo[1] = halfWidth;
            end = ArenaTrialsTuning.InkTideWarnSeconds
                + (travel * 2f + ArenaTrialsTuning.InkTideDepth) / ArenaTrialsTuning.InkTideSpeed;

            AnimationCurve soft = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.08f, 1f), new Keyframe(0.92f, 1f), new Keyframe(1f, 0.35f));
            for (int i = 0; i < 2; i++)
            {
                fronts[i] = ArenaTrialsFx.GroundLine(Root.transform, "Front" + i, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft,
                    ArenaTrialsTuning.InkTideDepth, 1);
                crests[i] = ArenaTrialsFx.GroundLine(Root.transform, "Crest" + i, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft, 0.22f, 2);
                if (fronts[i] != null) { fronts[i].widthCurve = soft; fronts[i].positionCount = 2; }
                if (crests[i] != null) { crests[i].widthCurve = soft; crests[i].positionCount = 2; }
                // 缺口两边各一道金线，从起点往终点写出去：这就是那条活路
                lanes[i] = ArenaTrialsFx.GroundLine(Root.transform, "Lane" + i, BossRushFxBlend.Additive, BossRushFxKit.GainBright, 0.14f, 3);
                if (lanes[i] != null) lanes[i].positionCount = 2;
                wakeNodes[i] = Node("Wake" + i, start);
                wakes[i] = WakeEmitter(wakeNodes[i], segTo[i] - segFrom[i]);
            }
            PlaceFront(0f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBrushSweep, anchor);
        }

        /// <summary>浪头身后拖出的墨迹：贴地烟缕，沿浪头整段铺开，世界空间里留在原地慢慢散。</summary>
        private ParticleSystem WakeEmitter(Transform node, float length)
        {
            ParticleSystem ps = ArenaTrialsFx.Wisps(node, "Ink", ArenaTrialsFx.InkDark, 0.5f, 1f, 6f, 1.6f, 2.6f, 1.6f, true, 0f);
            if (ps == null) return null;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(Mathf.Max(0.5f, length), 0.1f, ArenaTrialsTuning.InkTideDepth * 0.6f);
            node.rotation = Quaternion.LookRotation(dir, Vector3.up);
            return ps;
        }

        private Vector3 Point(float along, float lateral)
        {
            Vector3 p = start + dir * along + side * lateral;
            p.y = groundY;
            return p;
        }

        private void PlaceFront(float along)
        {
            float crestAlong = along + ArenaTrialsTuning.InkTideDepth * 0.42f;
            for (int i = 0; i < 2; i++)
            {
                if (fronts[i] != null)
                {
                    fronts[i].SetPosition(0, Point(along, segFrom[i]));
                    fronts[i].SetPosition(1, Point(along, segTo[i]));
                }
                if (crests[i] != null)
                {
                    crests[i].SetPosition(0, Point(crestAlong, segFrom[i] + 0.4f));
                    crests[i].SetPosition(1, Point(crestAlong, segTo[i] - 0.4f));
                }
                if (wakeNodes[i] != null) wakeNodes[i].position = Point(along, (segFrom[i] + segTo[i]) * 0.5f);
            }
        }

        protected override bool Tick(float deltaTime)
        {
            float warn = ArenaTrialsTuning.InkTideWarnSeconds;
            float remaining = end - Age;
            float env = ArenaTrialsFx.Envelope(Age, 0f, remaining, 0.5f);
            float along;
            if (Age < warn)
            {
                float p = Age / warn;
                along = 0f;
                // 墨浪在起点聚起来（越来越浓、越来越厚），金线从起点往前写
                float gather = BossRushUI.SmoothStep(p);
                for (int i = 0; i < 2; i++)
                {
                    if (fronts[i] != null) fronts[i].widthMultiplier = ArenaTrialsTuning.InkTideDepth * (0.55f + 0.45f * gather);
                    ArenaTrialsFx.Tint(fronts[i], ArenaTrialsFx.InkDark, 0.82f * gather);
                    ArenaTrialsFx.Tint(crests[i], Paper, 0.32f * gather);
                    ArenaTrialsFx.SetRate(wakes[i], 3f);
                    WriteLane(i, BossRushUI.SmoothStep(Mathf.Clamp01(p * 1.5f)), 0.6f);
                }
            }
            else
            {
                if (!released)
                {
                    released = true;
                    ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, Point(travel, 0f));
                    for (int i = 0; i < 2; i++) ArenaTrialsFx.SetRate(wakes[i], 24f);
                }
                along = (Age - warn) * ArenaTrialsTuning.InkTideSpeed;
                float passed = Mathf.Clamp01(along / (travel * 2f));
                for (int i = 0; i < 2; i++)
                {
                    ArenaTrialsFx.Tint(fronts[i], ArenaTrialsFx.InkDark, 0.88f * env);
                    ArenaTrialsFx.Tint(crests[i], Paper, 0.38f * env);
                    WriteLane(i, 1f, 0.6f * (1f - BossRushUI.SmoothStep(passed)));
                }
                Sweep(along);
            }
            PlaceFront(along);
            return remaining > 0f;
        }

        private void WriteLane(int i, float progress, float alpha)
        {
            LineRenderer lane = lanes[i];
            if (lane == null) return;
            float lateral = i == 0 ? gapCenter - gapHalf : gapCenter + gapHalf;
            lane.SetPosition(0, Point(0f, lateral));
            lane.SetPosition(1, Point(Mathf.Max(0.05f, travel * 2f * progress), lateral));
            ArenaTrialsFx.Tint(lane, Gold, alpha);
        }

        private void Sweep(float along)
        {
            float depthHalf = ArenaTrialsTuning.InkTideDepth * 0.5f;
            List<Health> list = Hazards.Victims(Point(along, 0f), halfWidth + depthHalf + 1f);
            for (int i = 0; i < list.Count; i++)
            {
                Health health = list[i];
                if (health == null || hit.Contains(health)) continue;
                Vector3 at = ArenaTrialsHazards.PositionOf(health);
                Vector3 rel = at - start;
                float a = Vector3.Dot(rel, dir);
                float lateral = Vector3.Dot(rel, side);
                if (Mathf.Abs(a - along) > depthHalf || Mathf.Abs(lateral) > halfWidth) continue;
                if (Mathf.Abs(lateral - gapCenter) <= gapHalf) continue;
                // 翻滚中的不吃（Hurt 返回 false），浪头还压着的话下一帧照样能卷到
                if (!ArenaTrialsHazards.HurtShare(health, ArenaTrialsTuning.InkTideDamage, at - dir, ElementTypes.physics, true)) continue;
                hit.Add(health);
                CharacterMainControl character = health.TryGetCharacter();
                if (character != null)
                {
                    Hazards.Knock(character, dir * ArenaTrialsTuning.InkTideKnockback, ArenaTrialsTuning.InkTideKnockSeconds);
                }
                BossRushFxBurst splash = BossRushFxKit.Dust(new Color(0.08f, 0.07f, 0.07f, 0.55f), 5);
                splash.SpeedMin = 2.5f;
                splash.SpeedMax = 4f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.1f, splash);
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.8f, BossRushFxKit.Glint(Paper, 1.1f, 0.2f));
            }
        }
    }

    // ----------------------------------------------------------------------
    // 狂风
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialGale : ArenaTrialHazardRun
    {
        private static readonly Color Streak = new Color(0.90f, 0.88f, 0.82f, 1f);
        private static readonly Color Autumn = new Color(0.62f, 0.47f, 0.30f, 1f);

        private Vector3 dir;
        private ParticleSystem streaks;
        private ParticleSystem leaves;
        private float nextGust;
        private float gustAge = -1f;
        private int gusts;

        internal ArenaTrialGale(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Gale; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            CreateRoot("Gale", at);
            dir = RandomFlatDirection();
            nextGust = ArenaTrialsTuning.GaleLead;
            streaks = Flow("Streaks", BossRushParticleShape.Wisp, Streak, 0.22f, 13f, 18f, 0.10f, 0.18f, 1.3f, 80);
            if (streaks != null)
            {
                ParticleSystemRenderer renderer = streaks.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    // 拉长成风痕：细、长、淡，不是一团团的烟
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.12f;
                    renderer.lengthScale = 3f;
                    renderer.cameraVelocityScale = 0f;
                }
            }
            leaves = Flow("Leaves", BossRushParticleShape.Leaf, Autumn, 0.85f, 7f, 10f, 0.16f, 0.26f, 1.9f, 24);
            if (leaves != null)
            {
                ParticleSystem.RotationOverLifetimeModule spin = leaves.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
                ParticleSystem.MainModule main = leaves.main;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = 0.08f;
            }
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBrushSweep, at);
        }

        /// <summary>上风处一大片盒形发射区，粒子顺风吹过玩家四周（世界空间，发射区跟着玩家走）。</summary>
        private ParticleSystem Flow(string name, BossRushParticleShape shape, Color color, float alpha, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float life, int max)
        {
            Material material = BossRushFxKit.GetShapeMaterial(shape, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, Root.transform, -dir * 11f + Vector3.up * 1.2f, material, max, true);
            if (ps == null) return null;
            ps.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            ParticleSystem.ShapeModule shapeModule = ps.shape;
            shapeModule.shapeType = ParticleSystemShapeType.Box;
            shapeModule.scale = new Vector3(26f, 2.4f, 4f);
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(color.r, color.g, color.b, alpha), new Color(color.r, color.g, color.b, alpha),
                new Color(color.r, color.g, color.b, 0f), 0.2f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }

        protected override bool Tick(float deltaTime)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && Root != null) Root.transform.position = player.transform.position;
            float lead = ArenaTrialsTuning.GaleLead;
            float ramp = BossRushUI.SmoothStep(Mathf.Clamp01(Age / lead));
            float fade = ArenaTrialsFx.Envelope(Age, 0f, ArenaTrialsTuning.GaleSeconds - Age, 0.8f);

            if (Age >= nextGust && Age < ArenaTrialsTuning.GaleSeconds - 0.6f)
            {
                nextGust = Age + ArenaTrialsTuning.GaleGustInterval;
                gustAge = 0f;
                gusts++;
                if (gusts % 2 == 1 && player != null) ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBrushSweep, player.transform.position);
            }
            float gust = 0f;
            if (gustAge >= 0f)
            {
                gustAge += deltaTime;
                if (gustAge < ArenaTrialsTuning.GaleGustSeconds) gust = Mathf.Sin(Mathf.PI * gustAge / ArenaTrialsTuning.GaleGustSeconds);
                else gustAge = -1f;
            }
            ArenaTrialsFx.SetRate(streaks, (10f + 30f * gust) * ramp * fade);
            ArenaTrialsFx.SetRate(leaves, (1.5f + 4f * gust) * ramp * fade);

            if (Age >= lead)
            {
                float strength = ArenaTrialsTuning.GaleSpeed * (ArenaTrialsTuning.GaleBreeze + (1f - ArenaTrialsTuning.GaleBreeze) * gust) * fade;
                if (strength > 0.01f)
                {
                    List<CharacterMainControl> list = Hazards.Bodies();
                    for (int i = 0; i < list.Count; i++) Hazards.Push(list[i], dir * strength);
                }
            }
            return Age < ArenaTrialsTuning.GaleSeconds;
        }
    }

    // ----------------------------------------------------------------------
    // 旋涡
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialVortex : ArenaTrialHazardRun
    {
        private static readonly Color Lilac = new Color(0.66f, 0.56f, 0.84f, 1f);
        private static readonly Color LilacCore = new Color(0.86f, 0.80f, 1f, 1f);

        private Vector3 center;
        private SpriteRenderer pool, outer, inner, core;
        private float tick;

        internal ArenaTrialVortex(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Vortex; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 around = player != null ? player.transform.position : Hazards.PickAnchor();
            // 不压在脚下：离玩家几米开外张开，拉力才有「被拽过去」的感觉
            center = Hazards.Place(around, 3.5f, 5.5f);
            CreateRoot("Vortex", center);
            float radius = ArenaTrialsTuning.VortexRadius;
            float coreRadius = ArenaTrialsTuning.VortexCoreRadius;
            Transform t = Root.transform;
            pool = ArenaTrialsFx.FlatSoft(t, "Pool", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            outer = ArenaTrialsFx.FlatShape(t, "Outer", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            inner = ArenaTrialsFx.FlatShape(t, "Inner", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            core = ArenaTrialsFx.FlatSoft(t, "Core", BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ArenaTrialsFx.SetRadius(pool, radius * 0.9f, ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.SetRadius(outer, radius, ArenaTrialsFx.RuneRadius);
            ArenaTrialsFx.SetRadius(inner, coreRadius * 1.3f, ArenaTrialsFx.RuneInnerRadius);
            ArenaTrialsFx.SetRadius(core, coreRadius, ArenaTrialsFx.SoftRadius);

            Spiral("Ink", BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft,
                new Color(ArenaTrialsFx.VortexInk.r, ArenaTrialsFx.VortexInk.g, ArenaTrialsFx.VortexInk.b, 0.5f), radius, 2.4f, 9f, 0.9f, 1.5f, 1.2f, true);
            Spiral("Glints", BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot,
                new Color(LilacCore.r, LilacCore.g, LilacCore.b, 0.9f), radius * 0.85f, 1.5f, 5f, 0.05f, 0.1f, 2.2f, false);

            GameObject lightNode = new GameObject("Glow");
            lightNode.transform.SetParent(t, false);
            lightNode.transform.localPosition = Vector3.up * 1f;
            Light light = lightNode.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.62f, 0.48f, 0.92f, 1f);
            light.range = radius;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, 1.6f, 1.2f, 0.15f, 2.2f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellHum, center);
        }

        /// <summary>从外缘一圈往圆心卷进去的粒子（本地空间：径向收拢 + 绕竖轴公转）。</summary>
        private void Spiral(string name, BossRushParticleShape shape, BossRushFxBlend blend, float gain, Color color, float radius,
            float life, float rate, float sizeMin, float sizeMax, float orbit, bool flat)
        {
            Material material = BossRushFxKit.GetShapeMaterial(shape, blend, gain);
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, Root.transform, Vector3.up * (flat ? 0.15f : 0.4f), material,
                Mathf.CeilToInt(rate * life * 1.4f) + 4, false);
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.ShapeModule shapeModule = ps.shape;
            shapeModule.shapeType = ParticleSystemShapeType.Circle;
            shapeModule.radius = radius;
            shapeModule.radiusThickness = 0f;
            shapeModule.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(-radius / life * 0.95f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(orbit);
            ParticleSystem.ColorOverLifetimeModule colors = ps.colorOverLifetime;
            colors.enabled = true;
            colors.color = BossRushFxKit.FadeGradient(new Color(color.r, color.g, color.b, color.a * 0.5f), color,
                new Color(color.r, color.g, color.b, 0f), 0.25f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && flat) renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ps.Play();
        }

        protected override bool Tick(float deltaTime)
        {
            float life = ArenaTrialsTuning.VortexSeconds;
            float env = ArenaTrialsFx.Envelope(Age, 1.2f, life - Age, 1.2f);
            ArenaTrialsFx.Tint(pool, ArenaTrialsFx.InkDark, 0.34f * env);
            ArenaTrialsFx.Tint(outer, Lilac, 0.38f * env);
            ArenaTrialsFx.Tint(inner, LilacCore, 0.75f * env);
            ArenaTrialsFx.Tint(core, Lilac, (0.32f + 0.1f * Mathf.Sin(Age * 4.2f)) * env);
            ArenaTrialsFx.Spin(outer, 18f * deltaTime);
            ArenaTrialsFx.Spin(inner, -80f * deltaTime);

            if (Age > 0.6f && Age < life - 0.6f)
            {
                float ramp = BossRushUI.SmoothStep(Mathf.Clamp01((Age - 0.6f) / 1f)) * env;
                float radius = ArenaTrialsTuning.VortexRadius;
                List<CharacterMainControl> list = Hazards.Bodies();
                for (int i = 0; i < list.Count; i++)
                {
                    Vector3 offset = center - list[i].transform.position;
                    offset.y = 0f;
                    float d = offset.magnitude;
                    if (d > radius || d < 0.35f) continue;
                    Vector3 toward = offset / d;
                    Vector3 tangent = new Vector3(-toward.z, 0f, toward.x);
                    float strength = ArenaTrialsTuning.VortexPull * (0.45f + 0.55f * (1f - d / radius)) * ramp;
                    Hazards.Push(list[i], (toward + tangent * ArenaTrialsTuning.VortexSwirl) * strength);
                }
            }
            tick += deltaTime;
            if (tick >= ArenaTrialsTuning.HazardTickSeconds)
            {
                tick -= ArenaTrialsTuning.HazardTickSeconds;
                if (Age > 1f && Age < life - 0.8f)
                {
                    Hazards.Hit(center, ArenaTrialsTuning.VortexCoreRadius,
                        ArenaTrialsTuning.VortexCoreDamagePerSecond * ArenaTrialsTuning.HazardTickSeconds, ElementTypes.physics, false);
                }
            }
            return Age < life;
        }
    }

    // ----------------------------------------------------------------------
    // 冰封
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialFrost : ArenaTrialHazardRun
    {
        private static readonly Color Rime = new Color(0.86f, 0.93f, 0.99f, 1f);

        private sealed class Patch
        {
            public Vector3 At;
            public SpriteRenderer Sheet, Ring, Inner;
        }

        private readonly List<Patch> patches = new List<Patch>(2);

        internal ArenaTrialFrost(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Frost; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 near = player != null ? player.transform.position : Hazards.PickAnchor();
            CreateRoot("Frost", near);
            float radius = ArenaTrialsTuning.FrostRadius;
            AddPatch(Hazards.Place(near, 0.5f, 2.5f));
            CharacterMainControl boss = Hazards.RandomLiveBoss();
            Vector3 second = Hazards.Place(boss != null ? boss.transform.position : near, 2f, ArenaTrialsTuning.HazardScatterRadius);
            if (ArenaTrials.FlatSqr(second, patches[0].At) > radius * radius * 1.96f) AddPatch(second);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxChimeStone, near);
        }

        private void AddPatch(Vector3 at)
        {
            float radius = ArenaTrialsTuning.FrostRadius;
            Patch patch = new Patch();
            patch.At = at;
            Transform node = Node("Patch" + patches.Count, at);
            patch.Sheet = ArenaTrialsFx.FlatSoft(node, "Sheet", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            patch.Ring = ArenaTrialsFx.FlatShape(node, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            patch.Inner = ArenaTrialsFx.FlatShape(node, "Inner", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(patch.Ring, radius, ArenaTrialsFx.RuneRadius);
            ArenaTrialsFx.SetRadius(patch.Inner, radius * 0.55f, ArenaTrialsFx.RuneInnerRadius);
            ArenaTrialsFx.SetRadius(patch.Sheet, 0.05f, ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.Spin(patch.Ring, UnityEngine.Random.Range(0f, 360f));
            ArenaTrialsFx.Wisps(node, "Mist", Rime, 0.2f, radius * 0.8f, 3.5f, 1.4f, 2.4f, 3.5f, true, 0.04f);
            ArenaTrialsFx.Motes(node, "Flakes", BossRushParticleShape.Snowflake, Rime, radius * 0.85f, 2.2f, 0.07f, 0.13f, 2.8f, 0.3f,
                BossRushFxBlend.Additive, BossRushFxKit.GainBright);

            BossRushFxBurst shards = new BossRushFxBurst();
            shards.Shape = BossRushParticleShape.Shard;
            shards.Blend = BossRushFxBlend.Additive;
            shards.Gain = BossRushFxKit.GainBright;
            shards.Count = 10;
            shards.SpeedMin = 3f;
            shards.SpeedMax = 6f;
            shards.SizeMin = 0.12f;
            shards.SizeMax = 0.22f;
            shards.LifeMin = 0.5f;
            shards.LifeMax = 0.9f;
            shards.Gravity = 1.2f;
            shards.Drag = 2f;
            shards.Stretch = 0.05f;
            shards.GrowTo = 0.6f;
            shards.Upward = true;
            shards.ShapeRadius = radius * 0.3f;
            shards.Core = new Color(1f, 1f, 1f, 1f);
            shards.Main = new Color(ArenaTrialsFx.Frost.r, ArenaTrialsFx.Frost.g, ArenaTrialsFx.Frost.b, 0.9f);
            shards.End = new Color(ArenaTrialsFx.Frost.r, ArenaTrialsFx.Frost.g, ArenaTrialsFx.Frost.b, 0f);
            BossRushFxKit.PlayBurst(at + Vector3.up * 0.2f, shards);

            Light light = node.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.72f, 0.86f, 1f, 1f);
            light.range = radius * 1.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, 0.9f, 0.9f, 0.1f, 3.4f);
            patches.Add(patch);
        }

        protected override bool Tick(float deltaTime)
        {
            float life = ArenaTrialsTuning.FrostSeconds;
            float radius = ArenaTrialsTuning.FrostRadius;
            float env = ArenaTrialsFx.Envelope(Age, 0.9f, life - Age, 1.4f);
            float spread = BossRushUI.SmoothStep(Mathf.Clamp01(Age / 0.9f));
            for (int i = 0; i < patches.Count; i++)
            {
                Patch patch = patches[i];
                ArenaTrialsFx.SetRadius(patch.Sheet, Mathf.Max(0.05f, radius * 1.02f * spread), ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.Tint(patch.Sheet, ArenaTrialsFx.Frost, 0.2f * env);
                ArenaTrialsFx.Tint(patch.Ring, Rime, 0.55f * env);
                ArenaTrialsFx.Tint(patch.Inner, Rime, 0.25f * env);
                ArenaTrialsFx.Spin(patch.Ring, 6f * deltaTime);
                ArenaTrialsFx.Spin(patch.Inner, -10f * deltaTime);
            }
            return Age < life;
        }

        internal override bool Covers(Vector3 position)
        {
            if (Age < 0.35f || Age > ArenaTrialsTuning.FrostSeconds - 0.6f) return false;
            float sq = ArenaTrialsTuning.FrostRadius * ArenaTrialsTuning.FrostRadius;
            for (int i = 0; i < patches.Count; i++)
            {
                if (ArenaTrials.FlatSqr(position, patches[i].At) <= sq) return true;
            }
            return false;
        }
    }

    // ----------------------------------------------------------------------
    // 雾泽
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialFogmire : ArenaTrialHazardRun
    {
        private Vector3 center;
        private SpriteRenderer pool, edge;
        private ParticleSystem banks, floor;
        private bool waning;

        internal ArenaTrialFogmire(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Fogmire; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 around = player != null ? player.transform.position : Hazards.PickAnchor();
            center = Hazards.Place(around, 0f, 3f);
            CreateRoot("Fogmire", center);
            float radius = ArenaTrialsTuning.FogmireRadius;
            Transform t = Root.transform;
            pool = ArenaTrialsFx.FlatSoft(t, "Pool", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            // 边界用一圈淡墨符环画出来：雾再浓也知道边在哪
            edge = ArenaTrialsFx.FlatShape(t, "Edge", BossRushParticleShape.RuneOuter, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(pool, radius * 1.05f, ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.SetRadius(edge, radius, ArenaTrialsFx.RuneRadius);
            banks = ArenaTrialsFx.Wisps(t, "Banks", ArenaTrialsFx.Fog, 0.55f, radius * 0.85f, 8f, 3.2f, 5f, 5f, false, 0.06f);
            floor = ArenaTrialsFx.Wisps(t, "Floor", ArenaTrialsFx.Fog, 0.38f, radius * 0.9f, 4f, 2.4f, 3.6f, 4.5f, true, 0.02f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxTempleBell, center);
        }

        protected override bool Tick(float deltaTime)
        {
            float life = ArenaTrialsTuning.FogmireSeconds;
            float env = ArenaTrialsFx.Envelope(Age, 2f, life - Age, 2.2f);
            ArenaTrialsFx.Tint(pool, ArenaTrialsFx.Fog, 0.34f * env);
            ArenaTrialsFx.Tint(edge, ArenaTrialsFx.InkDark, 0.22f * env);
            ArenaTrialsFx.Spin(edge, 4f * deltaTime);
            if (!waning && Age >= life - 2.6f)
            {
                waning = true;
                ArenaTrialsFx.SetRate(banks, 0f);
                ArenaTrialsFx.SetRate(floor, 0f);
            }
            return Age < life;
        }

        internal override bool Covers(Vector3 position)
        {
            if (Age < 1.2f || Age > ArenaTrialsTuning.FogmireSeconds - 1.2f) return false;
            float radius = ArenaTrialsTuning.FogmireRadius;
            return ArenaTrials.FlatSqr(position, center) <= radius * radius;
        }
    }
}
