using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 一只多关节巨口宝箱、三类可见器官、随倒计时烧满的预警、飞行炮弹与共享粒子爆发。战斗判定全部由 encounter owner 持有。
    /// 表现口径对齐冠军之影：预警是「慢慢亮起 + 最后加速明灭」，出手是「亮芯 + 冲击环 + 碎屑 + 尘」的一记爆发，死亡先鼓胀再崩开，
    /// 不用实心几何体当火花，也不让任何部件在同一帧硬切消失。
    /// </summary>
    internal sealed class GreedyMimicVisual : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Transform Facing;
        internal readonly Transform Body;
        private readonly Transform jaw, healthRoot;
        private readonly Bounds bodyDamageBounds, lidWoodDamageBounds, lidLipDamageBounds;
        private const int BeamSegments = 8;
        private const int TongueSegments = 12;
        internal const int ShellCapacity = 10;
        private readonly Transform[] tongue = new Transform[TongueSegments];
        private readonly Transform[] legs = new Transform[4];
        private readonly GameObject[] mutations = new GameObject[3];
        private readonly float[] mutationPop = new float[3], mutationSize = new float[3];
        private readonly GameObject[] marks = new GameObject[5];
        private readonly MeshRenderer[] markRims = new MeshRenderer[5], markFills = new MeshRenderer[5], markCores = new MeshRenderer[5];
        private readonly float[] markRadius = new float[5];
        private readonly bool[] markHealing = new bool[5];
        private readonly LineRenderer[,] rays = new LineRenderer[5, BeamSegments];
        private readonly bool[] laneActive = new bool[5];
        private readonly Transform[] bombs = new Transform[5];
        private readonly Transform[] shells = new Transform[ShellCapacity];
        private readonly Renderer[] eyes = new Renderer[2];
        private readonly LineRenderer healthBack, healthFill, healthLag;
        private readonly MaterialPropertyBlock colors = new MaterialPropertyBlock();
        private readonly Material solid, glow, softGlow, strip;
        private readonly Mesh ringMesh, toothMesh, discMesh;
        private float clock, swallow, scale = 1f;
        private float warningTotal = -1f, lastWarning, lastHealth = 1f, shownHealth = 1f, lagHealth = 1f, hitFlash, nextHitFx;
        private float leap = -1f, dying = -1f;
        private bool popped, disposed;
        private GreedyMimicAttack currentAttack;
        private static readonly Color Wood = new Color(0.24f, 0.08f, 0.035f);
        private static readonly Color Gold = new Color(0.95f, 0.57f, 0.13f);
        private static readonly Color Flesh = new Color(0.55f, 0.07f, 0.13f);
        private static readonly Color Red = new Color(1f, 0.19f, 0.055f);
        private static readonly Color Green = new Color(0.2f, 1f, 0.53f);
        private static readonly Color Coin = new Color(1f, 0.78f, 0.3f);
        private static readonly Color Splinter = new Color(0.36f, 0.16f, 0.07f, 0.85f);

        internal GreedyMimicVisual(Scene scene, Vector3 position)
        {
            try
            {
            Root = new GameObject("BossRush_GreedyMimic");
            SceneManager.MoveGameObjectToScene(Root, scene);
            Root.transform.position = position;
            solid = FieldAdventureVisualAssets.Acquire(Root);
            glow = BossRushFxMaterials.Get(BossRushFxBlend.Additive, Texture2D.whiteTexture);
            softGlow = BossRushFxKit.GetMaterial(BossRushFxBlend.Additive, BossRushFxMaterials.GetSoftCircleTexture(), BossRushFxKit.GainBright);
            strip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (solid == null || glow == null || softGlow == null || strip == null) { UnityEngine.Object.Destroy(Root); throw new InvalidOperationException("宝箱怪表现材质不可用"); }
            ringMesh = BuildRing(); toothMesh = BuildTooth(); discMesh = BuildDisc();
            // 受击体与模型共享单位缩放的朝向；箱身和箱盖的当帧包络由 encounter 同步到受击盒。
            Facing = Group("StableFacing", Root.transform, Vector3.zero);
            Body = Group("BreathingBody", Facing, Vector3.zero);
            Transform[] bodyParts = new Transform[10], lidParts = new Transform[2];
            bodyParts[0] = Shape("ChestBelly", PrimitiveType.Cube, Body, new Vector3(0, 1.1f, 0), new Vector3(2.7f, 1.3f, 1.8f), Wood);
            bodyParts[1] = Shape("Gums", PrimitiveType.Cube, Body, new Vector3(0, 1.75f, 0), new Vector3(2.65f, 0.2f, 1.8f), Flesh);
            for (int i = -1; i <= 1; i++)
            {
                bodyParts[2 + (i + 1) * 2] = Shape("IronBand", PrimitiveType.Cube, Body, new Vector3(i * 1.08f, 1.12f, 0), new Vector3(0.14f, 1.4f, 1.9f), Gold);
                bodyParts[3 + (i + 1) * 2] = Shape("RivetedPlank", PrimitiveType.Cube, Body, new Vector3(0, 0.68f + (i + 1) * 0.42f, 0.925f), new Vector3(2.8f, 0.1f, 0.08f), Gold * 0.7f);
            }
            bodyParts[8] = Shape("LockPlate", PrimitiveType.Sphere, Body, new Vector3(0, 1.1f, 0.99f), new Vector3(0.55f, 0.7f, 0.15f), Gold);
            bodyParts[9] = Shape("LockHole", PrimitiveType.Cube, Body, new Vector3(0, 1.08f, 1.08f), new Vector3(0.13f, 0.28f, 0.06f), Color.black);
            jaw = Group("HingedLid", Body, new Vector3(0, 1.8f, -0.8f));
            lidParts[0] = Shape("RoundedLid", PrimitiveType.Capsule, jaw, new Vector3(0, 0.22f, 0.8f), new Vector3(1.1f, 1.4f, 1.8f), Wood);
            lidParts[0].localRotation = Quaternion.Euler(0, 0, 90);
            lidParts[1] = Shape("LidLip", PrimitiveType.Cube, jaw, new Vector3(0, 0, 0.8f), new Vector3(2.85f, 0.2f, 1.9f), Gold);
            // 只缓存主箱身与主箱盖的网格包络；眼、牙、腿、突变器官和世界效果不扩大受击区域。
            bodyDamageBounds = PartBounds(bodyParts);
            lidWoodDamageBounds = PartBounds(lidParts[0]);
            lidLipDamageBounds = PartBounds(lidParts[1]);
            for (int i = 0; i < 7; i++)
            {
                float x = -1.12f + i * 0.37f;
                Tooth(Body, new Vector3(x, 1.76f, 0.82f), false);
                Tooth(jaw, new Vector3(x, -0.08f, 1.65f), true);
            }
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.65f : 0.65f;
                Shape("EyeSocket", PrimitiveType.Sphere, jaw, new Vector3(x, 0.58f, 1.03f), new Vector3(0.7f, 0.7f, 0.45f), Color.black);
                eyes[i] = Shape("HungryEye", PrimitiveType.Sphere, jaw, new Vector3(x, 0.58f, 1.25f), new Vector3(0.45f, 0.45f, 0.17f), Gold, true).GetComponent<Renderer>();
                Shape("EyeSlit", PrimitiveType.Cube, jaw, new Vector3(x, 0.58f, 1.36f), new Vector3(0.1f, 0.4f, 0.05f), Color.black);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -1.15f : 1.15f, 0.65f, i < 2 ? 0.6f : -0.6f);
                legs[i] = Group("BentLeg", Body, p);
                Shape("Thigh", PrimitiveType.Capsule, legs[i], new Vector3(0, -0.12f, 0), new Vector3(0.38f, 0.4f, 0.4f), Flesh).localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 35 : -35);
                Shape("ClawedFoot", PrimitiveType.Cube, legs[i], new Vector3(i % 2 == 0 ? -0.2f : 0.2f, -0.5f, 0.15f), new Vector3(0.6f, 0.2f, 0.65f), Gold);
            }
            for (int i = 0; i < TongueSegments; i++)
            {
                tongue[i] = Shape("WhippingTongueSegment", PrimitiveType.Capsule, Root.transform, Vector3.zero, Vector3.one, Flesh);
                tongue[i].gameObject.SetActive(false);
            }
            mutations[0] = Group("PopcornCannons", Body, new Vector3(0, 1.4f, 0)).gameObject;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (1.35f + (i / 2) * 0.27f);
                Shape("BrassBarrel", PrimitiveType.Cylinder, mutations[0].transform, new Vector3(x, i / 2 * 0.3f, 0.7f), new Vector3(0.34f, 0.8f, 0.34f), Gold).localRotation = Quaternion.Euler(90, 0, 0);
                Shape("BlackMuzzle", PrimitiveType.Sphere, mutations[0].transform, new Vector3(x, i / 2 * 0.3f, 1.5f), new Vector3(0.29f, 0.29f, 0.12f), Color.black);
            }
            mutations[1] = Group("BombBelly", Body, new Vector3(0, 1.15f, -1.15f)).gameObject;
            for (int i = 0; i < 3; i++)
            {
                Shape("BombSac", PrimitiveType.Sphere, mutations[1].transform, new Vector3((i - 1) * 0.8f, i % 2 * 0.5f, 0), new Vector3(1f, 1f, 1f), new Color(0.13f, 0.16f, 0.18f));
                Shape("BurningFuse", PrimitiveType.Cylinder, mutations[1].transform, new Vector3((i - 1) * 0.8f, 0.65f + i % 2 * 0.5f, 0), new Vector3(0.12f, 0.25f, 0.12f), Red, true);
            }
            mutations[2] = Group("RidiculousMedicineBottle", Body, new Vector3(0, 2.75f, -0.7f)).gameObject;
            Shape("BottleBody", PrimitiveType.Cylinder, mutations[2].transform, Vector3.zero, new Vector3(1f, 0.9f, 1f), new Color(0.13f, 0.65f, 0.43f));
            Shape("BottleCap", PrimitiveType.Cylinder, mutations[2].transform, new Vector3(0, 1f, 0), new Vector3(0.7f, 0.16f, 0.7f), Gold);
            Shape("CrossA", PrimitiveType.Cube, mutations[2].transform, new Vector3(0, 0, 0.52f), new Vector3(0.6f, 0.16f, 0.05f), Color.white);
            Shape("CrossB", PrimitiveType.Cube, mutations[2].transform, new Vector3(0, 0, 0.52f), new Vector3(0.16f, 0.6f, 0.05f), Color.white);
            // 世界血条：始终朝镜头、抵消箱身呼吸缩放；受伤先掉亮条，白色残影随后追上，读得出「这一下打了多少」。
            healthRoot = Group("WorldHealth", Body, new Vector3(0, 3.95f, 0));
            healthBack = Bar("HealthBacking", healthRoot, BossRushFxKit.GetMaterial(BossRushFxBlend.Alpha, Texture2D.whiteTexture, BossRushFxKit.GainSoft),
                new Color(0.03f, 0.02f, 0.02f, 0.72f), 0.2f);
            healthLag = Bar("HealthLag", healthRoot, strip, new Color(1f, 0.9f, 0.78f, 0.55f), 0.12f);
            healthFill = Bar("HealthFill", healthRoot, strip, Red, 0.12f);
            healthLag.transform.localPosition = Vector3.back * 0.01f;
            healthFill.transform.localPosition = Vector3.back * 0.02f;
            SetBar(healthBack, 1f, 1.36f);
            for (int i = 0; i < 5; i++)
            {
                marks[i] = new GameObject("MimicWarningRing");
                marks[i].transform.SetParent(Root.transform, false);
                markFills[i] = Disc(marks[i].transform, "WarningFill", BossRushFxKit.GetMaterial(BossRushFxBlend.Alpha, Texture2D.whiteTexture, BossRushFxKit.GainSoft));
                markCores[i] = Disc(marks[i].transform, "WarningCountdown", softGlow);
                markCores[i].transform.localPosition = Vector3.up * 0.01f;
                GameObject rim = new GameObject("WarningRim");
                rim.transform.SetParent(marks[i].transform, false);
                rim.transform.localPosition = Vector3.up * 0.02f;
                rim.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                markRims[i] = rim.AddComponent<MeshRenderer>();
                Paint(markRims[i], Red, true);
                for (int j = 0; j < BeamSegments; j++)
                {
                    Transform beam = Group("LockedVolleyLaneSegment", Root.transform, Vector3.zero);
                    LineRenderer ray = rays[i, j] = beam.gameObject.AddComponent<LineRenderer>(); ray.sharedMaterial = strip;
                    ray.useWorldSpace = true; ray.positionCount = 2; ray.startWidth = ray.endWidth = 0.12f;
                    ray.startColor = ray.endColor = Red; ray.numCapVertices = 2; Quiet(ray); beam.gameObject.SetActive(false);
                }
                bombs[i] = Shape("AirborneBomb", PrimitiveType.Sphere, Root.transform, Vector3.zero, Vector3.one * 0.75f, new Color(0.12f, 0.13f, 0.15f));
                Shape("BombFuseSpark", PrimitiveType.Sphere, bombs[i], new Vector3(0f, 0.55f, 0f), Vector3.one * 0.32f, Coin, true);
                marks[i].SetActive(false); bombs[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < ShellCapacity; i++)
            {
                shells[i] = Shape("PopcornShell", PrimitiveType.Sphere, Root.transform, Vector3.zero, Vector3.one * 0.42f, Gold);
                Shape("ShellGlow", PrimitiveType.Sphere, shells[i], Vector3.zero, Vector3.one * 0.7f, Coin, true);
                TrailRenderer trail = shells[i].gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = strip; trail.time = 0.16f; trail.minVertexDistance = 0.1f;
                trail.widthMultiplier = 0.32f; trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.colorGradient = TrailGradient(); trail.emitting = false; Quiet(trail);
                shells[i].gameObject.SetActive(false);
            }
            foreach (GameObject mutation in mutations) mutation.SetActive(false);
            // 先建好所有渲染器，再接迷雾。预警落点单独采样，不能用宝箱可见性强制显示墙后的落点。
            ModEntityFogVisibility.Attach(Body.gameObject, Body);
            for (int j = 0; j < TongueSegments; j++) ModEntityFogVisibility.Attach(tongue[j].gameObject, tongue[j]);
            for (int i = 0; i < 5; i++)
            {
                ModEntityFogVisibility.Attach(marks[i]); ModEntityFogVisibility.Attach(bombs[i].gameObject);
                for (int j = 0; j < BeamSegments; j++) ModEntityFogVisibility.Attach(rays[i, j].gameObject);
            }
            for (int i = 0; i < ShellCapacity; i++) ModEntityFogVisibility.Attach(shells[i].gameObject, shells[i]);
            ApplyBodyPose(false, 0f);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>世界血条同样服从官方 HUD 与暂停双门；宿主在暂停早返前也调用。</summary>
        internal void SyncHudVisibility()
        {
            if (healthRoot == null) return;
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (dying >= 0f) visible = false;
            if (healthRoot.gameObject.activeSelf != visible) healthRoot.gameObject.SetActive(visible);
        }

        internal void Grow(GreedyMimicRules rules)
        {
            scale = rules.Scale; swallow = 1.4f;
            ApplyBodyPose(false, 0f);
            for (int i = 0; i < 3; i++)
            {
                int level = rules.Level((GreedyMimicFood)i);
                bool fresh = level > 0 && !mutations[i].activeSelf;
                mutations[i].SetActive(level > 0);
                if (fresh) mutationPop[i] = 1f;
                mutationSize[i] = 1f + Mathf.Max(0, level - 1) * 0.22f;
                mutations[i].transform.localScale = Vector3.one * mutationSize[i] * PopScale(mutationPop[i]);
            }
            // 咽下去的一口：金星从箱口往外迸，脚下扬起一圈尘，整只箱子随吞咽动画鼓胀。
            Vector3 mouth = Root.transform.position + Vector3.up * (1.9f * scale);
            BossRushFxBurst sparkle = BossRushFxKit.Sparks(Coin, 18);
            sparkle.Upward = true; sparkle.SpeedMin = 2.5f; sparkle.SpeedMax = 5.5f; sparkle.Gravity = 0.9f;
            sparkle.LifeMin = 0.4f; sparkle.LifeMax = 0.8f;
            Fx(mouth, sparkle);
            BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.5f, 0.42f, 0.34f, 0.45f), 12);
            dust.ShapeRadius = 1.4f * scale;
            Fx(Root.transform.position + Vector3.up * 0.1f, dust);
            Fx(Root.transform.position + Vector3.up * 0.06f, BossRushFxKit.Shockwave(new Color(1f, 0.75f, 0.35f, 0.5f), 1.6f * scale, 2.2f, 0.45f, true));
        }

        /// <summary>扑跳进度（0..1）；-1 表示落地。由 encounter 每帧在移动根之前写入。</summary>
        internal void SetLeap(float progress)
        {
            leap = progress;
        }

        internal void Tick(float dt, Vector3 player, bool fighting, float healthFraction, float warning, GreedyMimicAttack attack)
        {
            if (disposed || Root == null) return;
            SyncHudVisibility();
            clock += dt; swallow = Mathf.Max(0, swallow - dt);
            currentAttack = attack;
            if (warning > 0f && (warningTotal < 0f || warning > lastWarning + 0.0001f)) warningTotal = warning;
            lastWarning = warning;
            float progress = warning > 0f && warningTotal > 0f ? Mathf.Clamp01(1f - warning / warningTotal) : 0f;
            for (int i = 0; i < 3; i++)
                if (mutationPop[i] > 0f && dt > 0f)
                {
                    mutationPop[i] = Mathf.Max(0f, mutationPop[i] - dt / 0.45f);
                    mutations[i].transform.localScale = Vector3.one * mutationSize[i] * PopScale(mutationPop[i]);
                }
            if (dying >= 0f) { TickDeath(dt); return; }
            ApplyBodyPose(fighting, warning);
            Vector3 facing = player - Root.transform.position; facing.y = 0;
            if (facing.sqrMagnitude > 0.1f && warning <= 0f) Facing.rotation = Quaternion.Slerp(Facing.rotation, Quaternion.LookRotation(facing), Mathf.Min(1, dt * 3f));
            for (int i = 0; i < legs.Length; i++) legs[i].localRotation = Quaternion.Euler(Mathf.Sin(clock * (fighting ? 8 : 3) + i * Mathf.PI) * 16, 0, 0);
            float fraction = Mathf.Clamp01(healthFraction);
            TickHealth(dt, fraction);
            if (mutations[2].activeSelf) mutations[2].transform.localRotation = Quaternion.Euler(0, 0, attack == GreedyMimicAttack.Recovery && warning > 0 ? Mathf.Sin(clock * 12) * 25 : 0);
            TickWarnings(progress);
            TickEyes(dt, warning > 0f, progress);
        }

        private void ApplyBodyPose(bool fighting, float warning)
        {
            float chew = swallow > 0 ? Mathf.Sin(swallow * 17f) * 0.16f : Mathf.Sin(clock * 2.4f) * 0.025f;
            Vector3 squash = Vector3.one;
            float opening = swallow > 0 ? 25f + Mathf.Abs(Mathf.Sin(swallow * 12)) * 55f : warning > 0 ? 70f : 18f + Mathf.Sin(clock * 3) * 8f;
            if (leap >= 0f)
            {
                // 起跳拉长、落地前压扁；空中张大嘴。
                float stretch = Mathf.Sin(leap * Mathf.PI);
                squash = new Vector3(1f - stretch * 0.08f, 1f + stretch * 0.14f, 1f - stretch * 0.08f);
                opening = 30f + stretch * 30f;
            }
            else if (warning > 0f && currentAttack == GreedyMimicAttack.Pounce && warningTotal > 0f)
            {
                // 扑跳蓄力：越接近起跳压得越低，嘴闭紧。
                float crouch = Mathf.Clamp01(1f - warning / warningTotal);
                squash = new Vector3(1f + crouch * 0.07f, 1f - crouch * 0.16f, 1f + crouch * 0.07f);
                opening = 12f;
            }
            Body.localScale = Vector3.Scale(new Vector3(1f + chew, 1f - chew * 0.7f, 1f + chew), squash) * scale;
            Body.localPosition = Vector3.up * (leap >= 0f ? 0f : Mathf.Abs(Mathf.Sin(clock * (fighting ? 6 : 2))) * 0.09f);
            jaw.localRotation = Quaternion.Euler(-opening, 0, 0);
        }

        private void TickHealth(float dt, float fraction)
        {
            if (fraction < lastHealth - 0.0005f && clock >= nextHitFx)
            {
                // 中弹：木屑从箱身崩出、双眼一闪；箱身不做缩放回弹，受击包络始终贴着网格。
                nextHitFx = clock + 0.09f;
                hitFlash = 1f;
                BossRushFxBurst chips = BossRushFxKit.Dust(Splinter, 5);
                chips.Shape = BossRushParticleShape.Shard;
                chips.Radial = false; chips.FlatOnGround = false; chips.Upward = true;
                chips.SpeedMin = 3f; chips.SpeedMax = 6f; chips.Gravity = 1.4f; chips.Drag = 1f;
                chips.SizeMin = 0.12f; chips.SizeMax = 0.24f; chips.LifeMin = 0.4f; chips.LifeMax = 0.7f;
                chips.GrowTo = 0.8f; chips.Spin = 360f; chips.FadeIn = 0f;
                Fx(Body.position + Vector3.up * (1.3f * scale), chips);
            }
            lastHealth = fraction;
            if (dt > 0f)
            {
                shownHealth = Mathf.MoveTowards(shownHealth, fraction, dt * 2.5f);
                lagHealth = lagHealth > shownHealth ? Mathf.MoveTowards(lagHealth, shownHealth, dt * (lagHealth - shownHealth > 0.02f ? 0.35f : 1f)) : shownHealth;
                hitFlash = Mathf.Max(0f, hitFlash - dt * 5f);
            }
            if (healthRoot == null || !healthRoot.gameObject.activeSelf) return;
            Vector3 body = Body.localScale;
            healthRoot.localScale = new Vector3(1f / Mathf.Max(0.01f, body.x), 1f / Mathf.Max(0.01f, body.y), 1f / Mathf.Max(0.01f, body.z));
            Camera view = Camera.main;
            if (view != null) healthRoot.rotation = view.transform.rotation;
            SetBar(healthFill, shownHealth, 1.3f);
            SetBar(healthLag, lagHealth, 1.3f);
            Color fill = Color.Lerp(new Color(1f, 0.12f, 0.06f), new Color(1f, 0.55f, 0.16f), shownHealth);
            fill = Color.Lerp(fill, Color.white, hitFlash * 0.6f);
            healthFill.startColor = healthFill.endColor = fill;
        }

        private void TickWarnings(float progress)
        {
            float urgency = Mathf.Clamp01((progress - 0.7f) / 0.3f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(clock * (8f + urgency * 20f));
            for (int i = 0; i < marks.Length; i++)
            {
                if (!marks[i].activeSelf) continue;
                Color tone = markHealing[i] ? Green : Red;
                Tint(markFills[i], new Color(tone.r * 0.5f, tone.g * 0.5f, tone.b * 0.5f, 0.1f + progress * 0.14f));
                float fill = progress * progress * (3f - 2f * progress);
                markCores[i].transform.localScale = Vector3.one * Mathf.Max(0.001f, fill);
                Tint(markCores[i], new Color(tone.r, tone.g, tone.b, 0.12f + progress * 0.22f + urgency * pulse * 0.15f));
                Color rim = Color.Lerp(tone, Color.white, urgency * pulse * 0.55f);
                rim.a = 0.55f + progress * 0.35f;
                Tint(markRims[i], rim);
            }
            for (int i = 0; i < laneActive.Length; i++)
            {
                if (!laneActive[i]) continue;
                Color lane = Color.Lerp(new Color(1f, 0.25f, 0.08f, 0.35f + progress * 0.4f), new Color(1f, 0.85f, 0.6f, 1f), urgency * pulse);
                float width = 0.07f + progress * 0.15f;
                for (int j = 0; j < BeamSegments; j++)
                {
                    LineRenderer ray = rays[i, j];
                    if (!ray.gameObject.activeSelf) continue;
                    ray.startColor = ray.endColor = lane;
                    ray.startWidth = ray.endWidth = width;
                }
            }
        }

        private void TickEyes(float dt, bool warning, float progress)
        {
            Color eye = Gold;
            if (warning) eye = Color.Lerp(Gold, Red, 0.4f + 0.6f * progress);
            eye = Color.Lerp(eye, Color.white, hitFlash);
            for (int i = 0; i < eyes.Length; i++) Tint(eyes[i], eye);
        }

        internal void GetDamageBounds(out Bounds bodyBounds, out Bounds lidBounds)
        {
            Matrix4x4 bodyToFacing = Matrix4x4.TRS(Body.localPosition, Body.localRotation, Body.localScale);
            bodyBounds = TransformBounds(bodyDamageBounds, bodyToFacing);
            Matrix4x4 lidToFacing = bodyToFacing * Matrix4x4.TRS(jaw.localPosition, jaw.localRotation, jaw.localScale);
            // 箱盖的木壳和金属唇先各自转动再合并，避免把二者未占据的局部角点也随张口放大。
            lidBounds = TransformBounds(lidWoodDamageBounds, lidToFacing);
            lidBounds.Encapsulate(TransformBounds(lidLipDamageBounds, lidToFacing));
        }
        private static Bounds PartBounds(Transform[] parts)
        {
            Bounds result = PartBounds(parts[0]);
            for (int i = 1; i < parts.Length; i++) result.Encapsulate(PartBounds(parts[i]));
            return result;
        }
        private static Bounds PartBounds(Transform part)
        {
            return TransformBounds(part.GetComponent<MeshFilter>().sharedMesh.bounds,
                Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale));
        }
        private static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
        {
            Vector3 min = source.min, max = source.max;
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)));
            return result;
        }
        internal void ShowCircle(int index, Vector3 position, float radius, bool healing)
        {
            marks[index].transform.position = position + Vector3.up * 0.055f;
            marks[index].transform.localScale = new Vector3(radius, 1, radius);
            markRadius[index] = radius;
            markHealing[index] = healing;
            warningTotal = -1f;
            marks[index].SetActive(true);
            TickWarnings(0f);
        }
        internal void ShowLane(int index, Vector3 start, Vector3 end)
        {
            // 32 米弹道预建八段，每段独立取样迷雾，远端不能跟随中点露出黑区。
            end = start + Vector3.ClampMagnitude(end - start, 32f);
            for (int j = 0; j < BeamSegments; j++)
            {
                Vector3 a = Vector3.Lerp(start, end, (float)j / BeamSegments), b = Vector3.Lerp(start, end, (float)(j + 1) / BeamSegments);
                LineRenderer ray = rays[index, j]; ray.transform.position = (a + b) * 0.5f;
                ray.SetPosition(0, a + Vector3.up * 0.12f); ray.SetPosition(1, b + Vector3.up * 0.12f);
                ray.gameObject.SetActive(true);
            }
            laneActive[index] = true;
            warningTotal = -1f;
            TickWarnings(0f);
        }
        internal void AnimateBomb(int index, Vector3 origin, Vector3 target, float progress)
        {
            bombs[index].gameObject.SetActive(true);
            bombs[index].position = Vector3.Lerp(origin, target + Vector3.up * 0.4f, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * 7f;
            bombs[index].localScale = Vector3.one * (0.7f + Mathf.Sin(progress * 35) * 0.12f);
            bombs[index].Rotate(0f, 0f, 420f * Time.deltaTime);
        }
        /// <summary>飞行炮弹：encounter 每帧写位置；隐藏时尾迹停发，不在原地残留一截光带。</summary>
        internal void SetShell(int index, Vector3 position, bool visible)
        {
            if (index < 0 || index >= shells.Length) return;
            Transform shell = shells[index];
            TrailRenderer trail = shell.GetComponent<TrailRenderer>();
            if (!visible)
            {
                if (shell.gameObject.activeSelf) shell.gameObject.SetActive(false);
                return;
            }
            if (!shell.gameObject.activeSelf)
            {
                shell.position = position;
                shell.gameObject.SetActive(true);
                if (trail != null) { trail.Clear(); trail.emitting = true; }
            }
            shell.position = position;
            shell.Rotate(540f * Time.deltaTime, 0f, 260f * Time.deltaTime);
        }
        /// <summary>炮弹撞墙或命中：一小簇金火星。</summary>
        internal void ShellPop(Vector3 position)
        {
            BossRushFxBurst sparks = BossRushFxKit.Sparks(Coin, 9);
            sparks.SpeedMin = 3f; sparks.SpeedMax = 6f; sparks.Gravity = 0.8f;
            Fx(position, sparks);
            Fx(position, BossRushFxKit.Glint(Coin, 0.7f, 0.12f));
        }
        /// <summary>开火：四根炮管口各喷一下。</summary>
        internal void Muzzle()
        {
            Vector3 forward = Facing.forward;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 at = Body.TransformPoint(new Vector3(side * 1.45f, 1.55f, 1.6f));
                BossRushFxBurst flash = BossRushFxKit.Sparks(Coin, 10);
                flash.Cone = 18f; flash.Direction = forward; flash.SpeedMin = 6f; flash.SpeedMax = 11f; flash.LifeMin = 0.12f; flash.LifeMax = 0.22f;
                Fx(at, flash);
                BossRushFxBurst smoke = BossRushFxKit.Dust(new Color(0.55f, 0.52f, 0.5f, 0.4f), 4);
                smoke.Radial = false; smoke.FlatOnGround = false; smoke.Cone = 25f; smoke.Direction = forward;
                smoke.SpeedMin = 1.5f; smoke.SpeedMax = 3f; smoke.SizeMin = 0.35f; smoke.SizeMax = 0.6f;
                Fx(at, smoke);
            }
            ShakeNear(Root.transform.position, 0.08f, 20f);
        }
        /// <summary>扑跳落地：贴地冲击环、扬尘、几块飞起的碎土，镜头一震。</summary>
        internal void Slam(Vector3 position, float radius)
        {
            Fx(position + Vector3.up * 0.06f, BossRushFxKit.Shockwave(new Color(1f, 0.7f, 0.42f, 0.7f), radius * 0.5f, 2.6f, 0.45f, true));
            BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.48f, 0.4f, 0.32f, 0.6f), 22);
            dust.ShapeRadius = radius * 0.5f; dust.SpeedMin = radius * 1.4f; dust.SpeedMax = radius * 2.4f; dust.FastGrow = true;
            Fx(position + Vector3.up * 0.1f, dust);
            BossRushFxBurst clods = BossRushFxKit.Dust(new Color(0.3f, 0.24f, 0.18f, 0.9f), 10);
            clods.Shape = BossRushParticleShape.Shard; clods.Radial = false; clods.FlatOnGround = false; clods.Upward = true;
            clods.SpeedMin = 4f; clods.SpeedMax = 8f; clods.Gravity = 1.6f; clods.SizeMin = 0.15f; clods.SizeMax = 0.3f;
            clods.Spin = 300f; clods.GrowTo = 0.8f; clods.FadeIn = 0f; clods.LifeMin = 0.6f; clods.LifeMax = 1f;
            Fx(position + Vector3.up * 0.2f, clods);
            ShakeNear(position, 0.28f, 18f);
        }
        /// <summary>巨型炸弹落地：亮芯、火球、冲击环、烟柱与火星。</summary>
        internal void Explode(Vector3 position, float radius)
        {
            Fx(position + Vector3.up * 0.5f, BossRushFxKit.Glint(new Color(1f, 0.7f, 0.3f), radius * 1.1f, 0.18f));
            Fx(position + Vector3.up * 0.06f, BossRushFxKit.Shockwave(new Color(1f, 0.45f, 0.12f, 0.8f), radius * 0.55f, 2.6f, 0.4f, true));
            BossRushFxBurst fire = BossRushFxKit.Sparks(new Color(1f, 0.45f, 0.1f), 30);
            fire.Upward = true; fire.SpeedMin = 4f; fire.SpeedMax = 10f; fire.Gravity = 0.9f; fire.Drag = 1.2f;
            fire.LifeMin = 0.35f; fire.LifeMax = 0.8f; fire.SizeMin = 0.06f; fire.SizeMax = 0.12f;
            Fx(position + Vector3.up * 0.3f, fire);
            BossRushFxBurst smoke = BossRushFxKit.Dust(new Color(0.16f, 0.14f, 0.13f, 0.65f), 14);
            smoke.Radial = false; smoke.FlatOnGround = false; smoke.Upward = true; smoke.ShapeRadius = radius * 0.3f;
            smoke.SpeedMin = 1.5f; smoke.SpeedMax = 3.5f; smoke.SizeMin = 0.9f; smoke.SizeMax = 1.6f; smoke.LifeMin = 0.9f; smoke.LifeMax = 1.5f;
            Fx(position + Vector3.up * 0.4f, smoke);
            FlashLight(position + Vector3.up, new Color(1f, 0.55f, 0.2f), radius * 2.5f, 5f, 0.35f);
            ShakeNear(position, 0.32f, 22f);
        }
        internal void StrikeTongue(Vector3 target)
        {
            Vector3 start = Root.transform.position + Vector3.up * 1.6f, end = target + Vector3.up * 0.4f;
            // 咬击沿用锁定落点，覆盖 45 米战斗距离；十二段把每次独立迷雾采样控制在约四米内。
            for (int j = 0; j < TongueSegments; j++)
            {
                Vector3 a = Vector3.Lerp(start, end, (float)j / TongueSegments), b = Vector3.Lerp(start, end, (float)(j + 1) / TongueSegments);
                tongue[j].gameObject.SetActive(true); tongue[j].position = (a + b) * 0.5f;
                tongue[j].rotation = Quaternion.FromToRotation(Vector3.up, b - a); tongue[j].localScale = new Vector3(0.65f, Vector3.Distance(a, b) * 0.5f, 0.65f);
            }
            BossRushFxBurst spit = BossRushFxKit.Sparks(new Color(1f, 0.55f, 0.6f), 8);
            spit.SpeedMin = 2f; spit.SpeedMax = 4f; spit.Gravity = 1.2f;
            Fx(end, spit);
        }
        internal void ClearAttack()
        {
            for (int j = 0; j < TongueSegments; j++) tongue[j].gameObject.SetActive(false);
            for (int i = 0; i < marks.Length; i++)
            {
                marks[i].SetActive(false); bombs[i].gameObject.SetActive(false); laneActive[i] = false;
                for (int j = 0; j < BeamSegments; j++) rays[i, j].gameObject.SetActive(false);
            }
            warningTotal = -1f;
        }
        /// <summary>通用一记爆发：金币火星 + 木屑 + 尘（喂食、回血、打断、出手共用）。</summary>
        internal void Burst(Vector3 position, float duration)
        {
            float size = Mathf.Clamp(duration, 0.4f, 2f);
            BossRushFxBurst coins = BossRushFxKit.Sparks(Coin, Mathf.RoundToInt(14 * size));
            coins.Upward = true; coins.SpeedMin = 3f; coins.SpeedMax = 6f + size * 2f; coins.Gravity = 1.1f; coins.Drag = 0.6f;
            coins.LifeMin = 0.45f; coins.LifeMax = 0.9f; coins.SizeMin = 0.07f; coins.SizeMax = 0.12f;
            Fx(position, coins);
            Fx(position, BossRushFxKit.Glint(Coin, 0.9f * size, 0.16f));
        }
        internal void Heal(Vector3 position)
        {
            BossRushFxBurst motes = BossRushFxKit.Sparks(Green, 26);
            motes.Upward = true; motes.SpeedMin = 1.5f; motes.SpeedMax = 3.5f; motes.Gravity = -0.25f; motes.Drag = 0.9f;
            motes.LifeMin = 0.7f; motes.LifeMax = 1.2f; motes.FadeIn = 0.1f; motes.ShapeRadius = 1.2f * scale;
            Fx(position, motes);
            Fx(position, BossRushFxKit.Shockwave(new Color(0.35f, 1f, 0.6f, 0.55f), 1.2f * scale, 2.4f, 0.5f, true));
        }
        /// <summary>战败：先鼓胀、箱盖乱拍，约 0.35 秒后崩开喷出金币与木屑，箱身缩没；全程不硬切。</summary>
        internal void Defeated()
        {
            ClearAttack();
            for (int i = 0; i < shells.Length; i++) SetShell(i, Vector3.zero, false);
            dying = 0f;
            popped = false;
            SyncHudVisibility();
        }
        private void TickDeath(float dt)
        {
            dying += dt;
            float inflate = Mathf.Clamp01(dying / 0.35f);
            if (!popped)
            {
                float tremble = Mathf.Sin(clock * 55f) * 0.04f * inflate;
                Body.localScale = new Vector3(1f + inflate * 0.22f + tremble, 1f + inflate * 0.16f - tremble, 1f + inflate * 0.22f + tremble) * scale;
                jaw.localRotation = Quaternion.Euler(-(25f + Mathf.Abs(Mathf.Sin(clock * 30f)) * 60f), 0f, 0f);
                if (dying >= 0.35f) Pop();
                return;
            }
            float shrink = Mathf.Clamp01((dying - 0.35f) / 0.28f);
            Body.localScale = Vector3.one * (1.22f * scale * (1f - shrink * shrink));
            jaw.localRotation = Quaternion.Euler(-100f, 0f, 0f);
            if (shrink >= 1f && Body.gameObject.activeSelf) Body.gameObject.SetActive(false);
        }
        private void Pop()
        {
            popped = true;
            Vector3 at = Body.position + Vector3.up * (1.4f * scale);
            BossRushFxBurst coins = BossRushFxKit.Sparks(Coin, 46);
            coins.Upward = true; coins.SpeedMin = 5f; coins.SpeedMax = 11f; coins.Gravity = 1.3f; coins.Drag = 0.5f;
            coins.LifeMin = 0.8f; coins.LifeMax = 1.5f; coins.SizeMin = 0.08f; coins.SizeMax = 0.15f; coins.Trail = 0.25f;
            Fx(at, coins);
            BossRushFxBurst planks = BossRushFxKit.Dust(Splinter, 18);
            planks.Shape = BossRushParticleShape.Shard; planks.Radial = false; planks.FlatOnGround = false;
            planks.SpeedMin = 4f; planks.SpeedMax = 9f; planks.Gravity = 1.5f; planks.Drag = 0.6f;
            planks.SizeMin = 0.25f; planks.SizeMax = 0.5f; planks.Spin = 420f; planks.GrowTo = 0.85f; planks.FadeIn = 0f;
            planks.LifeMin = 0.9f; planks.LifeMax = 1.4f;
            Fx(at, planks);
            Fx(at, BossRushFxKit.Glint(Coin, 2.6f * scale, 0.22f));
            Fx(Root.transform.position + Vector3.up * 0.07f, BossRushFxKit.Shockwave(new Color(1f, 0.72f, 0.3f, 0.75f), 1.4f * scale, 4f, 0.6f, true));
            BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.42f, 0.34f, 0.27f, 0.55f), 20);
            dust.ShapeRadius = 1.2f * scale; dust.SpeedMin = 3f; dust.SpeedMax = 6f; dust.FastGrow = true;
            Fx(Root.transform.position + Vector3.up * 0.1f, dust);
            FlashLight(at, Coin, 9f, 5.5f, 0.6f);
            ShakeNear(at, 0.4f, 24f);
        }
        private void Fx(Vector3 position, BossRushFxBurst spec)
        {
            try
            {
                ParticleSystem ps = BossRushFxKit.PlayBurst(position, spec);
                if (ps == null) return;
                // 爆发按自己的落点取样迷雾：墙后的落地 / 爆炸不随宝箱可见性一起露出来。
                SceneManager.MoveGameObjectToScene(ps.gameObject, Root.scene);
                ModEntityFogVisibility.Attach(ps.gameObject);
            }
            catch (Exception e) { ModBehaviour.DevLog("[GreedyMimic] 爆发表现失败: " + e.Message); }
        }
        private void FlashLight(Vector3 at, Color color, float range, float intensity, float fade)
        {
            GameObject go = new GameObject("GreedyMimic_Flash");
            SceneManager.MoveGameObjectToScene(go, Root.scene);
            go.transform.position = at;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = color; light.range = range; light.intensity = intensity; light.shadows = LightShadows.None;
            BossRushFxLightFade.Attach(light, intensity, 0f).FadeOut(fade, true);
            ModEntityFogVisibility.Attach(go);
        }
        private static void ShakeNear(Vector3 source, float strength, float radius)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;
                Vector3 delta = player.transform.position - source;
                float falloff = 1f - Mathf.Clamp01(delta.magnitude / radius);
                if (falloff <= 0.01f) return;
                delta.y = 0f;
                Vector3 direction = delta.sqrMagnitude > 0.01f ? delta.normalized : Vector3.forward;
                CameraShaker.Shake((direction + Vector3.up * 0.3f) * (strength * falloff), CameraShaker.CameraShakeTypes.explosion);
            }
            catch { /* 相机未就绪 */ }
        }
        private static float PopScale(float pop)
        {
            // 新长出的器官从 0.2 弹到 1，带一次回弹。
            if (pop <= 0f) return 1f;
            float t = 1f - pop;
            return 0.2f + 0.8f * (1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f));
        }
        private static Transform Group(string name, Transform parent, Vector3 position)
        { Transform t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        private Transform Shape(string name, PrimitiveType kind, Transform parent, Vector3 p, Vector3 size, Color color, bool additive = false)
        {
            GameObject go = GameObject.CreatePrimitive(kind); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = size;
            Collider collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            Paint(go.GetComponent<MeshRenderer>(), color, additive); return go.transform;
        }
        private MeshRenderer Disc(Transform parent, string name, Material material)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = discMesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; Quiet(renderer);
            return renderer;
        }
        private LineRenderer Bar(string name, Transform parent, Material material, Color color, float width)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = false; line.positionCount = 2;
            line.alignment = LineAlignment.TransformZ; line.widthMultiplier = width; line.numCapVertices = 2;
            line.startColor = line.endColor = color; Quiet(line);
            return line;
        }
        private static void SetBar(LineRenderer line, float fraction, float half)
        {
            if (line == null) return;
            float f = Mathf.Clamp01(fraction);
            line.enabled = f > 0.001f;
            line.SetPosition(0, new Vector3(-half, 0f, 0f));
            line.SetPosition(1, new Vector3(-half + 2f * half * f, 0f, 0f));
        }
        private void Paint(Renderer renderer, Color color, bool additive)
        {
            renderer.sharedMaterial = additive ? glow : solid;
            Tint(renderer, color);
            renderer.shadowCastingMode = additive ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = !additive;
        }
        /// <summary>只换颜色不换材质：预警圈、眼光逐帧调色走这里。</summary>
        private void Tint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            colors.Clear(); colors.SetColor("_BaseColor", color); colors.SetVector("_TintColor", new Vector4(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, color.a * 0.5f)); renderer.SetPropertyBlock(colors);
        }
        private static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        private static Gradient TrailGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.12f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }
        private void Tooth(Transform parent, Vector3 position, bool down)
        {
            Transform t = Group("IvoryFang", parent, position); t.localScale = new Vector3(0.28f, 0.55f, 0.32f); if (down) t.localRotation = Quaternion.Euler(180, 0, 0);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = toothMesh; Paint(t.gameObject.AddComponent<MeshRenderer>(), new Color(1f, 0.87f, 0.62f), false);
        }
        private static Mesh BuildTooth()
        {
            Mesh mesh = new Mesh { name = "GreedyMimic_Fang" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0, 1, 0.1f) };
            mesh.triangles = new[] { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); return mesh;
        }
        private static Mesh BuildRing()
        {
            const int count = 64; var vertices = new Vector3[count * 2]; var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count; Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)); vertices[i * 2] = direction; vertices[i * 2 + 1] = direction * 0.93f;
                int a = i * 2, b = ((i + 1) % count) * 2, t = i * 6; triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1; triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
            }
            Mesh mesh = new Mesh { name = "GreedyMimic_WarningRing", vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); return mesh;
        }
        private static Mesh BuildDisc()
        {
            const int count = 48; var vertices = new Vector3[count + 1]; var uv = new Vector2[count + 1]; var triangles = new int[count * 3];
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                uv[i + 1] = new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % count + 1; triangles[i * 3 + 2] = i + 1;
            }
            Mesh mesh = new Mesh { name = "GreedyMimic_WarningDisc", vertices = vertices, uv = uv, triangles = triangles }; mesh.RecalculateNormals(); return mesh;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (Root != null) { Root.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(Root); UnityEngine.Object.Destroy(Root); }
            if (ringMesh != null) UnityEngine.Object.Destroy(ringMesh); if (toothMesh != null) UnityEngine.Object.Destroy(toothMesh);
            if (discMesh != null) UnityEngine.Object.Destroy(discMesh);
        }
    }
}
