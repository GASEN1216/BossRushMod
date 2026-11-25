// ============================================================================
// ArenaTrialsAffixFx.cs - 出征令首领词缀的表现：本体脚下的词缀符印、身体的墨影、双生丝线、
// 拉起 / 召唤符阵，以及死相两种（亡魂、殉爆）的本体，和各种一次性演出
// ============================================================================
// 质感口径同天灾（ArenaTrialsFx 文件头）：贴地符环画在真实半径上、粒子少而亮、颜色压在冷银 / 古金 / 墨紫 / 朱砂里；
// 一次只演一件事——符印常驻但很淡，真正的「大动静」只在分裂、破罩、狂暴、拉起、殉爆这几个节点上。
// 声音只用水墨套件里的钟、磬、纸、笔、火、鼓。
// 所有常驻物都是一棵不挂在角色身上的根（跟随由词缀部分每帧喂位置）：角色死亡被官方 SetActive(false) 时
// 表现不会跟着一帧消失，而是走 ArenaTrialsFx.Retire 自然退场。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>挂词缀的首领脚下那枚符印：内环取第一条词缀的颜色、外环取最后一条，护驾时多一圈金罩与牵向亲卫的金线，狂暴后转朱。</summary>
    internal sealed class ArenaAffixAura
    {
        private GameObject root;
        private SpriteRenderer sigil;
        private SpriteRenderer halo;
        private SpriteRenderer glow;
        private SpriteRenderer guardRing;
        private ParticleSystem motes;
        private ParticleSystem embers;
        private Light light;
        private readonly List<LineRenderer> guardLines = new List<LineRenderer>(3);
        private List<CharacterMainControl> guards;
        private Color inner;
        private Color outer;
        private float guardLevel;
        private float frenzyLevel;
        private float age;

        internal static ArenaAffixAura Create(CharacterMainControl boss, List<ArenaAffix> affixes)
        {
            if (boss == null || affixes == null || affixes.Count == 0) return null;
            try
            {
                ArenaAffixAura aura = new ArenaAffixAura();
                aura.Build(boss.transform.position, affixes);
                return aura;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀符印创建失败: " + e.Message);
                return null;
            }
        }

        private void Build(Vector3 at, List<ArenaAffix> affixes)
        {
            inner = ArenaTrialsTuning.AffixColor(affixes[0]);
            outer = ArenaTrialsTuning.AffixColor(affixes[affixes.Count - 1]);
            root = new GameObject("BossRush_AffixAura");
            root.transform.position = at;
            glow = ArenaTrialsFx.FlatSoft(root.transform, "Glow", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(glow, 1.7f, ArenaTrialsFx.SoftRadius);
            halo = ArenaTrialsFx.FlatShape(root.transform, "Halo", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(halo, 1.45f, ArenaTrialsFx.RuneRadius);
            sigil = ArenaTrialsFx.FlatShape(root.transform, "Sigil", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(sigil, 1.05f, ArenaTrialsFx.RuneInnerRadius);
            guardRing = ArenaTrialsFx.FlatShape(root.transform, "Guard", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ArenaTrialsFx.SetRadius(guardRing, 1.2f, ArenaTrialsFx.RuneRadius);
            motes = ArenaTrialsFx.Motes(root.transform, "Motes", BossRushParticleShape.GlowDot, inner, 0.55f, 2.2f, 0.05f, 0.09f,
                1.8f, 1.1f, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            embers = ArenaTrialsFx.Motes(root.transform, "Embers", BossRushParticleShape.GlowDot, ArenaTrialsFx.EmberDeep, 0.6f, 0f,
                0.05f, 0.1f, 1.2f, 1.8f, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(inner, outer, 0.5f);
            light.range = 3.4f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, 0.6f, 0.8f, 0.18f, 3.2f);
        }

        internal void SetGuards(List<CharacterMainControl> list) { guards = list; }

        internal void Follow(CharacterMainControl boss, bool guardUp, bool frenzied, float dt)
        {
            if (root == null || boss == null) return;
            age += dt;
            root.transform.position = boss.transform.position;
            float appear = BossRushUI.SmoothStep(Mathf.Clamp01(age / 0.9f));
            guardLevel = Mathf.MoveTowards(guardLevel, guardUp ? 1f : 0f, dt * 2.5f);
            frenzyLevel = Mathf.MoveTowards(frenzyLevel, frenzied ? 1f : 0f, dt * 1.6f);
            Color sigilColor = Color.Lerp(inner, ArenaTrialsFx.EmberDeep, frenzyLevel);
            ArenaTrialsFx.Tint(sigil, sigilColor, 0.36f * appear);
            ArenaTrialsFx.Tint(halo, Color.Lerp(outer, ArenaTrialsFx.EmberDeep, frenzyLevel * 0.6f), 0.15f * appear);
            ArenaTrialsFx.Tint(glow, sigilColor, (0.08f + 0.06f * frenzyLevel) * appear);
            float breathe = 0.85f + 0.15f * Mathf.Sin(age * 2.1f);
            ArenaTrialsFx.Tint(guardRing, ArenaTrialsTuning.AffixColor(ArenaAffix.Escort), 0.42f * guardLevel * breathe * appear);
            ArenaTrialsFx.Spin(sigil, dt * (14f + 40f * frenzyLevel));
            ArenaTrialsFx.Spin(halo, -dt * 6f);
            ArenaTrialsFx.Spin(guardRing, dt * 9f);
            ArenaTrialsFx.SetRate(embers, 7f * frenzyLevel);
            if (light != null) light.color = Color.Lerp(Color.Lerp(inner, outer, 0.5f), ArenaTrialsFx.EmberDeep, frenzyLevel);
            UpdateGuardLines(boss.transform.position, dt);
        }

        /// <summary>首领到每名亲卫一根贴地金线（亲卫没了线就收）。</summary>
        private void UpdateGuardLines(Vector3 from, float dt)
        {
            int count = guards != null ? guards.Count : 0;
            while (guardLines.Count < count)
            {
                LineRenderer line = ArenaTrialsFx.GroundLine(root.transform, "GuardLine", BossRushFxBlend.Additive, BossRushFxKit.GainBright, 0.05f, 2);
                if (line == null) break;
                line.positionCount = 2;
                guardLines.Add(line);
            }
            Color gold = ArenaTrialsTuning.AffixColor(ArenaAffix.Escort);
            for (int i = 0; i < guardLines.Count; i++)
            {
                LineRenderer line = guardLines[i];
                CharacterMainControl guard = i < count ? guards[i] : null;
                bool alive = guard != null && guard.Health != null && !guard.Health.IsDead;
                if (!alive)
                {
                    ArenaTrialsFx.Tint(line, gold, 0f);
                    continue;
                }
                float lift = ArenaTrialsFx.GroundLift + 0.03f;
                line.SetPosition(0, from + Vector3.up * lift);
                line.SetPosition(1, guard.transform.position + Vector3.up * lift);
                ArenaTrialsFx.Tint(line, gold, 0.3f * guardLevel * (0.8f + 0.2f * Mathf.Sin(age * 3f + i)));
            }
        }

        internal void Retire(float seconds)
        {
            if (root != null) ArenaTrialsFx.Retire(root, seconds);
            root = null;
        }
    }

    /// <summary>词缀身体身上那层「不是本尊」的影：分身 / 双生冷银墨烟，亲卫一圈小金环，影从墨紫烟。</summary>
    internal sealed class ArenaAffixVeil
    {
        private GameObject root;
        private SpriteRenderer ring;
        private Color tone;
        private float age;

        internal static ArenaAffixVeil Create(CharacterMainControl body, ArenaAffixRole role)
        {
            if (body == null) return null;
            try
            {
                ArenaAffixVeil veil = new ArenaAffixVeil();
                veil.Build(body.transform.position, role);
                return veil;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀身体墨影创建失败: " + e.Message);
                return null;
            }
        }

        private void Build(Vector3 at, ArenaAffixRole role)
        {
            root = new GameObject("BossRush_AffixVeil");
            root.transform.position = at;
            switch (role)
            {
                case ArenaAffixRole.Guard:
                    tone = ArenaTrialsTuning.AffixColor(ArenaAffix.Escort);
                    ring = ArenaTrialsFx.FlatShape(root.transform, "Ring", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                    ArenaTrialsFx.SetRadius(ring, 0.6f, ArenaTrialsFx.RuneInnerRadius);
                    ArenaTrialsFx.Motes(root.transform, "Motes", BossRushParticleShape.GlowDot, tone, 0.35f, 1.2f, 0.04f, 0.07f, 1.4f, 0.9f,
                        BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                    break;
                case ArenaAffixRole.Shade:
                    tone = ArenaTrialsTuning.AffixColor(ArenaAffix.Summoner);
                    ArenaTrialsFx.Wisps(root.transform, "Ink", ArenaTrialsFx.VortexInk, 0.42f, 0.3f, 6f, 0.35f, 0.6f, 1.1f, false, 0.55f);
                    ArenaTrialsFx.Motes(root.transform, "Motes", BossRushParticleShape.GlowDot, tone, 0.3f, 0.9f, 0.04f, 0.07f, 1.2f, 0.8f,
                        BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                    break;
                default:
                    tone = ArenaTrialsTuning.AffixColor(ArenaAffix.Split);
                    ArenaTrialsFx.Wisps(root.transform, "Ink", ArenaTrialsFx.InkDark, 0.3f, 0.35f, 4.5f, 0.4f, 0.7f, 1.3f, false, 0.45f);
                    ArenaTrialsFx.Motes(root.transform, "Motes", BossRushParticleShape.GlowDot, tone, 0.4f, 1.6f, 0.04f, 0.08f, 1.5f, 0.9f,
                        BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                    break;
            }
        }

        internal void Follow(CharacterMainControl body, float dt)
        {
            if (root == null || body == null) return;
            age += dt;
            root.transform.position = body.transform.position;
            if (ring != null)
            {
                ArenaTrialsFx.Tint(ring, tone, 0.3f * BossRushUI.SmoothStep(Mathf.Clamp01(age / 0.6f)));
                ArenaTrialsFx.Spin(ring, dt * 18f);
            }
        }

        internal void Retire(float seconds)
        {
            if (root != null) ArenaTrialsFx.Retire(root, seconds);
            root = null;
        }
    }

    /// <summary>双生两只之间一根贴地的冷银丝线：中段轻轻起伏，两只都站着才亮。</summary>
    internal sealed class ArenaAffixTether
    {
        private const int Points = 12;
        private GameObject root;
        private LineRenderer line;
        private Color color;
        private float level;
        private float age;

        internal static ArenaAffixTether Create(string name, Color color)
        {
            try
            {
                ArenaAffixTether tether = new ArenaAffixTether();
                tether.color = color;
                tether.root = new GameObject(name);
                tether.line = ArenaTrialsFx.GroundLine(tether.root.transform, "Thread", BossRushFxBlend.Additive, BossRushFxKit.GainBright, 0.06f, 2);
                if (tether.line != null) tether.line.positionCount = Points;
                return tether;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal void Show(Vector3 a, Vector3 b, float dt) { Step(a, b, true, dt); }

        internal void Hide(float dt)
        {
            if (line == null || level <= 0f) return;
            level = Mathf.MoveTowards(level, 0f, dt * 2f);
            ArenaTrialsFx.Tint(line, color, 0.26f * level);
        }

        private void Step(Vector3 a, Vector3 b, bool visible, float dt)
        {
            if (line == null) return;
            age += dt;
            level = Mathf.MoveTowards(level, visible ? 1f : 0f, dt * 1.5f);
            Vector3 span = b - a;
            span.y = 0f;
            Vector3 side = span.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, span.normalized) : Vector3.right;
            float lift = ArenaTrialsFx.GroundLift + 0.04f;
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                float wave = Mathf.Sin(t * Mathf.PI) * Mathf.Sin(age * 1.7f + t * 5f) * 0.18f;
                line.SetPosition(i, Vector3.Lerp(a, b, t) + side * wave + Vector3.up * lift);
            }
            ArenaTrialsFx.Tint(line, color, 0.26f * level * (0.8f + 0.2f * Mathf.Sin(age * 2.6f)));
        }

        internal void Destroy()
        {
            if (root != null) ArenaTrialsFx.Retire(root, 0.5f);
            root = null;
            line = null;
        }
    }

    /// <summary>
    /// 地上的一座符阵：双生的「还魂」（冷银，魂点向心聚拢）与召唤的「影门」（墨紫，墨烟从中涌起）。
    /// 填充从圆心长到环上，读「还剩多久」——同天灾预警的口径。
    /// </summary>
    internal sealed class ArenaAffixOmen
    {
        private GameObject root;
        private SpriteRenderer ring;
        private SpriteRenderer fill;
        private ParticleSystem core;
        private Light light;
        private Color color;
        private float radius;
        private float progress;
        private bool revive;
        private float age;

        internal static ArenaAffixOmen Create(string name, Vector3 at, float radius, Color color, bool revive)
        {
            try
            {
                ArenaAffixOmen omen = new ArenaAffixOmen();
                omen.Build(name, at, radius, color, revive);
                return omen;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀符阵创建失败: " + e.Message);
                return null;
            }
        }

        private void Build(string name, Vector3 at, float r, Color c, bool isRevive)
        {
            color = c;
            radius = r;
            revive = isRevive;
            root = new GameObject(name);
            root.transform.position = at;
            fill = ArenaTrialsFx.FlatSoft(root.transform, "Fill", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
            ArenaTrialsFx.SetRadius(fill, 0.01f, ArenaTrialsFx.SoftRadius);
            ring = ArenaTrialsFx.FlatShape(root.transform, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ArenaTrialsFx.SetRadius(ring, r, ArenaTrialsFx.RuneRadius);
            if (revive)
            {
                core = ArenaTrialsFx.Motes(root.transform, "Souls", BossRushParticleShape.GlowDot, c, r * 0.9f, 3f, 0.05f, 0.09f, 1.3f, 1.3f,
                    BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            }
            else
            {
                core = ArenaTrialsFx.Wisps(root.transform, "Ink", ArenaTrialsFx.VortexInk, 0.5f, r * 0.5f, 10f, 0.4f, 0.8f, 1f, false, 0.9f);
            }
            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = r * 3f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, 0.3f, 0.3f, 0f, 2.9f, revive ? 0f : 0.2f);
            SetProgress(0f);
        }

        /// <summary>0 → 1：填充长满、灯渐亮、环越转越快。</summary>
        internal void SetProgress(float t)
        {
            if (root == null) return;
            float dt = Time.deltaTime;
            age += dt;
            progress = Mathf.Clamp01(t);
            float appear = BossRushUI.SmoothStep(Mathf.Clamp01(age / 0.35f));
            ArenaTrialsFx.SetRadius(fill, Mathf.Max(0.01f, radius * progress), ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.Tint(fill, color, 0.22f * appear);
            ArenaTrialsFx.Tint(ring, color, (0.4f + 0.3f * progress) * appear);
            ArenaTrialsFx.Spin(ring, dt * (revive ? 1f : -1f) * (18f + 70f * progress));
            if (light != null)
            {
                BossRushFxLightFade fade = light.GetComponent<BossRushFxLightFade>();
                if (fade != null) fade.SetTarget(0.3f + 1.3f * progress);
            }
            if (core != null && revive) ArenaTrialsFx.SetRate(core, 3f + 9f * progress);
        }

        internal void Retire(float seconds)
        {
            if (root != null) ArenaTrialsFx.Retire(root, seconds);
            root = null;
        }
    }

    /// <summary>首领倒下后留在场上的东西（亡魂、殉爆）：不是身体，收波后照样演完，战规收尾时收掉。</summary>
    internal abstract class ArenaAffixAftermath
    {
        protected readonly ArenaTrials Trials;
        protected GameObject Root;
        protected float Age;

        protected ArenaAffixAftermath(ArenaTrials trials) { Trials = trials; }

        /// <summary>返回 false 表示演完了（已经自己退场）。</summary>
        internal abstract bool Step(float dt);

        internal void Retire()
        {
            if (Root != null) ArenaTrialsFx.Retire(Root, 0.6f);
            Root = null;
        }
    }

    /// <summary>亡魂：尸身上升起一缕墨魂（冷银魂火裹在墨烟里、地上一团影），追着玩家飘 15 秒，贴身时一点点吸血；翻滚可穿。</summary>
    internal sealed class ArenaAffixWraith : ArenaAffixAftermath
    {
        private const float RiseSeconds = 0.9f;
        private const float TouchTick = 0.25f;
        private Vector3 position;
        private float touchTimer;
        private bool touched;
        private SpriteRenderer shadow;
        private Transform body;

        internal ArenaAffixWraith(ArenaTrials trials, Vector3 at) : base(trials)
        {
            position = at;
            try
            {
                Root = new GameObject("BossRush_AffixWraith");
                Root.transform.position = at;
                shadow = ArenaTrialsFx.FlatSoft(Root.transform, "Shadow", BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
                ArenaTrialsFx.SetRadius(shadow, 0.8f, ArenaTrialsFx.SoftRadius);
                GameObject bodyGo = new GameObject("Soul");
                bodyGo.transform.SetParent(Root.transform, false);
                body = bodyGo.transform;
                Color soul = new Color(0.74f, 0.80f, 0.88f, 1f);
                ParticleSystem smoke = ArenaTrialsFx.Wisps(body, "Smoke", ArenaTrialsFx.InkDark, 0.55f, 0.3f, 16f, 0.45f, 0.8f, 0.9f, false, 0.35f);
                if (smoke != null) smoke.transform.localPosition = Vector3.zero;
                ParticleSystem motes = ArenaTrialsFx.Motes(body, "Fire", BossRushParticleShape.GlowDot, soul, 0.2f, 7f, 0.06f, 0.1f, 0.9f, 0.6f,
                    BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                if (motes != null) motes.transform.localPosition = Vector3.zero;
                Light light = bodyGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = soul;
                light.range = 4.5f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                BossRushFxLightFade.Attach(light, 1.1f, 0.6f, 0.25f, 1.8f);
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxSoulRise, at);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令亡魂创建失败: " + e.Message);
            }
        }

        internal override bool Step(float dt)
        {
            if (Root == null) return false;
            Age += dt;
            CharacterMainControl player = CharacterMainControl.Main;
            bool playerAlive = player != null && player.Health != null && !player.Health.IsDead;
            if (Age > RiseSeconds + ArenaTrialsTuning.WraithSeconds || !playerAlive)
            {
                ArenaAffixFx.WraithFade(position + Vector3.up);
                Retire();
                return false;
            }
            float height;
            if (Age < RiseSeconds)
            {
                // 先从尸身上慢慢浮起来，再开始追
                height = Mathf.Lerp(0.2f, 1f, BossRushUI.SmoothStep(Age / RiseSeconds));
            }
            else
            {
                Vector3 to = player.transform.position - position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > 0.05f) position += to / distance * Mathf.Min(distance, ArenaTrialsTuning.WraithSpeed * dt);
                position.y = Mathf.Lerp(position.y, player.transform.position.y, dt * 3f);
                height = 1f + 0.12f * Mathf.Sin(Age * 2.4f);
                touchTimer -= dt;
                float reach = ArenaTrialsTuning.WraithTouchRadius;
                if (touchTimer <= 0f && ArenaTrials.FlatSqr(position, player.transform.position) <= reach * reach)
                {
                    touchTimer = TouchTick;
                    float damage = ArenaTrials.ShareOfMaxHealth(player.Health, ArenaTrialsTuning.WraithDamagePerSecond * TouchTick);
                    if (ArenaTrials.HurtNeutral(player.Health, damage, position, true) && !touched)
                    {
                        touched = true;
                        ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellHum, position);
                    }
                }
            }
            Root.transform.position = position;
            if (body != null) body.localPosition = new Vector3(0f, height, 0f);
            float fade = 1f;
            float remaining = RiseSeconds + ArenaTrialsTuning.WraithSeconds - Age;
            if (remaining < 1.5f) fade = Mathf.Clamp01(remaining / 1.5f);
            ArenaTrialsFx.Tint(shadow, ArenaTrialsFx.InkDark, 0.32f * Mathf.Clamp01(Age / RiseSeconds) * fade);
            return true;
        }
    }

    /// <summary>殉爆：尸身处一圈朱砂符环画在爆炸半径上，填充 3 秒长满、火越聚越急，满了原地炸开；翻滚可躲。</summary>
    internal sealed class ArenaAffixVolatile : ArenaAffixAftermath
    {
        private readonly Vector3 center;
        private readonly List<Health> victims = new List<Health>(8);
        private SpriteRenderer ring;
        private SpriteRenderer fill;
        private ParticleSystem core;
        private BossRushFxLightFade lightFade;
        private int lastTick = -1;

        internal ArenaAffixVolatile(ArenaTrials trials, Vector3 at) : base(trials)
        {
            Vector3 ground;
            center = SpawnPositionHelper.TrySnapToGround(at, out ground, 0f, 2f) && Mathf.Abs(ground.y - at.y) < 2f ? ground : at;
            try
            {
                Root = new GameObject("BossRush_AffixVolatile");
                Root.transform.position = center;
                float r = ArenaTrialsTuning.VolatileRadius;
                fill = ArenaTrialsFx.FlatSoft(Root.transform, "Fill", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                ArenaTrialsFx.SetRadius(fill, 0.01f, ArenaTrialsFx.SoftRadius);
                ring = ArenaTrialsFx.FlatShape(Root.transform, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                ArenaTrialsFx.SetRadius(ring, r, ArenaTrialsFx.RuneRadius);
                core = ArenaTrialsFx.Motes(Root.transform, "Core", BossRushParticleShape.GlowDot, ArenaTrialsFx.Ember, 0.5f, 4f, 0.06f, 0.11f,
                    0.8f, 1.6f, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
                GameObject lightGo = new GameObject("Light");
                lightGo.transform.SetParent(Root.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = ArenaTrialsFx.EmberDeep;
                light.range = r * 1.4f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                lightFade = BossRushFxLightFade.Attach(light, 0.4f, 0.3f, 0f, 2.9f, 0.25f);
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxEmberFlare, center);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令殉爆创建失败: " + e.Message);
            }
        }

        internal override bool Step(float dt)
        {
            if (Root == null) return false;
            Age += dt;
            float delay = ArenaTrialsTuning.VolatileDelay;
            float t = Mathf.Clamp01(Age / delay);
            float appear = BossRushUI.SmoothStep(Mathf.Clamp01(Age / 0.3f));
            ArenaTrialsFx.SetRadius(fill, Mathf.Max(0.01f, ArenaTrialsTuning.VolatileRadius * t), ArenaTrialsFx.SoftRadius);
            ArenaTrialsFx.Tint(fill, ArenaTrialsFx.EmberDeep, 0.2f * appear);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Age * (6f + 18f * t));
            ArenaTrialsFx.Tint(ring, ArenaTrialsTuning.AffixColor(ArenaAffix.Volatile), (0.45f + 0.35f * t) * pulse * appear);
            ArenaTrialsFx.Spin(ring, dt * (20f + 120f * t));
            ArenaTrialsFx.SetRate(core, 4f + 14f * t);
            if (lightFade != null) lightFade.SetTarget(0.4f + 2.4f * t * t);
            int tick = Mathf.FloorToInt(delay - Age);
            if (tick != lastTick && tick >= 1 && tick < Mathf.FloorToInt(delay))
            {
                lastTick = tick;
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellDing, center);
            }
            if (Age < delay) return true;
            Explode();
            ArenaTrialsFx.Retire(Root, 0.5f);
            Root = null;
            return false;
        }

        private void Explode()
        {
            float r = ArenaTrialsTuning.VolatileRadius;
            ArenaTrialsFx.Impact(center, ArenaTrialsFx.EmberDeep, r, 30, new Color(0.12f, 0.10f, 0.09f, 0.6f));
            ArenaTrialsFx.Flash(center + Vector3.up * 1.6f, ArenaTrialsFx.Ember, 5f, r * 3f, 0.7f);
            BossRushFxBurst rise = BossRushFxKit.Sparks(ArenaTrialsFx.Ember, 24);
            rise.Upward = true;
            rise.SpeedMin = 3f;
            rise.SpeedMax = 7f;
            rise.Gravity = 0.4f;
            rise.LifeMin = 0.6f;
            rise.LifeMax = 1.2f;
            rise.SizeMin = 0.06f;
            rise.SizeMax = 0.12f;
            BossRushFxKit.PlayBurst(center + Vector3.up * 0.3f, rise);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, center);
            ArenaExpeditionHud.FlashAmbience(0.35f);
            if (Trials == null) return;
            Trials.CollectVictims(center, r, victims);
            for (int i = 0; i < victims.Count; i++)
            {
                Health health = victims[i];
                ArenaTrials.HurtNeutral(health, ArenaTrials.ShareOfMaxHealth(health, ArenaTrialsTuning.VolatileDamage), center, true,
                    ElementTypes.fire);
            }
            victims.Clear();
        }
    }

    /// <summary>词缀的一次性演出。都是自毁的粒子 / 瞬时灯，调用方不持有任何东西。</summary>
    internal static class ArenaAffixFx
    {
        private static readonly Color InkSmoke = new Color(0.07f, 0.06f, 0.06f, 0.55f);

        /// <summary>双生的孪生现身：一圈冷银涟漪、一颗亮芯、一缕墨。</summary>
        internal static void TwinEmerge(Vector3 at)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Twin);
            Play(at, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.7f), 1.2f, 2.2f, 0.6f, true));
            Play(at + Vector3.up * 0.9f, BossRushFxKit.Glint(c, 1.4f, 0.3f));
            Play(at, BossRushFxKit.Dust(InkSmoke, 6));
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxChimeStone, at);
        }

        /// <summary>还魂：冷银冲击、火星、亮芯与一盏灯——「它又站起来了」。</summary>
        internal static void TwinRevive(Vector3 at)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Twin);
            ArenaTrialsFx.Impact(at, c, 1.4f, 14, InkSmoke);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxChimeStone, at);
        }

        /// <summary>两只都在时限内倒下：还魂阵向内收拢、一记印声——这一对封住了。</summary>
        internal static void TwinSealed(Vector3 at)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Twin);
            Play(at, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.65f), 2.7f, 0.3f, 0.5f, true));
            Play(at + Vector3.up * 0.5f, BossRushFxKit.Glint(c, 0.9f, 0.2f));
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxSealStamp, at);
        }

        /// <summary>亲卫就位：几点慢升的金屑与一圈小涟漪。</summary>
        internal static void GuardArrive(Vector3 at)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Escort);
            Play(at, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.55f), 0.6f, 2f, 0.5f, true));
            BossRushFxBurst gold = BossRushFxKit.Sparks(c, 8);
            gold.Upward = true;
            gold.SpeedMin = 0.8f;
            gold.SpeedMax = 1.8f;
            gold.Drag = 1.5f;
            gold.LifeMin = 0.8f;
            gold.LifeMax = 1.4f;
            gold.Stretch = 0f;
            Play(at + Vector3.up * 0.2f, gold);
        }

        /// <summary>亲卫清完：首领身上的金罩碎开（一圈金冲击、一撮金屑、亮芯、瞬时灯、碎钟）。</summary>
        internal static void GuardShatter(CharacterMainControl boss)
        {
            if (boss == null) return;
            Vector3 at = boss.transform.position;
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Escort);
            Play(at + Vector3.up * ArenaTrialsFx.GroundLift, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.9f), 2.4f, 2.2f, 0.55f, true));
            BossRushFxBurst shards = BossRushFxKit.Sparks(c, 18);
            shards.Shape = BossRushParticleShape.Shard;
            shards.SpeedMin = 5f;
            shards.SpeedMax = 9f;
            shards.SizeMin = 0.12f;
            shards.SizeMax = 0.22f;
            shards.LifeMin = 0.4f;
            shards.LifeMax = 0.8f;
            shards.Gravity = 1.4f;
            shards.Spin = 360f;
            Play(at + Vector3.up * 1.1f, shards);
            Play(at + Vector3.up * 1.1f, BossRushFxKit.Glint(c, 1.8f, 0.25f));
            ArenaTrialsFx.Flash(at + Vector3.up * 1.4f, c, 2.6f, 7f, 0.5f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellShatter, at);
        }

        /// <summary>主将倒下、亲卫影从散去（或收波散掉剩下的身体）：一团墨散开，不出声。</summary>
        internal static void Disperse(Vector3 at)
        {
            BossRushFxBurst ink = BossRushFxKit.Dust(InkSmoke, 8);
            ink.FlatOnGround = false;
            ink.Radial = false;
            ink.Upward = true;
            ink.SpeedMin = 0.8f;
            ink.SpeedMax = 1.6f;
            ink.ShapeRadius = 0.4f;
            Play(at + Vector3.up * 0.6f, ink);
        }

        /// <summary>分裂：本体碎成两半——冷银裂纹冲击、碎片、墨尘与一盏冷光。</summary>
        internal static void SplitBurst(Vector3 at)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Split);
            Play(at + Vector3.up * ArenaTrialsFx.GroundLift, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.8f), 1f, 3f, 0.5f, true));
            BossRushFxBurst shards = BossRushFxKit.Sparks(c, 16);
            shards.Shape = BossRushParticleShape.Shard;
            shards.Radial = true;
            shards.SpeedMin = 4f;
            shards.SpeedMax = 8f;
            shards.SizeMin = 0.1f;
            shards.SizeMax = 0.2f;
            shards.LifeMin = 0.35f;
            shards.LifeMax = 0.7f;
            Play(at + Vector3.up * 0.8f, shards);
            BossRushFxBurst ink = BossRushFxKit.Dust(InkSmoke, 10);
            ink.SpeedMin = 2.5f;
            ink.SpeedMax = 4f;
            Play(at, ink);
            ArenaTrialsFx.Flash(at + Vector3.up * 1.2f, c, 2.2f, 6f, 0.45f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxChimeStone, at);
        }

        /// <summary>小分身倒下：一缕墨、一点冷光。</summary>
        internal static void ShardFall(Vector3 at)
        {
            Play(at, BossRushFxKit.Dust(InkSmoke, 5));
            Play(at + Vector3.up * 0.7f, BossRushFxKit.Glint(ArenaTrialsTuning.AffixColor(ArenaAffix.Split), 0.8f, 0.2f));
        }

        /// <summary>影从从影门里起来。</summary>
        internal static void ShadeRise(Vector3 at)
        {
            BossRushFxBurst ink = BossRushFxKit.Dust(new Color(ArenaTrialsFx.VortexInk.r, ArenaTrialsFx.VortexInk.g, ArenaTrialsFx.VortexInk.b, 0.6f), 8);
            ink.FlatOnGround = false;
            ink.Radial = false;
            ink.Upward = true;
            ink.SpeedMin = 1.5f;
            ink.SpeedMax = 3f;
            Play(at, ink);
            Play(at + Vector3.up * 0.8f, BossRushFxKit.Glint(ArenaTrialsTuning.AffixColor(ArenaAffix.Summoner), 0.9f, 0.22f));
        }

        /// <summary>狂暴：朱砂冲击、一撮上窜火星、一盏暗红灯、一声战鼓。</summary>
        internal static void FrenzyFlare(CharacterMainControl boss)
        {
            if (boss == null) return;
            Vector3 at = boss.transform.position;
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Frenzy);
            Play(at + Vector3.up * ArenaTrialsFx.GroundLift, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.85f), 1.4f, 2.6f, 0.6f, true));
            BossRushFxBurst embers = BossRushFxKit.Sparks(ArenaTrialsFx.Ember, 26);
            embers.Upward = true;
            embers.SpeedMin = 3f;
            embers.SpeedMax = 6f;
            embers.Gravity = 0.3f;
            embers.LifeMin = 0.5f;
            embers.LifeMax = 1f;
            Play(at + Vector3.up * 0.4f, embers);
            ArenaTrialsFx.Flash(at + Vector3.up * 1.3f, ArenaTrialsFx.EmberDeep, 3f, 7f, 0.6f);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxWarDrum, at);
        }

        /// <summary>闪现前摇：墨烟向首领脚下收拢（半秒），一记笔扫声。</summary>
        internal static void BlinkGather(Vector3 at)
        {
            BossRushFxBurst ink = BossRushFxKit.Dust(InkSmoke, 12);
            ink.ShellOnly = true;
            ink.ShapeRadius = 1.4f;
            ink.SpeedMin = -2.6f;
            ink.SpeedMax = -2f;
            ink.Drag = 0f;
            ink.LifeMin = 0.45f;
            ink.LifeMax = 0.55f;
            ink.GrowTo = 0.6f;
            Play(at + Vector3.up * ArenaTrialsFx.GroundLift, ink);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBrushSweep, at);
        }

        /// <summary>闪现落地：原地留一团散开的墨，落点一圈墨紫涟漪、亮芯与一盏暗灯。</summary>
        internal static void BlinkArrive(Vector3 from, Vector3 to)
        {
            Color c = ArenaTrialsTuning.AffixColor(ArenaAffix.Blink);
            Play(from, BossRushFxKit.Dust(InkSmoke, 8));
            Play(to + Vector3.up * ArenaTrialsFx.GroundLift, BossRushFxKit.Shockwave(new Color(c.r, c.g, c.b, 0.75f), 0.8f, 2.4f, 0.45f, true));
            Play(to, BossRushFxKit.Dust(InkSmoke, 10));
            Play(to + Vector3.up * 0.9f, BossRushFxKit.Glint(c, 1.1f, 0.22f));
            ArenaTrialsFx.Flash(to + Vector3.up * 1.2f, c, 1.6f, 5f, 0.4f);
        }

        /// <summary>亡魂散了：一点冷光、一缕墨。</summary>
        internal static void WraithFade(Vector3 at)
        {
            Play(at, BossRushFxKit.Glint(new Color(0.74f, 0.80f, 0.88f, 1f), 0.9f, 0.3f));
            BossRushFxBurst ink = BossRushFxKit.Dust(InkSmoke, 6);
            ink.FlatOnGround = false;
            ink.Radial = false;
            ink.Upward = true;
            ink.SpeedMin = 0.6f;
            ink.SpeedMax = 1.2f;
            Play(at, ink);
        }

        private static void Play(Vector3 at, BossRushFxBurst spec)
        {
            try { BossRushFxKit.PlayBurst(at, spec); }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀演出失败: " + e.Message); }
        }
    }
}
