// ============================================================================
// PhantomWitchCurseSweatVfx.cs - 诅咒附体视觉（丧纱轮廓 + 魂焰 + 微符文）
// ============================================================================
// 模块说明：
//   当角色被施加幽灵诅咒 Buff 时，挂载到角色身上的轻量 MonoBehaviour。
//   从角色身体周围持续散发半透明幽紫气息，粒子反重力向上漂浮后消散。
//   Buff 消失后自动停止发射并在粒子播完后自毁。
//
//   覆盖路径：
//   1. Boss 技能 / 诅咒领域 → 显式 TryAttach（在 AddBuff 旁）
//   2. 玩家普攻 buffChance → Health.OnHurt 全局回调自动检测
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 诅咒附体视觉：目标轮廓外覆盖一层不属于自己的丧纱，
    /// 并伴随极少量魂焰与微型符文闪现。
    /// </summary>
    internal sealed class PhantomWitchCurseSweatVfx : MonoBehaviour
    {
        // ========== 粒子外观参数 ==========

        private static readonly Color DropletColorStart = new Color(
            PhantomWitchConfig.GhostBreathVeil.r,
            PhantomWitchConfig.GhostBreathVeil.g,
            PhantomWitchConfig.GhostBreathVeil.b,
            0.62f);
        private static readonly Color DropletColorEnd = new Color(
            PhantomWitchConfig.VioletVoidVeil.r,
            PhantomWitchConfig.VioletVoidVeil.g,
            PhantomWitchConfig.VioletVoidVeil.b,
            0.00f);

        private const float DropletSizeMin = 0.08f;
        private const float DropletSizeMax = 0.16f;
        private const float DropletLifetime = 1.2f;
        private const float DropletGravity = -0.15f; // 轻微反重力向上
        private const float BurstSpeedMin = 0.12f;
        private const float BurstSpeedMax = 0.28f;
        private const float EmissionRate = 8f;
        private const int MaxParticles = 40;

        /// <summary>
        /// 粒子发射源相对角色 pivot 的 Y 轴偏移（大约在身体中心）
        /// </summary>
        private const float EmitHeightOffset = 1.1f;

        /// <summary>
        /// 创建后的宽限期（秒），在此期间不检查 HasBuff，
        /// 避免引擎 buff 应用顺序导致的首帧误判。
        /// </summary>
        private const float BuffCheckGracePeriod = 0.6f;

        /// <summary>
        /// HasBuff 检查频率（秒），避免每帧反射调用。
        /// </summary>
        private const float BuffCheckInterval = 0.25f;
        private const float RuneFlashDuration = 0.35f;
        // 轮廓光改走加色后按层压 alpha（审美口径第 6 条：大面积光晕压 alpha）；此前半透明混合下 0.42–0.76 是一层紫膜。
        private const float Layer1HaloAlpha = 0.22f;
        private const float Layer2HaloAlpha = 0.30f;
        private const float Layer3HaloAlpha = 0.40f;

        // ========== 全局 Hook 状态 ==========

        private static bool hookRegistered = false;

        /// <summary>丧纱轮廓贴图全场共用一张（会话常驻）：它是共享材质工厂的缓存键，不随单个角色的挂件销毁。</summary>
        private static Texture2D sharedHaloTexture;

        // ========== 运行时状态 ==========

        private ParticleSystem particleSys;
        private float createTime;
        private float lastBuffCheckTime;
        private bool stopping;
        private CharacterMainControl cachedCharacter;
        private GameObject haloObject;
        private MeshRenderer haloRenderer;
        private Transform haloTransform;
        private MaterialPropertyBlock haloBlock;
        private Material haloMaterial;
        private Texture2D haloTexture;
        private Material runeLineMaterial;
        private float nextRuneFlashTime;
        private int currentCurseLayers = 1;
        private readonly List<GameObject> transientRuneObjects = new List<GameObject>(4);

        // ========== 全局 Hook ==========

        /// <summary>
        /// 注册 Health.OnHurt 全局回调。应在噬魂挽歌初始化时调用一次。
        /// 幂等：多次调用不会重复注册。
        /// </summary>
        public static void RegisterGlobalHook()
        {
            if (hookRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt += OnGlobalHurt;
                hookRegistered = true;
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] Health.OnHurt 全局回调已注册");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 注册 Health.OnHurt 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注销全局回调（场景清理时调用）。
        /// </summary>
        public static void UnregisterGlobalHook()
        {
            if (!hookRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt -= OnGlobalHurt;
            }
            catch
            {
            }
            hookRegistered = false;
        }

        private static void OnGlobalHurt(Health hurtHealth, DamageInfo damageInfo)
        {
            if (hurtHealth == null)
            {
                return;
            }

            try
            {
                CharacterMainControl character = TryGetTargetCharacter(hurtHealth);
                CharacterBuffManager buffMgr = TryGetBuffManager(character);
                bool hasCurse = buffMgr != null && buffMgr.HasBuff(PhantomWitchConfig.CurseBuffID);
                // Health.Hurt 已完成唯一一次 buffChance 判定；这里只观察结果，不重新掷骰。
                if (hasCurse && character != null)
                {
                    TryAttach(character.gameObject);
                }
            }
            catch
            {
            }
        }

        private static CharacterMainControl TryGetTargetCharacter(Health hurtHealth)
        {
            if (hurtHealth == null)
            {
                return null;
            }

            try
            {
                return hurtHealth.TryGetCharacter();
            }
            catch
            {
                return null;
            }
        }

        private static CharacterMainControl ResolveCharacter(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            CharacterMainControl character = target.GetComponent<CharacterMainControl>();
            if (character == null)
            {
                character = target.GetComponentInParent<CharacterMainControl>();
            }
            if (character == null)
            {
                character = target.GetComponentInChildren<CharacterMainControl>();
            }

            return character;
        }

        private static GameObject ResolveAttachTarget(GameObject target)
        {
            CharacterMainControl character = ResolveCharacter(target);
            return character != null ? character.gameObject : target;
        }

        private static CharacterBuffManager TryGetBuffManager(CharacterMainControl character)
        {
            if (character == null)
            {
                return null;
            }

            try
            {
                return character.GetBuffManager();
            }
            catch
            {
                return null;
            }
        }

        private static bool HasCurse(CharacterMainControl character)
        {
            CharacterBuffManager buffMgr = TryGetBuffManager(character);
            if (buffMgr == null)
            {
                return false;
            }

            try
            {
                return buffMgr.HasBuff(PhantomWitchConfig.CurseBuffID);
            }
            catch
            {
                return false;
            }
        }

        // ========== 公开接口 ==========

        /// <summary>
        /// 在受击目标身上挂载/续期流汗特效。已存在则仅续期，不会重复创建。
        /// </summary>
        public static void TryAttach(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            GameObject attachTarget = ResolveAttachTarget(target);

            PhantomWitchCurseSweatVfx existing = attachTarget.GetComponent<PhantomWitchCurseSweatVfx>();
            if (existing != null)
            {
                existing.Refresh();
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 续期 target=" + attachTarget.name);
                return;
            }

            try
            {
                PhantomWitchCurseSweatVfx vfx = attachTarget.AddComponent<PhantomWitchCurseSweatVfx>();
                vfx.Refresh();
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 新挂载 target=" + attachTarget.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 挂载失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 续期：重置计时，恢复发射。
        /// </summary>
        public void Refresh()
        {
            createTime = Time.time;
            lastBuffCheckTime = Time.time;

            if (cachedCharacter == null)
            {
                cachedCharacter = ResolveCharacter(gameObject);
            }

            if (particleSys == null)
            {
                CreateParticleSystem();
            }

            if (haloRenderer == null)
            {
                CreateHalo();
            }

            if (stopping)
            {
                stopping = false;
            }

            if (particleSys != null)
            {
                var emission = particleSys.emission;
                emission.rateOverTime = EmissionRate;
                if (!particleSys.isPlaying)
                {
                    particleSys.Play();
                }
            }

            currentCurseLayers = Mathf.Max(1, GetCurseLayers());
            UpdateParticleStrength(currentCurseLayers);
            UpdateHaloVisual(ResolveHaloAlphaForLayers(currentCurseLayers));
            ScheduleRuneFlash();
        }

        // ========== 生命周期 ==========

        private void Awake()
        {
            createTime = Time.time;
            lastBuffCheckTime = Time.time;

            cachedCharacter = ResolveCharacter(gameObject);

            CreateParticleSystem();
            CreateHalo();
            ScheduleRuneFlash();
        }

        private void Update()
        {
            if (stopping)
            {
                UpdateHaloVisual(0f);
                // 等所有粒子播完后自毁
                if (particleSys == null || !particleSys.IsAlive(false))
                {
                    DestroySelf();
                }
                return;
            }

            if (cachedCharacter == null)
            {
                cachedCharacter = ResolveCharacter(gameObject);
            }

            currentCurseLayers = Mathf.Max(1, GetCurseLayers());
            UpdateHaloVisual(ResolveHaloAlphaForLayers(currentCurseLayers));
            UpdateParticleStrength(currentCurseLayers);
            UpdateRuneFlash();

            // 宽限期内不检查，避免引擎 buff 还没来得及挂上
            if (Time.time - createTime < BuffCheckGracePeriod)
            {
                return;
            }

            // 按间隔检测角色是否仍持有诅咒 Buff
            if (Time.time - lastBuffCheckTime >= BuffCheckInterval)
            {
                lastBuffCheckTime = Time.time;

                if (!CharacterStillCursed())
                {
                    BeginStop();
                }
            }
        }

        private void OnDestroy()
        {
            CleanupTransientRunes();

            if (particleSys != null)
            {
                try
                {
                    Destroy(particleSys.gameObject);
                }
                catch
                {
                }
                particleSys = null;
            }

            if (haloObject != null)
            {
                try
                {
                    Destroy(haloObject);
                }
                catch
                {
                }
                haloObject = null;
                haloRenderer = null;
                haloTransform = null;
            }

            // 轮廓材质归 BossRushFxMaterials（全 Mod 共享），贴图是全场共用的一张：只放手，不销毁。
            haloMaterial = null;
            haloTexture = null;

            if (runeLineMaterial != null)
            {
                Destroy(runeLineMaterial);
                runeLineMaterial = null;
            }
        }

        // ========== Buff 状态检测 ==========

        private bool CharacterStillCursed()
        {
            if (cachedCharacter == null)
            {
                cachedCharacter = ResolveCharacter(gameObject);
            }

            return HasCurse(cachedCharacter);
        }

        private int GetCurseLayers()
        {
            if (cachedCharacter == null)
            {
                cachedCharacter = ResolveCharacter(gameObject);
            }

            CharacterBuffManager buffMgr = TryGetBuffManager(cachedCharacter);
            if (buffMgr == null || buffMgr.Buffs == null)
            {
                return 0;
            }

            try
            {
                var buffs = buffMgr.Buffs;
                for (int i = 0; i < buffs.Count; i++)
                {
                    Buff buff = buffs[i];
                    if (buff != null && buff.ID == PhantomWitchConfig.CurseBuffID)
                    {
                        return buff.CurrentLayers;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        // ========== 粒子系统 ==========

        private void CreateParticleSystem()
        {
            try
            {
                GameObject psGo = new GameObject("PW_CurseSweat_PS");
                psGo.transform.SetParent(transform, false);
                psGo.transform.localPosition = new Vector3(0f, EmitHeightOffset, 0f);

                particleSys = psGo.AddComponent<ParticleSystem>();

                // ---------- Main ----------
                var main = particleSys.main;
                main.duration = 30f;
                main.loop = true;
                main.startLifetime = DropletLifetime;
                main.startSpeed = new ParticleSystem.MinMaxCurve(BurstSpeedMin, BurstSpeedMax);
                main.startSize = new ParticleSystem.MinMaxCurve(DropletSizeMin, DropletSizeMax);
                main.startColor = DropletColorStart;
                main.gravityModifier = DropletGravity;
                main.maxParticles = MaxParticles;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                // ---------- Emission ----------
                var emission = particleSys.emission;
                emission.rateOverTime = EmissionRate;

                // ---------- Shape：从身体周围散发 ----------
                var shape = particleSys.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.34f;

                // ---------- Color over Lifetime：从半透明蓝→透明 ----------
                var colorOverLifetime = particleSys.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(DropletColorStart, 0f),
                        new GradientColorKey(PhantomWitchConfig.VioletVoidVeil, 0.5f),
                        new GradientColorKey(new Color(DropletColorEnd.r, DropletColorEnd.g, DropletColorEnd.b), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.1f, 0f),
                        new GradientAlphaKey(DropletColorStart.a, 0.12f),
                        new GradientAlphaKey(DropletColorStart.a * 0.8f, 0.55f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                // ---------- Size over Lifetime：先膨胀后收缩（水珠拉长感） ----------
                var sizeOverLifetime = particleSys.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                AnimationCurve sizeCurve = new AnimationCurve(
                    new Keyframe(0f, 0.3f),
                    new Keyframe(0.15f, 1f),
                    new Keyframe(0.6f, 0.7f),
                    new Keyframe(1f, 0.1f));
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

                // ---------- Renderer（复用共享粒子材质）：丧纱气息是半透明的纱，不发光 ----------
                PhantomWitchAssetManager.ConfigureSharedParticleRenderer(particleSys, BossRushFxBlend.Alpha);

                particleSys.Play();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 创建粒子系统失败: " + e.Message);
            }
        }

        private void CreateHalo()
        {
            try
            {
                GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                halo.name = "PW_CurseHalo";
                halo.transform.SetParent(transform, false);
                halo.transform.localPosition = new Vector3(0f, EmitHeightOffset, 0f);
                halo.transform.localScale = new Vector3(1.45f, 1.75f, 1f);
                haloObject = halo;
                haloTransform = halo.transform;

                Collider collider = halo.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                haloRenderer = halo.GetComponent<MeshRenderer>();
                // 审查 VB-02：此前每挂一次就 new 一份材质 + 贴图，Shader.Find 首选项在游戏里又不存在（落到 Sprites/Default）。
                // 现在共享加色材质 + 全场共用的轮廓贴图，颜色走材质属性块。
                haloTexture = GetSharedHaloTexture();
                haloMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Additive, haloTexture);
                if (haloMaterial != null)
                {
                    haloRenderer.sharedMaterial = haloMaterial;
                }
                haloBlock = new MaterialPropertyBlock();
                halo.AddComponent<PhantomWitchBillboard>();
                UpdateHaloVisual(Layer1HaloAlpha);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SweatVfx] 创建轮廓光失败: " + e.Message);
            }
        }

        private static Texture2D GetSharedHaloTexture()
        {
            if (sharedHaloTexture == null)
            {
                sharedHaloTexture = CreateHaloTexture();
            }

            return sharedHaloTexture;
        }

        private static Texture2D CreateHaloTexture()
        {
            Texture2D texture = new Texture2D(32, 64, TextureFormat.RGBA32, false);
            texture.name = "PW_CurseHalo";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            float centerX = 15.5f;
            float centerY = 31.5f;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    float nx = (x - centerX) / centerX;
                    float ny = (y - centerY) / centerY;
                    float distance = Mathf.Sqrt(nx * nx + ny * ny * 0.45f);
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha * (3f - 2f * alpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private void UpdateHaloVisual(float alpha)
        {
            if (haloRenderer == null)
            {
                return;
            }

            Color color = currentCurseLayers >= 3
                ? new Color(
                    Mathf.Lerp(PhantomWitchConfig.VioletVoidVeil.r, PhantomWitchConfig.BloodRoseVeil.r, 0.35f),
                    Mathf.Lerp(PhantomWitchConfig.VioletVoidVeil.g, PhantomWitchConfig.BloodRoseVeil.g, 0.35f),
                    Mathf.Lerp(PhantomWitchConfig.VioletVoidVeil.b, PhantomWitchConfig.BloodRoseVeil.b, 0.35f),
                    alpha)
                : new Color(
                    PhantomWitchConfig.VioletVoidVeil.r,
                    PhantomWitchConfig.VioletVoidVeil.g,
                    PhantomWitchConfig.VioletVoidVeil.b,
                    alpha);

            PhantomWitchFxRenderUtil.SetRendererColor(haloRenderer, haloBlock, color);
        }

        private float ResolveHaloAlphaForLayers(int layers)
        {
            if (layers >= 3)
            {
                return Layer3HaloAlpha;
            }

            if (layers >= 2)
            {
                return Layer2HaloAlpha;
            }

            return Layer1HaloAlpha;
        }

        private void UpdateParticleStrength(int layers)
        {
            if (particleSys == null)
            {
                return;
            }

            var emission = particleSys.emission;
            if (layers >= 3)
            {
                emission.rateOverTime = EmissionRate + 3f;
            }
            else if (layers >= 2)
            {
                emission.rateOverTime = EmissionRate + 1.5f;
            }
            else
            {
                emission.rateOverTime = EmissionRate;
            }
        }

        private void UpdateRuneFlash()
        {
            if (currentCurseLayers < 2 || Time.time < nextRuneFlashTime)
            {
                return;
            }

            SpawnRuneFlash();
            ScheduleRuneFlash();
        }

        private void ScheduleRuneFlash()
        {
            if (currentCurseLayers >= 3)
            {
                nextRuneFlashTime = Time.time + UnityEngine.Random.Range(2f, 3f);
            }
            else
            {
                nextRuneFlashTime = Time.time + UnityEngine.Random.Range(4f, 6f);
            }
        }

        private void SpawnRuneFlash()
        {
            try
            {
                PruneDestroyedTransientRunes();

                GameObject rune = new GameObject("PW_CurseMiniRune");
                rune.transform.SetParent(transform, false);
                rune.transform.localPosition = new Vector3(
                    UnityEngine.Random.Range(-0.22f, 0.22f),
                    EmitHeightOffset + UnityEngine.Random.Range(-0.15f, 0.20f),
                    UnityEngine.Random.Range(-0.22f, 0.22f));
                rune.transform.localRotation = Quaternion.Euler(
                    UnityEngine.Random.Range(-25f, 25f),
                    UnityEngine.Random.Range(0f, 360f),
                    UnityEngine.Random.Range(-20f, 20f));
                transientRuneObjects.Add(rune);

                for (int i = 0; i < 2; i++)
                {
                    GameObject lineGo = new GameObject("Line_" + i);
                    lineGo.transform.SetParent(rune.transform, false);
                    LineRenderer line = lineGo.AddComponent<LineRenderer>();
                    line.useWorldSpace = false;
                    line.loop = false;
                    line.positionCount = 3;
                    // 审查 VB-08：0.014 m 的线在相机距离下不到 1 px。
                    line.widthMultiplier = 0.045f;
                    line.widthCurve = PhantomWitchVfxRedesign.TaperedLineWidthCurve;
                    line.sharedMaterial = GetRuneLineMaterial();
                    Color color = new Color(
                        PhantomWitchConfig.SilverAshCore.r,
                        PhantomWitchConfig.SilverAshCore.g,
                        PhantomWitchConfig.SilverAshCore.b,
                        0.45f);
                    line.startColor = color;
                    line.endColor = new Color(color.r, color.g, color.b, 0.1f);

                    float width = UnityEngine.Random.Range(0.06f, 0.12f);
                    line.SetPosition(0, new Vector3(-width, 0f, 0f));
                    line.SetPosition(1, new Vector3(0f, UnityEngine.Random.Range(0.02f, 0.06f), 0f));
                    line.SetPosition(2, new Vector3(width * UnityEngine.Random.Range(0.5f, 1f), UnityEngine.Random.Range(-0.02f, 0.03f), 0f));
                }

                PhantomWitchFadeDestroy fade = rune.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(RuneFlashDuration, RuneFlashDuration);
                Destroy(rune, RuneFlashDuration);
            }
            catch
            {
            }
        }

        private Material GetRuneLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private void CleanupTransientRunes()
        {
            for (int i = 0; i < transientRuneObjects.Count; i++)
            {
                GameObject rune = transientRuneObjects[i];
                if (rune != null)
                {
                    try
                    {
                        Destroy(rune);
                    }
                    catch
                    {
                    }
                }
            }

            transientRuneObjects.Clear();
        }

        private void PruneDestroyedTransientRunes()
        {
            for (int i = transientRuneObjects.Count - 1; i >= 0; i--)
            {
                if (transientRuneObjects[i] == null)
                {
                    transientRuneObjects.RemoveAt(i);
                }
            }
        }

        private void BeginStop()
        {
            stopping = true;

            if (particleSys != null)
            {
                var emission = particleSys.emission;
                emission.rateOverTime = 0f;
                particleSys.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void DestroySelf()
        {
            try
            {
                Destroy(this);
            }
            catch
            {
            }
        }
    }
}
