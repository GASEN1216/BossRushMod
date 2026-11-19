// ============================================================================
// CollectibleShowcaseFx.cs - 6–8 档收集品 3D 模型的陈列 / 地面特效（越高越华丽，8 档浮空自转）
// ============================================================================
// 需求（2026-11-19，主会话转达 owner）：高品质物品的 3D 模型要高端、大气、有史诗感，摆在官方陈列柜里、
// 丢在地上都要一眼显眼、看着很贵；超高品质的再缓慢浮空。范围先按收集品 6–8 档，和格子边框同一套档位
// （CollectibleSlotFrame.FrameTier）。
//
// 档次（Looks，下标 = 边框档）：
//   6 档：脚下赤金光晕（慢呼吸）、升腾金点、隔几秒一道从底升到顶的扫光环、一盏暖光；地上另有一道矮光柱；
//   7 档：再加一圈旋转符文环、身上不时一亮的星芒，光柱更高、灯更亮、扫光更勤；
//   8 档：再加反向旋转的外圈符文、绕身环绕的金色光点，陈列柜里也有矮光柱；模型缓慢浮起、上下漂浮、慢慢自转。
//   配色越高越金：6 档赤金、7 档琥珀金、8 档耀金（TierMain，守卫解析，任何一档不偏紫偏蓝）。
//   6 档不用格子边框那种纯官方红：地上一摊纯红光像敌人的危险预警，往金里调成赤金，仍比 7 档红。
//   陈列柜里（父链上没有 InteractablePickup）：光晕直径封顶 0.92 m（位距 1 m，相邻两件不互相压）、
//   灯 0.6 倍、光柱只有 8 档有且矮（柜子三层阶梯，高光柱会挡住上一层）。
//
// 接法：CollectibleShowcaseModels 建模型模板时给 Model（MeshRenderer 所在物体）挂 CollectibleShowcaseFxAnchor，
//   官方 CreateAGraphic 复制模板时它跟着复制到每个实例上。锚点没有 Update，只收进出镜头的消息：
//   实例第一次真的进了镜头（OnBecameVisible）才在 ItemGraphicInfo 根下建特效根（CollectibleShowcaseFx，
//   表现层独立类型，§4.15）；出了镜头由特效根自己的 Update 核对 Renderer.isVisible 后整棵停用（父物体停用 /
//   销毁途中发来的出镜头消息里不改激活状态，免得报错），再进镜头重新播放。地上 / 陈列只看父链上有没有
//   InteractablePickup（GetComponentInParent，只往上走一条链，不做场景查找）。
//   模型渲染器的本地包围盒按光晕、光柱与浮空高度放大：光柱先于模型进画面时也算「进了镜头」，不会在屏幕边缘突然冒出来。
//
// 材质：只用全 Mod 共享的程序化特效材质（BossRushFxMaterials / BossRushFxKit，URP Particles/Unlit 加色）；
//   本文件不写 Shader.Find、不查官方对象。URP 粒子着色器不可用时整套不画（owner：资源不对就硬失败，
//   不退成半透明的「看起来差不多」）。颜色走 MaterialPropertyBlock 的 _BaseColor（线性 HDR，吃官方 Bloom），
//   不改共享材质。光柱贴图与它的材质、平铺面片网格归本文件所有，模块销毁时销毁。
//
// 性能（AGENTS §4.12）：
//   - 不是 6–8 档、或模型没进镜头：一个对象都不建，没有每帧成本；锚点本身没有 Update。
//   - 进了镜头：每件 2–4 个粒子系统（活粒子合计不超过 80 颗）+ 2–4 张面片 + 1 盏不投影的点光；
//     每帧只改几个变换、属性块和灯的亮度。
//   - 出镜头：特效根整棵停用（粒子、面片、灯、Update 一起停）；被捡起（拾取物销毁 / 停用）、陈列位换物品
//     （官方销毁图形实例）、切图（场景销毁）时随父物体停用或销毁，锚点 OnDestroy 注销。
//   - 模块销毁：ResetStaticCaches 拆掉所有活着的特效、还原模型姿态与包围盒、销毁锚点与自有资源。
//   - 时间走 unscaled（陈列柜镜头、拍照模式下照样动），官方暂停菜单打开时粒子暂停、动画停推进。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    /// <summary>
    /// 挂在收集品模型模板的 Model 上，随官方 Instantiate 复制到每个实例。没有 Update：只收进出镜头的消息，
    /// 第一次进镜头时按需建特效，出镜头停用。
    /// </summary>
    internal sealed class CollectibleShowcaseFxAnchor : MonoBehaviour
    {
        private static readonly List<CollectibleShowcaseFxAnchor> live = new List<CollectibleShowcaseFxAnchor>();
        private static bool warnedNoShader;

        private CollectibleShowcaseFx fx;
        private Renderer modelRenderer;
        private Bounds meshBounds;
        /// <summary>-1 = 还没解析（官方 Setup 之前）；0 = 不需要特效；1–3 = 边框档。</summary>
        private int tier = -1;
        private bool ground, registered, boundsExpanded;

        /// <summary>建模型模板时调用：6–8 档才挂锚点。模板挂在停用的根下，锚点不会在模板上运行。</summary>
        internal static void AttachToTemplate(GameObject model, int quality)
        {
            if (model == null || CollectibleShowcaseFx.Tier(quality) <= 0) return;
            if (model.GetComponent<CollectibleShowcaseFxAnchor>() == null) model.AddComponent<CollectibleShowcaseFxAnchor>();
        }

        /// <summary>模块销毁时调用：拆掉所有活着的特效、还原模型，并销毁锚点（之后再进镜头也不会重建）。</summary>
        internal static void TeardownAll()
        {
            var anchors = new List<CollectibleShowcaseFxAnchor>(live);
            live.Clear();
            for (int i = 0; i < anchors.Count; i++)
            {
                try { if (anchors[i] != null) anchors[i].Teardown(true); }
                catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            }
            warnedNoShader = false;
        }

        private void Start()
        {
            if (Resolve()) SetVisible(modelRenderer.isVisible);
        }

        private void OnBecameVisible()
        {
            if (Resolve()) SetVisible(true);
        }

        // 出镜头不在这里停用特效根：这条消息也会在父物体停用 / 销毁的途中同步发来，那时改兄弟物体的激活状态会报错。
        // 特效根自己的 Update 每帧先看模型还在不在镜头里，不在就当帧停用（CollectibleShowcaseFx.Tick）。

        private void OnDisable()
        {
            // 拾取物被收起时特效根作为兄弟物体跟着停用；重新露面时粒子要从头播放。
            if (fx != null) fx.MarkRestart();
        }

        private void OnDestroy()
        {
            Teardown(false);
        }

        /// <summary>解析档次与所在位置（只做一次）。官方 Setup 之前拿不到物品，留到下一次消息再解析。</summary>
        private bool Resolve()
        {
            if (tier >= 0) return tier > 0;
            if (CollectiblesRuntimeModule.Current == null) return false;
            ItemGraphicInfo info = GetComponentInParent<ItemGraphicInfo>();
            Item item = info != null ? info.ItemRefrence : null;
            if (item == null) return false;
            tier = 0;
            CollectibleDefinition def = CollectibleCatalog.Get(item.TypeID);
            int resolved = def != null ? CollectibleShowcaseFx.Tier(def.Quality) : 0;
            MeshFilter filter = GetComponent<MeshFilter>();
            modelRenderer = GetComponent<Renderer>();
            if (resolved <= 0 || modelRenderer == null || filter == null || filter.sharedMesh == null) return false;
            if (!BossRushFxMaterials.SupportsAdditive)
            {
                if (!warnedNoShader)
                {
                    warnedNoShader = true;
                    Debug.LogWarning(CollectibleTuning.LogPrefix + "找不到 URP 粒子着色器，收集品陈列特效不绘制");
                }
                return false;
            }
            tier = resolved;
            ground = GetComponentInParent<InteractablePickup>() != null;
            meshBounds = filter.sharedMesh.bounds;
            live.Add(this);
            registered = true;
            ExpandVisibilityBounds();
            return true;
        }

        private void ExpandVisibilityBounds()
        {
            try
            {
                modelRenderer.localBounds = CollectibleShowcaseFx.VisibilityBounds(meshBounds, tier, ground);
                boundsExpanded = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 放大收集品模型包围盒失败: " + e.Message);
            }
        }

        private void SetVisible(bool visible)
        {
            if (!visible) return;
            if (fx == null)
            {
                if (CollectiblesRuntimeModule.Current == null) return;
                fx = CollectibleShowcaseFx.Create(transform, modelRenderer, tier, ground, meshBounds);
            }
            if (fx != null) fx.Show(true);
        }

        internal void Teardown(bool destroySelf)
        {
            if (registered)
            {
                registered = false;
                live.Remove(this);
            }
            if (fx != null)
            {
                fx.Dispose();
                fx = null;
            }
            if (!destroySelf) return;
            if (boundsExpanded && modelRenderer != null)
            {
                try { modelRenderer.ResetLocalBounds(); }
                catch (Exception) { /* 还原失败只影响剔除精度 */ }
            }
            boundsExpanded = false;
            tier = 0;
            Destroy(this);
        }
    }

    /// <summary>一件 6–8 档收集品模型的特效根，挂在 ItemGraphicInfo 根下（和 Model 同级）。只在可见时启用。</summary>
    internal sealed class CollectibleShowcaseFx : MonoBehaviour
    {
        private const float TwoPi = Mathf.PI * 2f;
        private const float MaxStep = 0.1f;
        /// <summary>光晕 / 符文离地高度：地上抬 3 cm 免得被地面起伏吃掉，陈列柜隔板平整只抬 1.5 cm。</summary>
        private const float GroundLift = 0.03f;
        private const float DisplayLift = 0.015f;
        private const float GroundHaloCap = 1.4f;
        private const float DisplayHaloCap = 0.92f;
        private const float DisplayLightScale = 0.6f;
        private const float DisplayRangeScale = 0.75f;
        private const float RuneInnerDegreesPerSecond = 24f;
        private const float RuneOuterDegreesPerSecond = 14f;
        private const float SheenRiseSeconds = 1.15f;
        private const float SheenGain = 1.7f;
        private const float RuneGain = 1.25f;
        private const float MoteGain = 2.4f;
        private const float GlintGain = 3.2f;
        private const float OrbitGain = 2.6f;
        /// <summary>光柱只发一颗、永不过期的粒子（约 27 小时），重新进镜头时重发。</summary>
        private const float BeamLifetime = 100000f;
        // 8 档浮空：慢慢升到 10 cm，上下 ±3.5 cm、一个来回 3.6 s，每秒自转 14°（约 26 s 一圈）。
        private const float FloatLift = 0.1f;
        private const float FloatRiseSeconds = 1.4f;
        private const float FloatBobAmplitude = 0.035f;
        private const float FloatBobPeriod = 3.6f;
        private const float FloatSpinDegreesPerSecond = 14f;

        private sealed class Look
        {
            internal float HaloScale, HaloMin, HaloMax, BreathHz;
            internal int Runes;
            internal float BeamGround, BeamDisplay, BeamGain;
            internal float MoteRate;
            internal int MoteMax;
            internal float GlintInterval, GlintChance, GlintSizeMin, GlintSizeMax;
            internal float OrbitRate;
            internal float SheenPeriod;
            internal float LightIntensity, LightRange;
            internal bool Float;
        }

        /// <summary>下标 = 边框档（1 = 6 档、2 = 7 档、3 = 8 档），和格子边框同一套档位，越高越华丽。</summary>
        private static readonly Look[] Looks =
        {
            null,
            new Look { HaloScale = 1.0f, HaloMin = 0.38f, HaloMax = 0.58f, BreathHz = 0.35f, Runes = 0,
                BeamGround = 1.3f, BeamDisplay = 0f, BeamGain = 0.5f, MoteRate = 5f, MoteMax = 14,
                GlintInterval = 0f, GlintChance = 0f, GlintSizeMin = 0f, GlintSizeMax = 0f, OrbitRate = 0f,
                SheenPeriod = 5.2f, LightIntensity = 0.7f, LightRange = 1.6f, Float = false },
            new Look { HaloScale = 1.1f, HaloMin = 0.42f, HaloMax = 0.66f, BreathHz = 0.45f, Runes = 1,
                BeamGround = 1.9f, BeamDisplay = 0f, BeamGain = 0.65f, MoteRate = 8f, MoteMax = 22,
                GlintInterval = 1.1f, GlintChance = 0.7f, GlintSizeMin = 0.13f, GlintSizeMax = 0.2f, OrbitRate = 0f,
                SheenPeriod = 4.2f, LightIntensity = 1.0f, LightRange = 1.9f, Float = false },
            new Look { HaloScale = 1.2f, HaloMin = 0.46f, HaloMax = 0.74f, BreathHz = 0.55f, Runes = 2,
                BeamGround = 2.6f, BeamDisplay = 0.85f, BeamGain = 0.85f, MoteRate = 12f, MoteMax = 30,
                GlintInterval = 0.65f, GlintChance = 0.85f, GlintSizeMin = 0.17f, GlintSizeMax = 0.27f, OrbitRate = 7f,
                SheenPeriod = 3.2f, LightIntensity = 1.4f, LightRange = 2.3f, Float = true },
        };

        /// <summary>
        /// 光晕、光柱、升腾金点的主色，下标 = 边框档。越高越金：6 档赤金、7 档琥珀金（同格子边框）、8 档耀金（同格子边框）。
        /// 写死，不在运行时查官方样式。
        /// </summary>
        private static readonly Color[] TierMain =
        {
            new Color(0f, 0f, 0f, 0f),
            new Color(1f, 0.42f, 0.2f, 1f),
            new Color(1f, 0.68f, 0.22f, 1f),
            new Color(1f, 0.86f, 0.36f, 1f),
        };

        /// <summary>灯色：暖琥珀、饱和度压到中等，不把地面和柜子刷成一片黄。</summary>
        private static readonly Color[] TierLight =
        {
            new Color(0f, 0f, 0f, 0f),
            new Color(1f, 0.66f, 0.44f, 1f),
            new Color(1f, 0.76f, 0.46f, 1f),
            new Color(1f, 0.84f, 0.54f, 1f),
        };

        private static readonly Color WhiteGold = new Color(1f, 0.97f, 0.85f, 1f);
        private static readonly Color RuneGold = new Color(1f, 0.84f, 0.42f, 1f);
        private static readonly Quaternion ZUp = Quaternion.Euler(-90f, 0f, 0f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static Mesh flatQuad;
        private static Texture2D beamTexture;
        private static Material beamMaterial;
        private static MaterialPropertyBlock block;

        private Transform model;
        private Renderer modelRenderer;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private int tier;
        private Look look;
        private Color main;
        private float height, radius, lift, haloDiameter, sheenDiameter, lightIntensity;
        private MeshRenderer halo, runeInner, runeOuter, sheen;
        private ParticleSystemRenderer beam;
        private Light glowLight;
        private readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        private float clock, phase, sheenOffset, liftLevel, yaw;
        private bool paused, sheenParked, restartPending = true;

        // ====================================================================
        // 对外入口
        // ====================================================================

        /// <summary>收集品品质 → 特效档：和格子边框同一套（6、7、8 档各一级，其余没有）。</summary>
        internal static int Tier(int quality)
        {
            return CollectibleSlotFrame.FrameTier(quality);
        }

        /// <summary>模型渲染器的本地包围盒放大到能罩住光晕、光柱与浮空高度（模型原点在底面中心）。</summary>
        internal static Bounds VisibilityBounds(Bounds mesh, int tier, bool ground)
        {
            Look l = Looks[Mathf.Clamp(tier, 1, Looks.Length - 1)];
            float r = ModelRadius(mesh), h = ModelHeight(mesh);
            float spread = Mathf.Max(HaloDiameter(r, l, ground), r * 2.4f) * 0.5f + 0.1f;
            float lifted = l.Float ? FloatLift + FloatBobAmplitude : 0f;
            float beamTop = ground ? l.BeamGround : l.BeamDisplay;
            float top = Mathf.Max(h + lifted + 0.15f, beamTop + 0.05f);
            // 浮空时模型自己往上走，特效留在原地：在模型的本地空间里，地面在 -lifted 处。
            float bottom = -lifted - 0.05f;
            return new Bounds(new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(spread * 2f, top - bottom, spread * 2f));
        }

        internal static CollectibleShowcaseFx Create(Transform model, Renderer modelRenderer, int tier, bool ground, Bounds mesh)
        {
            if (model == null || modelRenderer == null || tier <= 0 || tier >= Looks.Length) return null;
            Transform parent = model.parent != null ? model.parent : model;
            GameObject go = new GameObject("BossRush_CollectibleFx");
            go.SetActive(false);
            go.layer = model.gameObject.layer;
            go.transform.SetParent(parent, false);
            CollectibleShowcaseFx fx = go.AddComponent<CollectibleShowcaseFx>();
            try
            {
                fx.modelRenderer = modelRenderer;
                fx.Build(model, tier, ground, mesh);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收集品陈列特效构建失败: " + e.Message);
                fx.Dispose();
                return null;
            }
            return fx;
        }

        /// <summary>
        /// 进镜头时由锚点调（OnBecameVisible），出镜头时由本组件自己的 Update 调。
        /// 只在这两处改激活状态：父物体停用 / 销毁的途中不碰。重新启用时粒子从预热状态重新播放。
        /// </summary>
        internal void Show(bool on)
        {
            if (!on)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                restartPending = true;
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!restartPending) return;
            restartPending = false;
            paused = false;
            for (int i = 0; i < systems.Count; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
            Animate(0f);
        }

        /// <summary>父物体被停用过（拾取物收起）：下次露面时粒子从头播放。不改激活状态。</summary>
        internal void MarkRestart()
        {
            restartPending = true;
        }

        /// <summary>销毁特效根并把模型放回原位（浮空只动 Model 的本地变换）。</summary>
        internal void Dispose()
        {
            if (model != null)
            {
                model.localPosition = restPosition;
                model.localRotation = restRotation;
            }
            Destroy(gameObject);
        }

        /// <summary>模块销毁时调用：拆掉活着的特效，销毁本文件自有的网格、贴图与材质（共享材质归 BossRushFxMaterials）。</summary>
        internal static void ResetStaticCaches()
        {
            CollectibleShowcaseFxAnchor.TeardownAll();
            try
            {
                if (beamMaterial != null) Destroy(beamMaterial);
                if (beamTexture != null) Destroy(beamTexture);
                if (flatQuad != null) Destroy(flatQuad);
            }
            catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            beamMaterial = null;
            beamTexture = null;
            flatQuad = null;
            block = null;
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
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收集品陈列特效更新异常，已收起: " + e.Message);
                Show(false);
            }
        }

        private void Tick()
        {
            if (CollectiblesRuntimeModule.Current == null) { Show(false); return; }
            // 出了镜头（或模型没了）当帧整棵停用；再进镜头由锚点的 OnBecameVisible 重新启用。
            if (model == null || modelRenderer == null || !modelRenderer.isVisible) { Show(false); return; }
            if (BossRushUI.IsGamePaused())
            {
                if (!paused) SetPaused(true);
                return;
            }
            if (paused) SetPaused(false);
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            clock += dt;
            Animate(dt);
        }

        private void SetPaused(bool pause)
        {
            paused = pause;
            for (int i = 0; i < systems.Count; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;
                if (pause) ps.Pause(true);
                else ps.Play(true);
            }
        }

        private void Animate(float dt)
        {
            float breath = 0.5f + 0.5f * Mathf.Sin((clock + phase) * look.BreathHz * TwoPi);
            SetTint(halo, main, Mathf.Lerp(look.HaloMin, look.HaloMax, breath));
            if (runeInner != null)
            {
                runeInner.transform.Rotate(0f, RuneInnerDegreesPerSecond * dt, 0f, Space.Self);
                SetTint(runeInner, RuneGold, RuneGain * (0.8f + 0.2f * breath));
            }
            if (runeOuter != null)
            {
                runeOuter.transform.Rotate(0f, -RuneOuterDegreesPerSecond * dt, 0f, Space.Self);
                SetTint(runeOuter, RuneGold, RuneGain * (0.7f + 0.2f * breath));
            }
            if (beam != null) SetTint(beam, main, look.BeamGain * (0.85f + 0.15f * breath));
            float flash = AnimateSheen();
            if (glowLight != null) glowLight.intensity = lightIntensity * (0.88f + 0.12f * breath + 0.2f * flash);
            if (look.Float) AnimateFloat(dt);
        }

        /// <summary>扫光环：每个周期开头一道金白细环从脚下升到模型顶上，边升边收、头尾淡出。返回当前亮度（给灯一点闪）。</summary>
        private float AnimateSheen()
        {
            if (sheen == null) return 0f;
            float local = Mathf.Repeat(clock + sheenOffset, look.SheenPeriod);
            if (local >= SheenRiseSeconds)
            {
                if (!sheenParked)
                {
                    sheen.enabled = false;
                    sheenParked = true;
                }
                return 0f;
            }
            float p = local / SheenRiseSeconds;
            float e = BossRushUI.SmoothStep(p);
            float alpha = Mathf.Clamp01(p / 0.15f) * (1f - Mathf.Clamp01((p - 0.6f) / 0.4f));
            float d = Mathf.Lerp(sheenDiameter, sheenDiameter * 0.78f, e);
            Transform t = sheen.transform;
            t.localPosition = new Vector3(0f, Mathf.Lerp(Lift(), lift + height * 1.05f, e), 0f);
            t.localScale = new Vector3(d, 1f, d);
            if (sheenParked)
            {
                sheen.enabled = true;
                sheenParked = false;
            }
            SetTint(sheen, WhiteGold, SheenGain * alpha);
            return alpha;
        }

        /// <summary>8 档浮空：先慢慢升起，之后上下漂浮、缓慢自转。只动 Model 的本地变换，GroundPoint 与特效留在原地。</summary>
        private void AnimateFloat(float dt)
        {
            if (model == null) return;
            liftLevel = Mathf.MoveTowards(liftLevel, 1f, dt / FloatRiseSeconds);
            float rise = BossRushUI.SmoothStep(liftLevel) * lift;
            float bob = Mathf.Sin((clock + phase) * TwoPi / FloatBobPeriod) * FloatBobAmplitude * liftLevel;
            yaw = Mathf.Repeat(yaw + FloatSpinDegreesPerSecond * dt, 360f);
            model.localPosition = restPosition + new Vector3(0f, rise + bob, 0f);
            model.localRotation = restRotation * Quaternion.Euler(0f, yaw, 0f);
        }

        private static void SetTint(Renderer renderer, Color color, float intensity)
        {
            if (renderer == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            // 线性 HDR 原值：SetVector 不经 sRGB 换算，intensity > 1 时吃官方 Bloom。
            Color linear = color.linear;
            block.SetVector(BaseColorId, new Vector4(linear.r * intensity, linear.g * intensity, linear.b * intensity, 1f));
            renderer.SetPropertyBlock(block);
        }

        // ====================================================================
        // 构建
        // ====================================================================

        private void Build(Transform modelTransform, int tierIndex, bool ground, Bounds mesh)
        {
            model = modelTransform;
            restPosition = model.localPosition;
            restRotation = model.localRotation;
            tier = tierIndex;
            look = Looks[tier];
            main = TierMain[tier];
            height = ModelHeight(mesh);
            radius = ModelRadius(mesh);
            lift = look.Float ? FloatLift : 0f;
            // 陈列柜里任何一格变动，官方都会销毁重建全部展示位的模型（Showcase.RefreshAll）：柜子里直接浮在高处，
            // 免得每摆一件，柜里的 8 档全部落下再重新升起；地上掉落的才从地面慢慢浮起。
            liftLevel = ground ? 0f : 1f;
            phase = UnityEngine.Random.value * 10f;
            sheenOffset = UnityEngine.Random.value * look.SheenPeriod;
            float baseLift = ground ? GroundLift : DisplayLift;
            haloDiameter = HaloDiameter(radius, look, ground);
            sheenDiameter = Mathf.Max(radius * 2.5f, 0.3f);

            Material soft = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            halo = NewQuad("Halo", soft, haloDiameter, baseLift);
            if (look.Runes >= 1)
                runeInner = NewQuad("RuneInner", BossRushFxKit.GetShapeMaterial(BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, 1f),
                    haloDiameter * 0.72f, baseLift + 0.004f);
            if (look.Runes >= 2)
                runeOuter = NewQuad("RuneOuter", BossRushFxKit.GetShapeMaterial(BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, 1f),
                    haloDiameter * 0.98f, baseLift + 0.002f);
            sheen = NewQuad("SheenRing", BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Ring, BossRushFxBlend.Additive, 1f),
                sheenDiameter, baseLift);
            if (sheen != null)
            {
                sheen.enabled = false;
                sheenParked = true;
            }

            BuildMotes(baseLift);
            if (look.GlintInterval > 0f) BuildGlints();
            if (look.OrbitRate > 0f) BuildOrbit();
            float beamHeight = ground ? look.BeamGround : look.BeamDisplay;
            if (beamHeight > 0f) BuildBeam(beamHeight);
            BuildLight(ground);
        }

        private float Lift()
        {
            return halo != null ? halo.transform.localPosition.y : 0f;
        }

        private static float ModelHeight(Bounds mesh)
        {
            return Mathf.Clamp(mesh.max.y, 0.12f, 1.2f);
        }

        private static float ModelRadius(Bounds mesh)
        {
            return Mathf.Clamp(Mathf.Max(mesh.extents.x, mesh.extents.z), 0.08f, 0.6f);
        }

        private static float HaloDiameter(float r, Look l, bool ground)
        {
            return Mathf.Clamp(r * 3.8f * l.HaloScale, 0.5f, ground ? GroundHaloCap : DisplayHaloCap);
        }

        /// <summary>从光晕里升起的金点（世界空间：拾取物被甩出去时身后留一串）。</summary>
        private void BuildMotes(float baseLift)
        {
            ParticleSystem motes = NewEmitter("Motes",
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, MoteGain),
                look.MoteMax, new Vector3(0f, baseLift, 0f), true);
            if (motes == null) return;
            ShapeCircle(motes, radius * 0.85f, 0.45f);
            Velocity(motes, 0.16f, 0.3f, 0f);
            Noise(motes, 0.035f, 0.8f);
            Life(motes, 1.8f, 2.6f);
            Size(motes, 0.022f, 0.045f);
            StartColor(motes, WhiteGold, Color.Lerp(main, WhiteGold, 0.35f));
            Fade(motes, main, 0.12f, 0.65f);
            Twinkle(motes);
            Rate(motes, look.MoteRate);
            Prewarm(motes);
        }

        /// <summary>模型身上不时一亮的大星芒（7、8 档）。</summary>
        private void BuildGlints()
        {
            ParticleSystem glints = NewEmitter("Glints",
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Star, BossRushFxBlend.Additive, GlintGain),
                3, new Vector3(0f, lift + height * 0.55f, 0f), false);
            if (glints == null) return;
            ShapeSphere(glints, Mathf.Max(radius, height * 0.5f) * 0.8f);
            Life(glints, 0.22f, 0.34f);
            Size(glints, look.GlintSizeMin, look.GlintSizeMax);
            ParticleSystem.MainModule mainModule = glints.main;
            mainModule.startRotation = new ParticleSystem.MinMaxCurve(0f, TwoPi);
            StartColor(glints, Color.white, WhiteGold);
            Pop(glints);
            ParticleSystem.Burst burst = new ParticleSystem.Burst(0.05f, (short)1, (short)1, 0, look.GlintInterval);
            burst.probability = look.GlintChance;
            ParticleSystem.EmissionModule emission = glints.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { burst });
            mainModule.duration = look.GlintInterval * 3f;
        }

        /// <summary>绕身环绕、缓缓上升的金色光点（8 档）。</summary>
        private void BuildOrbit()
        {
            ParticleSystem orbit = NewEmitter("Orbit",
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, OrbitGain),
                24, new Vector3(0f, lift + height * 0.3f, 0f), false);
            if (orbit == null) return;
            ShapeCircle(orbit, radius * 1.15f, 0f);
            Velocity(orbit, 0.05f, 0.12f, 1.4f);
            Life(orbit, 2.2f, 3f);
            Size(orbit, 0.028f, 0.05f);
            StartColor(orbit, WhiteGold, Color.Lerp(main, WhiteGold, 0.25f));
            Fade(orbit, main, 0.15f, 0.7f);
            Rate(orbit, look.OrbitRate);
            Prewarm(orbit);
        }

        /// <summary>
        /// 光柱：一颗竖直公告板粒子（始终竖直、只绕竖轴转向镜头），贴图中间一道亮芯、两侧柔光，底部淡入、往上渐隐。
        /// 亮度随光晕一起呼吸（属性块）。
        /// </summary>
        private void BuildBeam(float beamHeight)
        {
            Material material = BeamMaterial();
            ParticleSystem ps = NewEmitter("Beam", material, 1, new Vector3(0f, beamHeight * 0.5f, 0f), false);
            if (ps == null) return;
            ps.transform.localRotation = Quaternion.identity;
            float width = Mathf.Clamp(radius * 1.5f, 0.3f, 0.6f) * (0.8f + 0.1f * tier);
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.loop = false;
            mainModule.duration = 1f;
            mainModule.startLifetime = BeamLifetime;
            mainModule.startSize3D = true;
            mainModule.startSizeX = width;
            mainModule.startSizeY = beamHeight;
            mainModule.startSizeZ = 1f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)1) });
            beam = ps.GetComponent<ParticleSystemRenderer>();
            if (beam != null) beam.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        }

        /// <summary>一盏不投影的暖光，挂在模型头顶：照亮模型顶面和脚下一圈，随光晕呼吸、扫光经过时微微一亮。</summary>
        private void BuildLight(bool ground)
        {
            GameObject go = new GameObject("Glow");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, lift + height + 0.22f, 0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = TierLight[tier];
            l.range = look.LightRange * (ground ? 1f : DisplayRangeScale);
            lightIntensity = look.LightIntensity * (ground ? 1f : DisplayLightScale);
            l.intensity = lightIntensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.bounceIntensity = 0f;
            glowLight = l;
        }

        // ====================================================================
        // 小工具
        // ====================================================================

        private MeshRenderer NewQuad(string name, Material material, float diameter, float y)
        {
            Mesh mesh = FlatQuad();
            if (material == null || mesh == null) return null;
            GameObject go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(diameter, 1f, diameter);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        /// <summary>常驻发射器：共享的安全默认值（BossRushFxKit.CreateEmitter），Z 朝上，unscaled 时间，跟随层级缩放。</summary>
        private ParticleSystem NewEmitter(string name, Material material, int maxParticles, Vector3 localPosition, bool worldSpace)
        {
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, transform, localPosition, material, maxParticles, worldSpace);
            if (ps == null) return null;
            ps.gameObject.layer = gameObject.layer;
            ps.transform.localRotation = ZUp;
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.useUnscaledTime = true;
            mainModule.scalingMode = ParticleSystemScalingMode.Hierarchy;
            mainModule.duration = 3f;
            systems.Add(ps);
            return ps;
        }

        private static void ShapeCircle(ParticleSystem ps, float radius, float thickness)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.001f, radius);
            shape.radiusThickness = Mathf.Clamp01(thickness);
            shape.arc = 360f;
        }

        private static void ShapeSphere(ParticleSystem ps, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.001f, radius);
            shape.radiusThickness = 0f;
        }

        /// <summary>
        /// 生命期速度（发射器本地空间，Z = 世界上方）。rise 是竖直速度区间，orbit 是绕竖轴的角速度（弧度/秒）。
        /// 三轴线速度与三轴轨道各自保持同一曲线模式，否则 Unity 报「curves must all be in the same mode」。
        /// </summary>
        private static void Velocity(ParticleSystem ps, float riseMin, float riseMax, float orbit)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(orbit);
            velocity.radial = new ParticleSystem.MinMaxCurve(0f);
        }

        private static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(strength);
            noise.frequency = frequency;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.25f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        private static void Life(ParticleSystem ps, float min, float max)
        {
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.startLifetime = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Size(ParticleSystem ps, float min, float max)
        {
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.startSize = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void StartColor(ParticleSystem ps, Color a, Color b)
        {
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.startColor = new ParticleSystem.MinMaxGradient(a, b);
        }

        private static void Rate(ParticleSystem ps, float perSecond)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, perSecond);
        }

        /// <summary>预热：一进镜头就是「已经亮了一阵」的样子，而不是从零长出来。</summary>
        private static void Prewarm(ParticleSystem ps)
        {
            ParticleSystem.MainModule mainModule = ps.main;
            mainModule.prewarm = true;
        }

        /// <summary>生命期颜色：出生偏白、往上走渐渐染成主色；淡入、保持、淡出。</summary>
        private static void Fade(ParticleSystem ps, Color tail, float fadeIn, float holdUntil)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.4f),
                    new GradientColorKey(tail, 1f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp(fadeIn, 0.01f, 0.5f)),
                    new GradientAlphaKey(1f, Mathf.Clamp(holdUntil, 0.5f, 0.98f)),
                    new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>闪烁：尺寸在生命期里两起两落，像金粉反光。</summary>
        private static void Twinkle(ParticleSystem ps)
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.15f, 1f),
                new Keyframe(0.4f, 0.45f),
                new Keyframe(0.62f, 0.95f),
                new Keyframe(1f, 0f));
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>一闪即逝：快速放大到峰值再收掉（星芒）。</summary>
        private static void Pop(ParticleSystem ps)
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0f));
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        // ---------------- 自有资源（模块销毁时 ResetStaticCaches 销毁） ----------------

        /// <summary>XZ 平面上的单位方片，法线朝上，白色顶点色（URP 粒子着色器乘顶点色）。</summary>
        private static Mesh FlatQuad()
        {
            if (flatQuad != null) return flatQuad;
            Mesh mesh = new Mesh();
            mesh.name = "BossRush_CollectibleFxQuad";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
            };
            mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Color32 white = new Color32(255, 255, 255, 255);
            mesh.colors32 = new Color32[] { white, white, white, white };
            mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // 俯视顺时针 = 正面朝上（共享粒子材质本身也不剔除背面）
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            flatQuad = mesh;
            return flatQuad;
        }

        /// <summary>光柱材质：复制共享加色材质，换上本文件的光柱贴图（共享材质本身不动）。</summary>
        private static Material BeamMaterial()
        {
            if (beamMaterial != null) return beamMaterial;
            Material template = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            Texture2D texture = BeamTexture();
            if (template == null || texture == null) return null;
            Material material = new Material(template);
            material.name = "BossRush_CollectibleFxBeam";
            material.hideFlags = HideFlags.HideAndDontSave;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            beamMaterial = material;
            return beamMaterial;
        }

        /// <summary>
        /// 32×128 光柱贴图（白色，形状只在 alpha 里）：横向是一道窄亮芯加一层宽柔光、两边收到 0；
        /// 纵向底部 7% 内淡入，往上按 (1 - v)^1.35 渐隐。
        /// </summary>
        private static Texture2D BeamTexture()
        {
            if (beamTexture != null) return beamTexture;
            const int width = 32, height = 128;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "BossRush_CollectibleFxBeam";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float vertical = BossRushUI.SmoothStep(Mathf.Clamp01(v / 0.07f)) * Mathf.Pow(1f - v, 1.35f);
                for (int x = 0; x < width; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    float core = Mathf.Exp(-(u / 0.16f) * (u / 0.16f));
                    float wide = Mathf.Exp(-(u / 0.5f) * (u / 0.5f));
                    float edge = 1f - BossRushUI.SmoothStep(Mathf.Clamp01((u - 0.8f) / 0.2f));
                    float a = Mathf.Clamp01((0.6f * core + 0.4f * wide) * edge * vertical);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            beamTexture = texture;
            return beamTexture;
        }
    }
}
