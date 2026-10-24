using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 巨型外置炮、压力灯、螺旋束、冲击环与反弹瞄准线。资源属于本次手持，离手即回收。
    /// 光束与环用柔边条带加色材质（亮芯 + 外晕两层），淡出走先快后慢；开火、撞墙、命中各有一记粒子爆发，
    /// 压力灯按电量连续点亮，蓄满时闪一下并在枪口亮起星芒，告诉玩家「下一枪会放出来」。
    /// </summary>
    internal sealed class DangerousPrototypeVisuals : IDisposable
    {
        private readonly GameObject root;
        private readonly Transform barrel;
        private readonly Transform rotor;
        private readonly Transform needle;
        private readonly Renderer[] chargeLamps = new Renderer[5];
        private readonly Renderer radiantMuzzle;
        private readonly List<Flash> flashes = new List<Flash>();
        private readonly DangerousPrototypeKind kind;
        private readonly Color accent;
        private readonly Material coreStrip, glowStrip;
        private GameObject guideRoot;
        private LineRenderer guide;
        private float kick;
        private float spin;
        private float lastCharge = -1f;
        private float readyFlash;
        private float clock;
        private bool disposed;
        private readonly MaterialPropertyBlock colors = new MaterialPropertyBlock();
        private sealed class Flash
        {
            internal GameObject Root;
            internal LineRenderer Line;
            internal float Age;
            internal float Lifetime;
            internal float Width;
            internal Color Color;
            internal bool Expand;
        }
        internal DangerousPrototypeVisuals(Transform muzzle, CharacterMainControl player, DangerousPrototypeKind selected)
        {
            try
            {
            kind = selected;
            accent = Accent(kind);
            coreStrip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            glowStrip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            root = new GameObject("DangerousPrototype_HeldRig");
            root.transform.SetParent(muzzle, false);
            FieldAdventureVisualAssets.Acquire(root);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localPosition = new Vector3(0f, 0f, -0.28f);
            // 越夸张越好：整套装置比枪大一圈，远处也看得出手里套了什么。
            root.transform.localScale = Vector3.one * 1.2f;
            Color dark = new Color(0.09f, 0.13f, 0.16f);
            Color steel = new Color(0.41f, 0.5f, 0.56f);
            Part(root.transform, "PressureHousing", PrimitiveType.Cube, new Vector3(0f, 0f, 0.15f), new Vector3(0.52f, 0.43f, 0.8f), dark);
            barrel = new GameObject("TelescopingBarrel").transform;
            barrel.SetParent(root.transform, false);
            Cylinder(barrel, "MainTube", new Vector3(0f, 0f, 0.5f), 0.26f, 0.65f, steel);
            Cylinder(barrel, "HugeMuzzle", new Vector3(0f, 0f, 1.07f), 0.43f, 0.15f, dark);
            radiantMuzzle = Cylinder(barrel, "RadiantMuzzle", new Vector3(0f, 0f, 1.24f), 0.32f, 0.025f, accent).GetComponent<Renderer>();
            Cylinder(barrel, "DarkBore", new Vector3(0f, 0f, 1.27f), 0.21f, 0.03f, new Color(0.025f, 0.035f, 0.05f));
            for (int i = 0; i < 4; i++)
                Cylinder(barrel, "CoolingFin", new Vector3(0f, 0f, 0.24f + i * 0.2f), 0.34f, 0.026f, i % 2 == 0 ? accent : dark);
            rotor = new GameObject("AbsurdCoolingRotor").transform;
            rotor.SetParent(root.transform, false); rotor.localPosition = new Vector3(0f, 0f, 0.86f);
            for (int i = 0; i < 3; i++)
            {
                var arm = new GameObject("RotatingArm").transform;
                arm.SetParent(rotor, false); arm.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                Part(arm, "Fin", PrimitiveType.Cube, new Vector3(0f, 0.49f, 0f), new Vector3(0.14f, 0.48f, 0.09f), accent);
                Part(arm, "Counterweight", PrimitiveType.Sphere, new Vector3(0f, 0.72f, 0f), new Vector3(0.17f, 0.17f, 0.17f), steel);
            }
            Part(root.transform, "PressureGauge", PrimitiveType.Sphere, new Vector3(0.38f, 0.18f, 0.15f), new Vector3(0.26f, 0.26f, 0.07f), steel);
            needle = Part(root.transform, "RedNeedle", PrimitiveType.Cube, new Vector3(0.38f, 0.18f, 0.196f), new Vector3(0.025f, 0.16f, 0.025f), new Color(1f, 0.13f, 0.05f)).transform;
            for (int i = 0; i < 5; i++)
                chargeLamps[i] = Part(root.transform, "PressureLamp", PrimitiveType.Cube,
                    new Vector3(-0.2f + i * 0.1f, 0.24f, 0.12f), new Vector3(0.065f, 0.05f, 0.15f), accent).GetComponent<Renderer>();
            for (int side = -1; side <= 1; side += 2)
            {
                Cylinder(root.transform, "OversizedPressureTank", new Vector3(side * 0.35f, -0.13f, -0.1f), 0.15f, 0.38f, accent);
                Cylinder(root.transform, "TankCap", new Vector3(side * 0.35f, -0.13f, 0.28f), 0.18f, 0.04f, dark);
            }
            BuildSignature(root.transform, kind, accent, dark, steel);
            // 挂枪后新增的模型须接入官方角色显隐缓存，模型构建结束后才 SetRenderers。
            var subVisuals = root.AddComponent<CharacterSubVisuals>();
            subVisuals.SetRenderers();
            subVisuals.SetCharacter(player);
            if (player.characterModel != null) player.characterModel.SyncHiddenToMainCharacter();
            ModEntityFogVisibility.Attach(root, player.transform);
            }
            catch { Dispose(); throw; }
        }

        internal void Tick(float deltaTime, float charge)
        {
            if (root == null || BossRushUI.IsGamePaused()) return;
            clock += deltaTime;
            kick = Mathf.Max(0f, kick - deltaTime * 5f);
            readyFlash = Mathf.Max(0f, readyFlash - deltaTime * 3f);
            float recoil = kick * kick * (3f - 2f * kick);
            barrel.localPosition = new Vector3(0f, 0f, -recoil * 0.32f);
            spin += deltaTime * (100f + charge * 320f + kick * 700f);
            rotor.localRotation = Quaternion.Euler(0f, 0f, spin);
            needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(75f, -75f, charge) + (charge >= 1f ? Mathf.Sin(clock * 30f) * 4f : 0f));
            if (lastCharge >= 0f && lastCharge < 1f && charge >= 1f) PlayReady();
            bool ready = charge >= 1f;
            if (ready || readyFlash > 0f || Mathf.Abs(charge - lastCharge) > 0.01f)
            {
                // 每盏灯按自己那一段电量连续亮起；蓄满后整排呼吸，刚满时闪白一下。
                float breathe = ready ? 0.75f + 0.25f * Mathf.Sin(clock * 6f) : 1f;
                for (int i = 0; i < chargeLamps.Length; i++)
                {
                    float level = Mathf.Clamp01(charge * chargeLamps.Length - i);
                    Color lamp = Color.Lerp(new Color(0.07f, 0.1f, 0.12f), accent * breathe, level);
                    Tint(chargeLamps[i], Color.Lerp(lamp, Color.white, readyFlash * 0.7f), colors);
                }
                Tint(radiantMuzzle, Color.Lerp(accent * (ready ? 1.3f : 0.6f + charge * 0.4f), Color.white, readyFlash * 0.8f), colors);
            }
            lastCharge = charge;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash flash = flashes[i];
                flash.Age += deltaTime;
                if (flash.Root == null || flash.Age >= flash.Lifetime)
                {
                    if (flash.Root != null) UnityEngine.Object.Destroy(flash.Root);
                    flashes.RemoveAt(i); continue;
                }
                float t = flash.Age / flash.Lifetime;
                float fade = (1f - t) * (1f - t);
                if (flash.Expand) flash.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 2.2f, 1f - fade);
                flash.Line.widthMultiplier = flash.Width * (1f - t * t * 0.75f);
                Color color = flash.Color; color.a *= fade;
                flash.Line.startColor = color; flash.Line.endColor = color;
            }
            if (guide != null && guide.enabled)
            {
                float pulse = 0.65f + 0.35f * Mathf.Sin(clock * 5f);
                guide.widthMultiplier = (charge >= 1f ? 0.065f : 0.04f) * pulse;
            }
        }

        internal static Color Accent(DangerousPrototypeKind kind)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon: return new Color(1f, 0.39f, 0.04f);
                case DangerousPrototypeKind.RicochetSun: return new Color(0.12f, 0.95f, 1f);
                case DangerousPrototypeKind.QuackRail: return new Color(0.45f, 0.55f, 1f);
                case DangerousPrototypeKind.Thunderbill: return new Color(1f, 0.9f, 0.3f);
                default: return new Color(1f, 0.42f, 0.72f);
            }
        }

        /// <summary>每种装置一处一眼认得出的招牌部件。</summary>
        private static void BuildSignature(Transform parent, DangerousPrototypeKind kind, Color accent, Color dark, Color steel)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon:
                    // 枪口外再套一圈喇叭口和两根排气管。
                    Cylinder(parent, "FlaredBlastHorn", new Vector3(0f, 0f, 1.42f), 0.58f, 0.09f, dark);
                    for (int side = -1; side <= 1; side += 2)
                        Cylinder(parent, "ExhaustStack", new Vector3(side * 0.24f, 0.32f, 0.4f), 0.07f, 0.3f, steel);
                    break;
                case DangerousPrototypeKind.RicochetSun:
                    // 枪口前一圈太阳光芒片。
                    for (int i = 0; i < 8; i++)
                    {
                        var ray = new GameObject("SunRayArm").transform;
                        ray.SetParent(parent, false); ray.localPosition = new Vector3(0f, 0f, 1.2f); ray.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                        Part(ray, "SunRay", PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), new Vector3(0.09f, 0.32f, 0.04f), accent);
                    }
                    break;
                case DangerousPrototypeKind.QuackRail:
                    // 两根伸出枪口一米多的导轨，缠着发光线圈。
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(parent, "RailBar", PrimitiveType.Cube, new Vector3(side * 0.2f, 0f, 1.35f), new Vector3(0.07f, 0.14f, 1.9f), steel);
                        for (int c = 0; c < 4; c++)
                            Cylinder(parent, "RailCoil", new Vector3(side * 0.2f, 0f, 0.7f + c * 0.42f), 0.12f, 0.03f, accent);
                    }
                    break;
                case DangerousPrototypeKind.Thunderbill:
                    // 背上一颗特斯拉线圈球，枪口三根放电叉。
                    Part(parent, "TeslaSphere", PrimitiveType.Sphere, new Vector3(0f, 0.48f, 0.05f), Vector3.one * 0.34f, accent);
                    Cylinder(parent, "TeslaMast", new Vector3(0f, 0.3f, 0.05f), 0.05f, 0.12f, steel);
                    for (int i = 0; i < 3; i++)
                    {
                        var fork = new GameObject("DischargeFork").transform;
                        fork.SetParent(parent, false); fork.localPosition = new Vector3(0f, 0f, 1.3f); fork.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                        Part(fork, "Prong", PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.16f), new Vector3(0.05f, 0.05f, 0.36f), steel);
                    }
                    break;
                default:
                    // 顶上一排六管烟花发射架。
                    for (int i = 0; i < 6; i++)
                        Cylinder(parent, "RocketTube", new Vector3(-0.25f + (i % 3) * 0.25f, 0.42f + (i / 3) * 0.22f, 0.25f), 0.1f, 0.32f, i % 2 == 0 ? accent : dark);
                    break;
            }
        }

        private void PlayReady()
        {
            readyFlash = 1f;
            if (barrel == null) return;
            Vector3 muzzle = barrel.TransformPoint(new Vector3(0f, 0f, 1.3f));
            BossRushFxKit.PlayBurst(muzzle, BossRushFxKit.Glint(accent, 0.65f, 0.22f));
            BossRushFxKit.PlayBurst(muzzle, BossRushFxKit.Shockwave(new Color(accent.r, accent.g, accent.b, 0.7f), 0.25f, 3.2f, 0.3f, false));
        }

        internal void Fire()
        {
            kick = 1f;
            readyFlash = 0f;
            if (root == null) return;
            Vector3 muzzle = barrel.TransformPoint(new Vector3(0f, 0f, 1.3f));
            Vector3 forward = root.transform.forward;
            Impact(muzzle, forward, 0);
            BossRushFxKit.PlayBurst(muzzle, BossRushFxKit.Glint(Color.Lerp(accent, Color.white, 0.4f), kind == DangerousPrototypeKind.JetCannon ? 1.6f : 0.9f, 0.14f));
            BossRushFxBurst sparks = BossRushFxKit.Sparks(accent, kind == DangerousPrototypeKind.JetCannon ? 26 : 14);
            sparks.Cone = kind == DangerousPrototypeKind.JetCannon ? 22f : 12f;
            sparks.Direction = forward;
            sparks.SpeedMin = 8f; sparks.SpeedMax = 16f; sparks.LifeMin = 0.15f; sparks.LifeMax = 0.32f;
            BossRushFxKit.PlayBurst(muzzle, sparks);
            if (kind == DangerousPrototypeKind.JetCannon)
            {
                BossRushFxBurst smoke = BossRushFxKit.Dust(new Color(0.45f, 0.4f, 0.38f, 0.5f), 10);
                smoke.Radial = false; smoke.FlatOnGround = false; smoke.Cone = 24f; smoke.Direction = forward;
                smoke.SpeedMin = 3f; smoke.SpeedMax = 7f; smoke.SizeMin = 0.45f; smoke.SizeMax = 0.9f; smoke.LifeMin = 0.5f; smoke.LifeMax = 0.9f;
                BossRushFxKit.PlayBurst(muzzle, smoke);
                Shake(-forward, 0.22f);
            }
            // 枪口前一串收拢的冲击环：巨炮六节、反弹炮三节，限制在枪口附近，不创建遮住屏幕的巨大面片。
            int rings = kind == DangerousPrototypeKind.JetCannon ? 6 : 3;
            for (int i = 0; i < rings; i++)
            {
                Vector3 center = muzzle + forward * (0.22f + i * 0.29f);
                Ring(center, forward, 0.19f + i * 0.06f, accent, 0.16f, 0.34f + i * 0.025f);
            }
        }
        internal void HideRig()
        {
            if (root != null) root.SetActive(false);
            HideGuide();
        }
        internal void Beam(Vector3 from, Vector3 to, bool jet, int bounce)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.02f) return;
            Color color = jet ? new Color(1f, 0.42f, 0.08f, 0.85f)
                : (bounce == 0 ? new Color(0.15f, 0.65f, 1f, 0.75f) : new Color(1f, 0.67f, 0.1f, 0.95f));
            // 长光束分段采样迷雾，不能因枪口可见就显示整条穿入黑区的轨迹。
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 1, 16);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Vector3.Lerp(from, to, (float)i / segments);
                Vector3 b = Vector3.Lerp(from, to, (float)(i + 1) / segments);
                Stroke(new[] { a, b }, color, jet ? 0.7f : (bounce > 0 ? 0.4f : 0.2f), 0.42f, false, glowStrip);
                Stroke(new[] { a, b }, new Color(1f, 0.96f, 0.8f, 0.9f), jet ? 0.18f : (bounce > 0 ? 0.1f : 0.05f), 0.27f, false, coreStrip);
            }
            Vector3 axis = delta / length;
            Vector3 right = Vector3.Cross(axis, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, axis).normalized;
            for (int section = 0; section < segments; section++)
            {
                Vector3[] spiral = new Vector3[13];
                for (int i = 0; i < spiral.Length; i++)
                {
                    float t = (section + i / 12f) / segments;
                    float angle = t * length * 4f;
                    spiral[i] = Vector3.Lerp(from, to, t) + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * (jet ? 0.5f : 0.26f);
                }
                Stroke(spiral, color, 0.08f, 0.5f, false, glowStrip);
            }
            BossRushFxKit.PlayBurst(to, BossRushFxKit.Glint(color, jet ? 1f : 0.6f, 0.12f));
        }
        internal void Impact(Vector3 point, Vector3 normal, int bounce)
        {
            Color color = bounce == 0 ? accent : new Color(1f, 0.65f, 0.1f, 0.9f);
            Ring(point + normal * 0.04f, normal, 0.5f, color, 0.12f, 0.55f);
            Ring(point + normal * 0.06f, normal, 0.29f, Color.white, 0.055f, 0.32f);
            BossRushFxBurst sparks = BossRushFxKit.Sparks(color, bounce > 0 ? 18 : 10);
            sparks.Cone = 55f; sparks.Direction = normal;
            sparks.SpeedMin = 4f; sparks.SpeedMax = 9f; sparks.Gravity = 0.6f;
            BossRushFxKit.PlayBurst(point + normal * 0.05f, sparks);
        }

        /// <summary>轨道炮：一条贯穿全程的粗亮芯 + 外晕 + 沿线一串收拢冲击环与火花，镜头重震。</summary>
        internal void RailBeam(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.02f) return;
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 1, 18);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Vector3.Lerp(from, to, (float)i / segments);
                Vector3 b = Vector3.Lerp(from, to, (float)(i + 1) / segments);
                Stroke(new[] { a, b }, new Color(accent.r, accent.g, accent.b, 0.9f), 0.9f, 0.7f, false, glowStrip);
                Stroke(new[] { a, b }, new Color(1f, 1f, 1f, 1f), 0.24f, 0.45f, false, coreStrip);
            }
            Vector3 axis = delta / length;
            for (float d = 2f; d < length; d += 5f)
            {
                Ring(from + axis * d, axis, 0.45f, accent, 0.07f, 0.5f);
                BossRushFxBurst sparks = BossRushFxKit.Sparks(accent, 6);
                sparks.Cone = 70f; sparks.Direction = Vector3.up; sparks.SpeedMin = 2f; sparks.SpeedMax = 5f; sparks.Gravity = 0.5f;
                BossRushFxKit.PlayBurst(from + axis * d, sparks);
            }
            BossRushFxKit.PlayBurst(to, BossRushFxKit.Glint(accent, 1.6f, 0.2f));
            Shake(-axis, 0.35f);
        }

        /// <summary>连锁闪电：每一跳一道折线，亮芯 + 外晕，节点处一颗星芒；空放时只劈出一截。</summary>
        internal void Lightning(Vector3[] points, bool landed)
        {
            if (points == null || points.Length < 2) return;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                float length = Vector3.Distance(a, b);
                int steps = Mathf.Clamp(Mathf.CeilToInt(length / 0.8f), 3, 14);
                Vector3[] bolt = new Vector3[steps + 1];
                Vector3 side = Vector3.Cross((b - a).normalized, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float jitter = (s == 0 || s == steps) ? 0f : (UnityEngine.Random.value - 0.5f) * 0.6f;
                    bolt[s] = Vector3.Lerp(a, b, t) + side * jitter + Vector3.up * ((UnityEngine.Random.value - 0.5f) * 0.3f);
                }
                Stroke(bolt, new Color(accent.r, accent.g, accent.b, 0.85f), 0.32f, 0.32f, false, glowStrip);
                Stroke(bolt, Color.white, 0.09f, 0.24f, false, coreStrip);
                if (landed || i + 1 < points.Length - 1)
                    BossRushFxKit.PlayBurst(b, BossRushFxKit.Glint(accent, 0.9f, 0.18f));
            }
            Shake(Vector3.up, 0.12f);
        }

        /// <summary>烟花弹：一颗带拖尾的亮弹，运行时每帧挪动，落地时交回 Release。</summary>
        internal static GameObject CreateFireworkShell(Vector3 position)
        {
            Material hot = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            GameObject shell = new GameObject("DangerousPrototype_FireworkShell");
            shell.transform.position = position;
            if (hot == null) return shell;
            ParticleSystem trail = BossRushFxKit.CreateEmitter("ShellTrail", shell.transform, Vector3.zero, hot, 120, true);
            if (trail != null)
            {
                var main = trail.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
                var color = trail.colorOverLifetime;
                color.enabled = true;
                color.color = BossRushFxKit.FadeGradient(Color.white, new Color(1f, 0.7f, 0.3f, 0.9f), new Color(1f, 0.3f, 0.1f, 0f), 0f);
                var emission = trail.emission;
                emission.rateOverDistance = 9f;
                trail.Play();
            }
            ModEntityFogVisibility.Attach(shell);
            return shell;
        }

        internal static void ReleaseFireworkShell(GameObject shell)
        {
            if (shell != null) BossRushFxKit.Release(shell, 0.1f, 1f, true);
        }

        /// <summary>烟花炸开：亮芯、彩色星屑球形炸开后下坠、贴地冲击环与一记闪光；大号用于主弹。</summary>
        internal static void FireworkBurst(Vector3 position, Color color, float radius, bool big)
        {
            try
            {
                Vector3 air = position + Vector3.up * (big ? 1.6f : 1f);
                BossRushFxKit.PlayBurst(air, BossRushFxKit.Glint(Color.Lerp(color, Color.white, 0.5f), radius * (big ? 1.2f : 0.8f), 0.2f));
                BossRushFxBurst stars = BossRushFxKit.Sparks(color, big ? 70 : 26);
                stars.SpeedMin = radius * 1.6f; stars.SpeedMax = radius * 2.8f; stars.Gravity = 0.45f; stars.Drag = 1.6f;
                stars.LifeMin = 0.6f; stars.LifeMax = big ? 1.4f : 1f; stars.SizeMin = 0.07f; stars.SizeMax = 0.14f; stars.Trail = 0.3f;
                BossRushFxKit.PlayBurst(air, stars);
                BossRushFxKit.PlayBurst(position + Vector3.up * 0.06f,
                    BossRushFxKit.Shockwave(new Color(color.r, color.g, color.b, 0.7f), radius * 0.5f, 2.4f, 0.4f, true));
                if (big)
                {
                    BossRushFxBurst smoke = BossRushFxKit.Dust(new Color(0.3f, 0.28f, 0.3f, 0.5f), 12);
                    smoke.ShapeRadius = radius * 0.5f;
                    BossRushFxKit.PlayBurst(position + Vector3.up * 0.1f, smoke);
                    Shake(Vector3.up, 0.25f);
                }
                GameObject flash = new GameObject("DangerousPrototype_FireworkFlash");
                flash.transform.position = air;
                Light light = flash.AddComponent<Light>();
                light.type = LightType.Point; light.color = color; light.range = radius * 3f; light.intensity = big ? 5f : 2.5f; light.shadows = LightShadows.None;
                BossRushFxLightFade.Attach(light, light.intensity, 0f).FadeOut(0.35f, true);
                ModEntityFogVisibility.Attach(flash);
            }
            catch (System.Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 烟花表现失败: " + e.Message); }
        }

        /// <summary>反弹炮瞄准线：沿当前射向画到前两次反弹，直射段青色、反弹后转金色，越远越淡。</summary>
        internal void ShowGuide(Vector3[] points, int count)
        {
            if (root == null || points == null || count < 2) { HideGuide(); return; }
            if (guide == null)
            {
                guideRoot = new GameObject("DangerousPrototype_RicochetGuide");
                guide = guideRoot.AddComponent<LineRenderer>();
                guide.useWorldSpace = true;
                guide.sharedMaterial = glowStrip;
                guide.numCornerVertices = 2;
                guide.numCapVertices = 2;
                guide.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                guide.receiveShadows = false;
            }
            guide.positionCount = count;
            float total = 0f, first = 0f;
            for (int i = 0; i < count; i++)
            {
                guide.SetPosition(i, points[i]);
                if (i > 0) { total += Vector3.Distance(points[i - 1], points[i]); if (i == 1) first = total; }
            }
            float knee = total > 0.01f ? Mathf.Clamp(first / total, 0.05f, 0.95f) : 0.5f;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(accent, 0f), new GradientColorKey(accent, knee), new GradientColorKey(new Color(1f, 0.7f, 0.2f), Mathf.Min(1f, knee + 0.02f)), new GradientColorKey(new Color(1f, 0.6f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.04f), new GradientAlphaKey(0.45f, knee), new GradientAlphaKey(0f, 1f) });
            guide.colorGradient = gradient;
            guide.enabled = true;
        }

        internal void HideGuide()
        {
            if (guide != null) guide.enabled = false;
        }

        private void Ring(Vector3 center, Vector3 normal, float radius, Color color, float width, float lifetime)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            Vector3 up = Vector3.Cross(normal, right).normalized;
            Vector3[] points = new Vector3[25];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / (points.Length - 1);
                points[i] = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            }
            Stroke(points, color, width, lifetime, true, glowStrip);
        }
        private void Stroke(Vector3[] points, Color color, float width, float lifetime, bool expand, Material material)
        {
            // 最多 112 条短命线，连续枪械的事件频率不影响此上限。
            if (flashes.Count >= 112) return;
            GameObject go = new GameObject("DangerousPrototype_BlastSegment");
            Vector3 center = (points[0] + points[points.Length - 1]) * 0.5f;
            if (expand)
            {
                center = Vector3.zero;
                for (int i = 0; i < points.Length - 1; i++) center += points[i];
                center /= points.Length - 1;
            }
            go.transform.position = center;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material != null ? material : BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) line.SetPosition(i, points[i] - center);
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.startColor = color; line.endColor = color;
            flashes.Add(new Flash { Root = go, Line = line, Lifetime = lifetime, Width = width, Color = color, Expand = expand });
            ModEntityFogVisibility.Attach(go);
        }
        private static void Shake(Vector3 direction, float strength)
        {
            try { CameraShaker.Shake((direction + Vector3.up * 0.3f) * strength, CameraShaker.CameraShakeTypes.explosion); }
            catch { /* 相机未就绪 */ }
        }
        internal static GameObject CreateStation(Vector3 position)
        {
            return CreateStation(position, DangerousPrototypeKind.JetCannon);
        }

        /// <summary>试验台按这趟放的那一件着色，台面上悬着一圈同色的全息环。</summary>
        internal static GameObject CreateStation(Vector3 position, DangerousPrototypeKind offered)
        {
            GameObject station = new GameObject("Dingdang_DangerousPrototypeBench");
            try
            {
            FieldAdventureVisualAssets.Acquire(station);
            station.transform.position = position;
            Color dark = new Color(0.1f, 0.16f, 0.2f);
            Color orange = Accent(offered);
            Part(station.transform, "Workbench", PrimitiveType.Cube, new Vector3(0f, 0.7f, 0f), new Vector3(2.5f, 0.2f, 1.25f), dark);
            for (int i = -1; i <= 1; i += 2)
            {
                Part(station.transform, "Leg", PrimitiveType.Cube, new Vector3(i, 0.34f, 0f), new Vector3(0.2f, 0.7f, 1f), orange);
                Cylinder(station.transform, "PressureTank", new Vector3(i * 0.9f, 1.1f, 0f), 0.32f, 0.5f, orange);
            }
            Part(station.transform, "ControlPanel", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 0.5f, 0.5f), dark);
            for (int i = 0; i < 5; i++)
                Part(station.transform, "ConsoleLight", PrimitiveType.Sphere, new Vector3(-0.28f + i * 0.14f, 1.2f, 0.28f), Vector3.one * 0.09f, orange);
            Part(station.transform, "WarningPole", PrimitiveType.Cube, new Vector3(0f, 1.7f, -0.4f), new Vector3(0.09f, 1.4f, 0.09f), dark);
            Part(station.transform, "WarningSign", PrimitiveType.Cube, new Vector3(0f, 2.3f, -0.4f), new Vector3(1.1f, 0.65f, 0.09f), orange);
            Part(station.transform, "ExclamationStroke", PrimitiveType.Cube, new Vector3(0f, 2.38f, -0.33f), new Vector3(0.14f, 0.3f, 0.04f), dark);
            Part(station.transform, "ExclamationDot", PrimitiveType.Cube, new Vector3(0f, 2.13f, -0.33f), new Vector3(0.14f, 0.1f, 0.04f), dark);
            // 试验台顶上一盏缓慢呼吸的警示灯，远处也认得出「这里能借东西」。
            GameObject beacon = new GameObject("BenchBeacon");
            beacon.transform.SetParent(station.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 2.75f, -0.4f);
            Light light = beacon.AddComponent<Light>();
            light.type = LightType.Point; light.color = orange; light.range = 4.5f; light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, 1.4f, 0.6f, 0.3f, 2.6f);
            ModEntityFogVisibility.Attach(station);
            return station;
            }
            catch { station.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(station); UnityEngine.Object.Destroy(station); throw; }
        }
        private static GameObject Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = FieldAdventureVisualAssets.Solid;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            Tint(renderer, color, new MaterialPropertyBlock());
            return part;
        }
        private static GameObject Cylinder(Transform parent, string name, Vector3 position, float radius, float halfLength, Color color)
        {
            GameObject cylinder = Part(parent, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2f, halfLength, radius * 2f), color);
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return cylinder;
        }
        private static void Tint(Renderer renderer, Color color, MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            block.Clear(); block.SetColor("_BaseColor", color); block.SetColor("_TintColor", color * 0.5f);
            renderer.SetPropertyBlock(block);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (root != null) { root.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(root); UnityEngine.Object.Destroy(root); }
            if (guideRoot != null) UnityEngine.Object.Destroy(guideRoot);
            guideRoot = null; guide = null;
            foreach (Flash flash in flashes)
                if (flash.Root != null) { flash.Root.SetActive(false); UnityEngine.Object.Destroy(flash.Root); }
            flashes.Clear();
        }
    }
}
