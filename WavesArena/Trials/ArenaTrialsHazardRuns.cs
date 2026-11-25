// ============================================================================
// ArenaTrialsHazardRuns.cs - 出征令天灾本体（一）：基类与「落点类」四种——陨火雨、落雷、毒雾、地裂熔缝
// ============================================================================
// 排程、落点、伤害、推拉与地形状态都在 ArenaTrialsHazards（本文件只管各天灾自己的表现与结算时机）；
// 「场地类」五种（墨潮、狂风、旋涡、冰封、雾泽）在 ArenaTrialsHazardFields.cs。
// 质感口径见 ArenaTrialsHazards / ArenaTrialsFx 文件头。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // ======================================================================
    // 天灾本体
    // ======================================================================

    internal abstract class ArenaTrialHazardRun
    {
        protected readonly ArenaTrialsHazards Hazards;
        protected GameObject Root;
        protected float Age;

        protected ArenaTrialHazardRun(ArenaTrialsHazards hazards) { Hazards = hazards; }

        internal abstract ArenaTrialHazard Kind { get; }

        internal abstract void Begin();

        /// <summary>每帧；返回 false 表示演完（由 Hazards 收掉）。</summary>
        internal bool Step(float deltaTime)
        {
            Age += deltaTime;
            return Tick(deltaTime);
        }

        protected abstract bool Tick(float deltaTime);

        /// <summary>冰封 / 雾泽的范围判断；其余天灾不占地形。</summary>
        internal virtual bool Covers(Vector3 position) { return false; }

        /// <summary>收掉：表现自然淡出（不一帧消失），此后不再结算。</summary>
        internal void Retire(float seconds)
        {
            GameObject root = Root;
            Root = null;
            if (root != null) ArenaTrialsFx.Retire(root, seconds);
        }

        protected void CreateRoot(string name, Vector3 at)
        {
            Root = new GameObject("BossRush_TrialHazard_" + name);
            Root.transform.position = at;
        }

        protected Transform Node(string name, Vector3 at)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = at;
            return go.transform;
        }

        protected static Vector3 RandomFlatDirection()
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        protected static readonly Color Ash = new Color(0.20f, 0.18f, 0.17f, 0.5f);
        protected static readonly Color Gold = new Color(0.96f, 0.80f, 0.46f, 1f);
        protected static readonly Color Paper = new Color(0.93f, 0.89f, 0.80f, 1f);
    }

    // ----------------------------------------------------------------------
    // 陨火雨
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialMeteorRain : ArenaTrialHazardRun
    {
        private const float StarFlight = 0.38f;
        private const float ScorchSeconds = 2.4f;

        private sealed class Drop
        {
            public Vector3 At;
            public float Delay;
            public SpriteRenderer Ring, Inner, Fill, Scorch;
            public bool Launched, Landed;
            public float LandedAge;
        }

        private readonly List<Drop> drops = new List<Drop>(10);
        private int landed;
        private float lastLanding;

        internal ArenaTrialMeteorRain(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Meteor; } }

        internal override void Begin()
        {
            Vector3 anchor = Hazards.PickAnchor();
            CreateRoot("Meteor", anchor);
            int count = UnityEngine.Random.Range(ArenaTrialsTuning.MeteorCountMin, ArenaTrialsTuning.MeteorCountMax + 1);
            float delay = 0f;
            float radius = ArenaTrialsTuning.MeteorRadius;
            for (int i = 0; i < count; i++)
            {
                Drop drop = new Drop();
                drop.At = i == 0 ? Hazards.Place(anchor, 0f, 1.5f) : Hazards.Place(anchor, 1.5f, ArenaTrialsTuning.MeteorSpread);
                drop.Delay = delay;
                // 一颗接一颗地亮、一颗接一颗地砸：读得出节奏，不是同一帧满地开花
                delay += UnityEngine.Random.Range(0.12f, 0.24f);
                Transform node = Node("Drop" + i, drop.At);
                drop.Scorch = ArenaTrialsFx.FlatSoft(node, "Scorch", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
                drop.Fill = ArenaTrialsFx.FlatSoft(node, "Fill", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                drop.Ring = ArenaTrialsFx.FlatShape(node, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                drop.Inner = ArenaTrialsFx.FlatShape(node, "Inner", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                ArenaTrialsFx.SetRadius(drop.Scorch, radius * 0.85f, ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.SetRadius(drop.Ring, radius, ArenaTrialsFx.RuneRadius);
                ArenaTrialsFx.SetRadius(drop.Inner, radius * 0.62f, ArenaTrialsFx.RuneInnerRadius);
                ArenaTrialsFx.SetRadius(drop.Fill, 0.05f, ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.Spin(drop.Ring, UnityEngine.Random.Range(0f, 360f));
                drops.Add(drop);
            }
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxEmberFlare, anchor);
        }

        protected override bool Tick(float deltaTime)
        {
            float warn = ArenaTrialsTuning.MeteorWarnSeconds;
            float radius = ArenaTrialsTuning.MeteorRadius;
            for (int i = 0; i < drops.Count; i++)
            {
                Drop drop = drops[i];
                float t = Age - drop.Delay;
                if (t < 0f) continue;
                if (!drop.Landed)
                {
                    float p = Mathf.Clamp01(t / warn);
                    float a = ArenaTrialsFx.Envelope(t, 0.22f, 1f, 0f);
                    ArenaTrialsFx.Tint(drop.Ring, ArenaTrialsFx.Ember, (0.55f + 0.35f * p) * a);
                    ArenaTrialsFx.Tint(drop.Inner, Gold, 0.4f * a);
                    ArenaTrialsFx.Spin(drop.Ring, 22f * deltaTime);
                    ArenaTrialsFx.Spin(drop.Inner, -48f * deltaTime);
                    ArenaTrialsFx.SetRadius(drop.Fill, Mathf.Max(0.05f, radius * BossRushUI.SmoothStep(p)), ArenaTrialsFx.SoftRadius);
                    ArenaTrialsFx.Tint(drop.Fill, ArenaTrialsFx.EmberDeep, (0.14f + 0.3f * p) * a);
                    if (!drop.Launched && t >= warn - StarFlight)
                    {
                        drop.Launched = true;
                        ArenaTrialsFx.FallingStar(drop.At, ArenaTrialsFx.Ember, StarFlight);
                    }
                    if (t >= warn) Land(drop);
                }
                else
                {
                    // 落点留一块焦痕：亮环 0.3 秒褪掉、热光 0.6 秒退成墨色、焦痕 2.4 秒褪尽
                    float s = Age - drop.LandedAge;
                    ArenaTrialsFx.Tint(drop.Ring, ArenaTrialsFx.Ember, 0.9f * Mathf.Clamp01(1f - s / 0.3f));
                    ArenaTrialsFx.Tint(drop.Inner, Gold, 0f);
                    ArenaTrialsFx.Tint(drop.Fill, ArenaTrialsFx.Ember, 0.5f * Mathf.Clamp01(1f - s / 0.6f));
                    float k = Mathf.Clamp01(1f - s / ScorchSeconds);
                    ArenaTrialsFx.Tint(drop.Scorch, ArenaTrialsFx.InkDark, 0.5f * k * k);
                }
            }
            return landed < drops.Count || Age - lastLanding < ScorchSeconds;
        }

        private void Land(Drop drop)
        {
            drop.Landed = true;
            drop.LandedAge = Age;
            landed++;
            lastLanding = Age;
            float radius = ArenaTrialsTuning.MeteorRadius;
            ArenaTrialsFx.Impact(drop.At, ArenaTrialsFx.Ember, radius, 12, Ash);
            Hazards.Hit(drop.At, radius, ArenaTrialsTuning.MeteorDamage, ElementTypes.fire, false);
            // 鼓声只给第一颗和每第三颗，不连成一片
            if (landed == 1 || landed % 3 == 0) ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, drop.At);
        }
    }

    // ----------------------------------------------------------------------
    // 落雷
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialLightning : ArenaTrialHazardRun
    {
        private sealed class Strike
        {
            public Vector3 At;
            public float MarkAge;
            public SpriteRenderer Ring, Inner, Fill, Scorch;
            public bool Struck;
            public float StruckAge;
            public float Seed;
        }

        private readonly List<Strike> strikes = new List<Strike>(3);
        private int struck;
        private float lastStruck;

        internal ArenaTrialLightning(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Lightning; } }

        internal override void Begin()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            CreateRoot("Lightning", player != null ? player.transform.position : Vector3.zero);
            Mark();
        }

        private void Mark()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Strike strike = new Strike();
            strike.MarkAge = Age;
            strike.Seed = UnityEngine.Random.value * 50f;
            Vector3 at = Root.transform.position;
            if (player != null)
            {
                Vector3 velocity = player.Velocity;
                velocity.y = 0f;
                // 追着玩家：锁的是「再过一会儿会到的地方」，直线跑不掉
                Vector3 lead = Vector3.ClampMagnitude(velocity * ArenaTrialsTuning.LightningLead, 2.2f);
                at = Hazards.Place(player.transform.position + lead, 0f, 0.4f);
            }
            strike.At = at;
            float radius = ArenaTrialsTuning.LightningRadius;
            Transform node = Node("Strike" + strikes.Count, at);
            strike.Scorch = ArenaTrialsFx.FlatSoft(node, "Scorch", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            strike.Fill = ArenaTrialsFx.FlatSoft(node, "Fill", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            strike.Ring = ArenaTrialsFx.FlatShape(node, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            strike.Inner = ArenaTrialsFx.FlatShape(node, "Inner", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ArenaTrialsFx.SetRadius(strike.Scorch, radius * 0.8f, ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.SetRadius(strike.Ring, radius, ArenaTrialsFx.RuneRadius);
            ArenaTrialsFx.SetRadius(strike.Inner, radius * 0.55f, ArenaTrialsFx.RuneInnerRadius);
            ArenaTrialsFx.SetRadius(strike.Fill, 0.05f, ArenaTrialsFx.SoftRadius);
            strikes.Add(strike);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellDing, at);
        }

        protected override bool Tick(float deltaTime)
        {
            if (strikes.Count < ArenaTrialsTuning.LightningStrikes && Age >= strikes.Count * ArenaTrialsTuning.LightningInterval) Mark();
            float warn = ArenaTrialsTuning.LightningWarnSeconds;
            float radius = ArenaTrialsTuning.LightningRadius;
            for (int i = 0; i < strikes.Count; i++)
            {
                Strike strike = strikes[i];
                float t = Age - strike.MarkAge;
                if (!strike.Struck)
                {
                    float p = Mathf.Clamp01(t / warn);
                    float a = ArenaTrialsFx.Envelope(t, 0.15f, 1f, 0f);
                    // 蓄电：越临近越亮、越抖
                    float crackle = 1f - 0.25f * p * Mathf.PerlinNoise(Age * 18f, strike.Seed);
                    ArenaTrialsFx.Tint(strike.Ring, ArenaTrialsFx.Silver, (0.5f + 0.45f * p) * a * crackle);
                    ArenaTrialsFx.Tint(strike.Inner, ArenaTrialsFx.Silver, 0.35f * a);
                    ArenaTrialsFx.Spin(strike.Ring, -30f * deltaTime);
                    ArenaTrialsFx.Spin(strike.Inner, 90f * deltaTime);
                    ArenaTrialsFx.SetRadius(strike.Fill, Mathf.Max(0.05f, radius * BossRushUI.SmoothStep(p)), ArenaTrialsFx.SoftRadius);
                    ArenaTrialsFx.Tint(strike.Fill, ArenaTrialsFx.Silver, (0.12f + 0.28f * p) * a);
                    if (t >= warn) Fire(strike);
                }
                else
                {
                    float s = Age - strike.StruckAge;
                    ArenaTrialsFx.Tint(strike.Ring, ArenaTrialsFx.Silver, 0.9f * Mathf.Clamp01(1f - s / 0.25f));
                    ArenaTrialsFx.Tint(strike.Inner, ArenaTrialsFx.Silver, 0f);
                    ArenaTrialsFx.Tint(strike.Fill, ArenaTrialsFx.Silver, 0.45f * Mathf.Clamp01(1f - s / 0.4f));
                    float k = Mathf.Clamp01(1f - s / 1.4f);
                    ArenaTrialsFx.Tint(strike.Scorch, ArenaTrialsFx.InkDark, 0.45f * k * k);
                }
            }
            return struck < ArenaTrialsTuning.LightningStrikes || Age - lastStruck < 1.4f;
        }

        private void Fire(Strike strike)
        {
            strike.Struck = true;
            strike.StruckAge = Age;
            struck++;
            lastStruck = Age;
            float radius = ArenaTrialsTuning.LightningRadius;
            ArenaTrialsFx.LightningBolt(strike.At);
            ArenaTrialsFx.Impact(strike.At, ArenaTrialsFx.Silver, radius, 10, Ash);
            ArenaTrialsFx.Flash(strike.At + Vector3.up * 3f, ArenaTrialsFx.Silver, 6f, 12f, 0.35f);
            ArenaExpeditionHud.FlashAmbience(0.42f);
            Hazards.Hit(strike.At, radius, ArenaTrialsTuning.LightningDamage, ElementTypes.electricity, false);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, strike.At);
        }
    }

    // ----------------------------------------------------------------------
    // 毒雾
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialMiasma : ArenaTrialHazardRun
    {
        private static readonly Color Bile = new Color(0.66f, 0.74f, 0.44f, 1f);

        private sealed class Cloud
        {
            public Vector3 At;
            public SpriteRenderer Pool, Ring;
            public ParticleSystem Fumes, Bubbles;
        }

        private readonly List<Cloud> clouds = new List<Cloud>(3);
        private float life;
        private float tick;
        private bool waning;

        internal ArenaTrialMiasma(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Miasma; } }

        internal override void Begin()
        {
            Vector3 anchor = Hazards.PickAnchor();
            CreateRoot("Miasma", anchor);
            life = ArenaTrialsTuning.MiasmaSeconds * Hazards.DurationFactor;
            float radius = ArenaTrialsTuning.MiasmaRadius;
            int count = UnityEngine.Random.Range(ArenaTrialsTuning.MiasmaCloudsMin, ArenaTrialsTuning.MiasmaCloudsMax + 1);
            for (int i = 0; i < count; i++)
            {
                Cloud cloud = new Cloud();
                cloud.At = i == 0 ? Hazards.Place(anchor, 1f, 3f) : Hazards.Place(anchor, 3.5f, ArenaTrialsTuning.HazardScatterRadius);
                Transform node = Node("Cloud" + i, cloud.At);
                cloud.Pool = ArenaTrialsFx.FlatSoft(node, "Pool", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
                cloud.Ring = ArenaTrialsFx.FlatShape(node, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                ArenaTrialsFx.SetRadius(cloud.Pool, radius * 1.05f, ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.SetRadius(cloud.Ring, radius, ArenaTrialsFx.RuneRadius);
                ArenaTrialsFx.Spin(cloud.Ring, UnityEngine.Random.Range(0f, 360f));
                cloud.Fumes = ArenaTrialsFx.Wisps(node, "Fumes", ArenaTrialsFx.Miasma, 0.4f, radius * 0.75f, 5f, 2f, 3.2f, 4.5f, false, 0.18f);
                cloud.Bubbles = ArenaTrialsFx.Motes(node, "Bubbles", BossRushParticleShape.Bubble, Bile, radius * 0.7f, 1.6f, 0.12f, 0.22f,
                    2.6f, 0.45f, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                clouds.Add(cloud);
            }
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxSoulRise, anchor);
        }

        protected override bool Tick(float deltaTime)
        {
            float env = ArenaTrialsFx.Envelope(Age, 1.6f, life - Age, 2f);
            for (int i = 0; i < clouds.Count; i++)
            {
                Cloud cloud = clouds[i];
                ArenaTrialsFx.Tint(cloud.Pool, ArenaTrialsFx.Miasma, 0.3f * env);
                ArenaTrialsFx.Tint(cloud.Ring, Bile, 0.38f * env);
                ArenaTrialsFx.Spin(cloud.Ring, 8f * deltaTime);
            }
            if (!waning && Age >= life - 2f)
            {
                waning = true;
                for (int i = 0; i < clouds.Count; i++)
                {
                    ArenaTrialsFx.SetRate(clouds[i].Fumes, 0f);
                    ArenaTrialsFx.SetRate(clouds[i].Bubbles, 0f);
                }
            }
            tick += deltaTime;
            if (tick >= ArenaTrialsTuning.HazardTickSeconds)
            {
                tick -= ArenaTrialsTuning.HazardTickSeconds;
                // 刚聚起来的第一秒、散去前的最后一秒不结算（看得见才伤人）
                if (Age > 1f && Age < life - 1f)
                {
                    float share = ArenaTrialsTuning.MiasmaDamagePerSecond * ArenaTrialsTuning.HazardTickSeconds;
                    for (int i = 0; i < clouds.Count; i++)
                    {
                        Hazards.Hit(clouds[i].At, ArenaTrialsTuning.MiasmaRadius, share, ElementTypes.poison, false);
                    }
                }
            }
            return Age < life;
        }
    }

    // ----------------------------------------------------------------------
    // 地裂熔缝
    // ----------------------------------------------------------------------

    internal sealed class ArenaTrialRift : ArenaTrialHazardRun
    {
        private static readonly Color Cold = new Color(0.32f, 0.05f, 0.03f, 1f);
        private static readonly Color Molten = new Color(1f, 0.74f, 0.38f, 1f);

        private Vector3[] points;
        private Vector3 center;
        private LineRenderer charLine, glowLine, coreLine;
        private readonly List<ParticleSystem> embers = new List<ParticleSystem>(3);
        private float life;
        private float tick;
        private bool erupted;
        private bool cooling;
        private float seed;

        internal ArenaTrialRift(ArenaTrialsHazards hazards) : base(hazards) { }

        internal override ArenaTrialHazard Kind { get { return ArenaTrialHazard.Rift; } }

        internal override void Begin()
        {
            Vector3 anchor = Hazards.PickAnchor();
            center = Hazards.Place(anchor, 0.5f, 2.5f);
            CreateRoot("Rift", center);
            seed = UnityEngine.Random.value * 50f;
            life = ArenaTrialsTuning.RiftWarnSeconds + ArenaTrialsTuning.RiftSeconds * Hazards.DurationFactor;

            // 一道折线裂口：两端收尖、中段左右摆，点位逐个贴地（高差太大的退回中心高度）
            Vector3 dir = RandomFlatDirection();
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            const int count = 13;
            float phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float along = (t - 0.5f) * ArenaTrialsTuning.RiftLength;
                float lateral = 0f;
                if (i > 0 && i < count - 1)
                {
                    lateral = UnityEngine.Random.Range(-0.4f, 0.4f) + 0.35f * Mathf.Sin(t * Mathf.PI * 2f + phase);
                }
                Vector3 raw = center + dir * along + side * lateral;
                Vector3 ground;
                if (!SpawnPositionHelper.TrySnapToGround(raw, out ground, 0f, 2f) || Mathf.Abs(ground.y - center.y) > 2.5f)
                {
                    ground = new Vector3(raw.x, center.y, raw.z);
                }
                points[i] = ground + Vector3.up * ArenaTrialsFx.GroundLift;
            }

            float width = ArenaTrialsTuning.RiftWidth;
            AnimationCurve taper = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.14f, 1f), new Keyframe(0.86f, 1f), new Keyframe(1f, 0.2f));
            charLine = ArenaTrialsFx.GroundLine(Root.transform, "Char", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft, width * 1.3f, 0);
            glowLine = ArenaTrialsFx.GroundLine(Root.transform, "Glow", BossRushFxBlend.Additive, BossRushFxKit.GainSoft, width * 2.4f, 1);
            coreLine = ArenaTrialsFx.GroundLine(Root.transform, "Core", BossRushFxBlend.Additive, BossRushFxKit.GainHot, width * 0.42f, 2);
            LineRenderer[] lines = { charLine, glowLine, coreLine };
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                lines[i].widthCurve = taper;
                lines[i].positionCount = points.Length;
                lines[i].SetPositions(points);
            }
            int[] emberAt = { 3, 6, 9 };
            for (int i = 0; i < emberAt.Length; i++)
            {
                Transform node = Node("Embers" + i, points[emberAt[i]]);
                ParticleSystem ps = ArenaTrialsFx.Motes(node, "Embers", BossRushParticleShape.GlowDot, ArenaTrialsFx.Ember, width * 0.5f,
                    0.6f, 0.06f, 0.12f, 1.6f, 1.2f, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
                if (ps != null) embers.Add(ps);
                if (i != 1)
                {
                    Light light = node.gameObject.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = ArenaTrialsFx.Ember;
                    light.range = 4.5f;
                    light.intensity = 0f;
                    light.shadows = LightShadows.None;
                    BossRushFxLightFade.Attach(light, 2.2f, ArenaTrialsTuning.RiftWarnSeconds, 0.08f, 2.4f, 0.22f);
                }
            }
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxEmberFlare, center);
        }

        protected override bool Tick(float deltaTime)
        {
            float warn = ArenaTrialsTuning.RiftWarnSeconds;
            float heat;
            if (Age < warn) heat = 0.55f * BossRushUI.SmoothStep(Age / warn);
            else
            {
                if (!erupted) Erupt();
                float settle = Mathf.Clamp01((Age - warn) / 0.6f);
                heat = Mathf.Lerp(1f, 0.82f, settle) * (0.92f + 0.08f * Mathf.PerlinNoise(Age * 2.6f, seed));
            }
            float remaining = life - Age;
            if (remaining < 1.4f)
            {
                heat *= BossRushUI.SmoothStep(Mathf.Clamp01(remaining / 1.4f));
                if (!cooling)
                {
                    cooling = true;
                    for (int i = 0; i < embers.Count; i++) ArenaTrialsFx.SetRate(embers[i], 0f);
                }
            }
            float env = ArenaTrialsFx.Envelope(Age, 0.4f, remaining, 1.4f);
            ArenaTrialsFx.Tint(charLine, ArenaTrialsFx.InkDark, 0.72f * env);
            ArenaTrialsFx.Tint(glowLine, ArenaTrialsFx.EmberDeep, 0.32f * heat * env);
            ArenaTrialsFx.Tint(coreLine, Color.Lerp(Cold, Molten, heat), (0.45f + 0.55f * heat) * env);

            if (erupted && remaining > 1f)
            {
                tick += deltaTime;
                if (tick >= ArenaTrialsTuning.HazardTickSeconds)
                {
                    tick -= ArenaTrialsTuning.HazardTickSeconds;
                    Burn();
                }
            }
            return Age < life;
        }

        private void Erupt()
        {
            erupted = true;
            for (int i = 0; i < embers.Count; i++) ArenaTrialsFx.SetRate(embers[i], 2.2f);
            int[] burstAt = { 2, 6, 10 };
            for (int i = 0; i < burstAt.Length; i++)
            {
                Vector3 at = points[burstAt[i]];
                BossRushFxBurst spark = BossRushFxKit.Sparks(ArenaTrialsFx.Ember, 9);
                spark.Upward = true;
                spark.SpeedMin = 3f;
                spark.SpeedMax = 7f;
                spark.Gravity = 1.2f;
                spark.LifeMin = 0.4f;
                spark.LifeMax = 0.8f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.1f, spark);
                BossRushFxKit.PlayBurst(at, BossRushFxKit.Dust(Ash, 4));
            }
            ArenaTrialsFx.Flash(center + Vector3.up * 1.2f, ArenaTrialsFx.Ember, 3.5f, 9f, 0.5f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, center);
        }

        private void Burn()
        {
            float reach = ArenaTrialsTuning.RiftWidth * 0.5f + 0.35f;
            List<Health> list = Hazards.Victims(center, ArenaTrialsTuning.RiftLength * 0.5f + 2f);
            float share = ArenaTrialsTuning.RiftDamagePerSecond * ArenaTrialsTuning.HazardTickSeconds;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 at = ArenaTrialsHazards.PositionOf(list[i]);
                if (FlatDistanceToPath(at) <= reach) ArenaTrialsHazards.HurtShare(list[i], share, at, ElementTypes.fire, false);
            }
        }

        private float FlatDistanceToPath(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = new Vector2(points[i].x, points[i].z);
                Vector2 b = new Vector2(points[i + 1].x, points[i + 1].z);
                Vector2 q = new Vector2(p.x, p.z);
                Vector2 ab = b - a;
                float len = ab.sqrMagnitude;
                float t = len > 0.0001f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / len) : 0f;
                float d = (q - (a + ab * t)).magnitude;
                if (d < best) best = d;
            }
            return best;
        }
    }
}
