// ============================================================================
// RingParticleEffect.cs - 通用环形粒子特效基类
// ============================================================================
// 模块说明：
//   提供可复用的环形粒子特效系统
//   支持Local和World双层粒子系统
//   可通过子类配置参数实现不同的视觉效果
//
// 2026-09-23 审美修（VA-01 / VB-27）：
//   - 旧版 LateUpdate 每帧给每个发射器再手撒一颗，密度跟帧率走（60 fps 每秒约 65 颗、144 fps 约 150 颗），
//     出生还一次撒 10 颗；现在发射只走 rateOverTime，没有每帧工作（Update 只剩跟随）。
//   - 共享材质是 Legacy Particles/Alpha Blended，片元 2 × 顶点色 × _TintColor × 贴图；旧代码把 _TintColor
//     设成白，颜色与 alpha 都翻倍（灰烟变成发光白团）。现在写中性 0.5（SetVector 原始值），1×。
//     各常量的注释值因此就是实际不透明度；默认值按「旧的实际生效密度」重新折算过（飞行云雾走默认值）。
//   - 尺寸有随机范围（*SizeMin），子类可换渲染方式、贴图材质、径向速度、寿命末端色，并在建好后加层。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;

namespace BossRush.Common.Effects
{
    /// <summary>
    /// 通用环形粒子特效基类
    /// 创建多个环形分布的粒子发射器，支持Local和World双层系统
    /// [性能优化] 使用静态缓存的材质和纹理，避免重复创建
    /// </summary>
    public abstract class RingParticleEffect : MonoBehaviour
    {
        // ========== 静态缓存（性能优化：所有实例共享材质和纹理） ==========

        /// <summary>
        /// 缓存的粒子材质（所有实例共享）
        /// </summary>
        private static Material cachedParticleMaterial = null;

        /// <summary>
        /// 材质缓存锁（线程安全）
        /// </summary>
        private static readonly object materialLock = new object();

        /// <summary>退场时最多等多久（最后一颗粒子死完或超时就销毁）。</summary>
        private const float MaxStopWaitSeconds = 3f;

        // ========== 可配置参数（子类通过属性覆盖） ==========

        /// <summary>
        /// 环形发射器数量
        /// </summary>
        protected virtual int EmitterCount => 6;

        /// <summary>
        /// 发射器环形半径（米）
        /// </summary>
        protected virtual float EmitterRadius => 0.6f;

        /// <summary>
        /// 发射器位置随机偏移（米）
        /// </summary>
        protected virtual float EmitterRandomOffset => 0.15f;

        /// <summary>
        /// 发射器高度随机偏移（米，±）。贴地的层（霜雾）要调小，否则一半发射器埋进地里。
        /// </summary>
        protected virtual float EmitterHeightJitter => 0.1f;

        /// <summary>
        /// 跟随偏移（相对于目标的位置）
        /// </summary>
        protected virtual Vector3 FollowOffset => new Vector3(0f, -0.3f, 0f);

        /// <summary>
        /// 是否启用Local空间粒子系统
        /// </summary>
        protected virtual bool EnableLocalEmitters => true;

        /// <summary>
        /// 是否启用World空间粒子系统
        /// </summary>
        protected virtual bool EnableWorldEmitters => true;

        // ========== Local空间粒子参数 ==========
        // 默认值（飞行云雾走的就是这一套）按旧版的实际生效值折算：旧版 60 fps 下每个发射器每秒
        // 约 75 颗（每帧 1 颗 + 速率 15）、有效 alpha 0.98（着色器 ×2）；现在速率 40 / 寿命 0.35、
        // 有效 alpha 0.55，同样是一团云，但不再是不透明的硬饼，也不随帧率变浓。

        protected virtual int LocalMaxParticles => 100;
        protected virtual float LocalLifetime => 0.35f;
        protected virtual float LocalSpeed => 0.3f;
        protected virtual float LocalSize => 1.2f;
        /// <summary>出生尺寸下限（上限是 LocalSize），给一团里的粒子大小一点差异。</summary>
        protected virtual float LocalSizeMin => LocalSize * 0.65f;
        // alpha 只施加一次（见 ConfigureParticleSystem 的梯度注释），线性响应：写多少就是多少不透明度。
        protected virtual float LocalAlpha => 0.55f;
        protected virtual float LocalEmissionRate => 40f;
        protected virtual float LocalShapeRadius => 0.1f;

        // ========== World空间粒子参数 ==========

        protected virtual int WorldMaxParticles => 100;
        protected virtual float WorldLifetime => 0.3f;
        protected virtual float WorldSpeed => 0.3f;
        protected virtual float WorldSize => 1.2f;
        protected virtual float WorldSizeMin => WorldSize * 0.65f;
        // 同上：旧版实际生效的是 0.1225 × 2 ≈ 0.245。
        protected virtual float WorldAlpha => 0.2f;
        protected virtual float WorldEmissionRate => 30f;
        protected virtual float WorldShapeRadius => 0.05f;

        /// <summary>
        /// 粒子着色（RGB；alpha 仍由 LocalAlpha / WorldAlpha 控制）。默认白色，与历史行为一致。
        /// 材质与纹理仍是全局共享的白色贴图，着色只走 startColor / colorOverLifetime，不产生新材质。
        /// </summary>
        protected virtual Color ParticleTint => Color.white;

        /// <summary>
        /// 寿命末端的颜色乘子（与 startColor 相乘；白色 = 不变色）。例：霜雾从浅冰白过渡到偏深的冰蓝。
        /// </summary>
        protected virtual Color LifetimeEndColor => Color.white;

        /// <summary>生命期尺寸曲线（乘在出生尺寸上）。</summary>
        protected virtual AnimationCurve SizeOverLifetimeCurve => AnimationCurve.EaseInOut(0f, 0.8f, 1f, 1.3f);

        /// <summary>粒子公告板方式。贴地的雾 / 霜用 HorizontalBillboard。</summary>
        protected virtual ParticleSystemRenderMode ParticleRenderMode => ParticleSystemRenderMode.Billboard;

        /// <summary>径向速度（米/秒，&gt;0 向外飘散）。</summary>
        protected virtual float RadialSpeed => 0f;

        /// <summary>出生时随机旋转（烟缕类贴图不再朝向一致）。</summary>
        protected virtual bool RandomStartRotation => false;

        // ========== 内部状态 ==========

        private ParticleSystem[] emittersLocal;
        private ParticleSystem[] emittersWorld;
        private bool isStopping = false;
        private Coroutine stopCoroutine;
        private Transform followTarget;
        private Transform cachedTransform;

        // ========== 静态工厂方法（推荐使用） ==========

        /// <summary>
        /// 创建特效实例（推荐使用此方法）
        /// </summary>
        /// <typeparam name="T">特效类型</typeparam>
        /// <param name="target">跟随目标</param>
        /// <param name="initialPosition">初始位置（可选，默认使用目标位置）</param>
        /// <returns>特效实例</returns>
        public static T Create<T>(Transform target, Vector3? initialPosition = null) where T : RingParticleEffect
        {
            GameObject effectObj = new GameObject(typeof(T).Name);
            effectObj.transform.position = initialPosition ?? target.position;
            effectObj.SetActive(true);

            T effect = effectObj.AddComponent<T>();
            effect.SetFollowTarget(target);

            return effect;
        }

        // ========== 公开方法 ==========

        /// <summary>
        /// 设置跟随目标
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;
        }

        /// <summary>
        /// 停止特效（停发射，等已发出的粒子自然走完寿命再销毁）
        /// </summary>
        public void StopEffect()
        {
            if (isStopping) return;

            isStopping = true;

            if (stopCoroutine != null)
            {
                StopCoroutine(stopCoroutine);
            }

            stopCoroutine = StartCoroutine(DoStop());
        }

        // ========== Unity生命周期 ==========

        private void Awake()
        {
            cachedTransform = transform;
        }

        private void Start()
        {
            CreateParticleSystems();
        }

        private void Update()
        {
            // 跟随目标位置
            if (followTarget != null)
            {
                Transform effectTransform = cachedTransform;
                if (effectTransform == null)
                {
                    effectTransform = transform;
                    cachedTransform = effectTransform;
                }

                effectTransform.position = followTarget.position + FollowOffset;
            }
        }

        private void OnDestroy()
        {
            CleanupMaterials();
        }

        // ========== 粒子系统创建 ==========

        private void CreateParticleSystems()
        {
            int localCount = EnableLocalEmitters ? EmitterCount : 0;
            int worldCount = EnableWorldEmitters ? EmitterCount : 0;

            if (localCount > 0)
            {
                emittersLocal = new ParticleSystem[localCount];
            }

            if (worldCount > 0)
            {
                emittersWorld = new ParticleSystem[worldCount];
            }

            for (int i = 0; i < EmitterCount; i++)
            {
                // 计算环形位置
                Vector3 position = CalculateEmitterPosition(i);

                // 创建Local空间发射器
                if (EnableLocalEmitters)
                {
                    emittersLocal[i] = CreateEmitter($"Emitter_Local_{i}", position, ParticleSystemSimulationSpace.Local, true);
                }

                // 创建World空间发射器
                if (EnableWorldEmitters)
                {
                    emittersWorld[i] = CreateEmitter($"Emitter_World_{i}", position, ParticleSystemSimulationSpace.World, false);
                }
            }

            try
            {
                OnEmittersCreated(cachedTransform != null ? cachedTransform : transform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RingParticleEffect] 附加层创建失败: " + e.Message);
            }
        }

        /// <summary>环形发射器建好之后的挂点：子类在这里加点缀层（挂在 root 下，随本特效一起停、一起销毁）。</summary>
        protected virtual void OnEmittersCreated(Transform root)
        {
        }

        /// <summary>
        /// 计算发射器的环形位置
        /// </summary>
        private Vector3 CalculateEmitterPosition(int index)
        {
            float angle = (360f / EmitterCount) * index;
            float angleRad = angle * Mathf.Deg2Rad;

            Vector3 basePos = new Vector3(
                Mathf.Cos(angleRad) * EmitterRadius,
                0f,
                Mathf.Sin(angleRad) * EmitterRadius
            );

            Vector3 randomOffset = new Vector3(
                UnityEngine.Random.Range(-EmitterRandomOffset, EmitterRandomOffset),
                UnityEngine.Random.Range(-EmitterHeightJitter, EmitterHeightJitter),
                UnityEngine.Random.Range(-EmitterRandomOffset, EmitterRandomOffset)
            );

            return basePos + randomOffset;
        }

        /// <summary>
        /// 创建单个粒子发射器
        /// </summary>
        private ParticleSystem CreateEmitter(string name, Vector3 localPosition, ParticleSystemSimulationSpace space, bool isLocal)
        {
            GameObject emitterObj = new GameObject(name);
            Transform effectTransform = cachedTransform != null ? cachedTransform : transform;
            emitterObj.transform.SetParent(effectTransform);
            emitterObj.transform.localPosition = localPosition;
            emitterObj.transform.localRotation = Quaternion.identity;

            ParticleSystem ps = emitterObj.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(ps, space, isLocal);

            return ps;
        }

        /// <summary>
        /// 配置粒子系统参数
        /// </summary>
        private void ConfigureParticleSystem(ParticleSystem ps, ParticleSystemSimulationSpace space, bool isLocal)
        {
            // AddComponent 的默认系统可能已播放；先清掉默认粒子，再设置模块并显式启动。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            // 获取参数
            int maxParticles = isLocal ? LocalMaxParticles : WorldMaxParticles;
            float lifetime = isLocal ? LocalLifetime : WorldLifetime;
            float speed = isLocal ? LocalSpeed : WorldSpeed;
            float size = isLocal ? LocalSize : WorldSize;
            float sizeMin = Mathf.Clamp(isLocal ? LocalSizeMin : WorldSizeMin, 0.001f, size);
            float alpha = isLocal ? LocalAlpha : WorldAlpha;
            float emissionRate = isLocal ? LocalEmissionRate : WorldEmissionRate;
            float shapeRadius = isLocal ? LocalShapeRadius : WorldShapeRadius;

            // 配置渲染器
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleRenderMode;
                renderer.sharedMaterial = CreateMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // 配置主模块
            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, size);
            if (RandomStartRotation)
            {
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            Color tint = ParticleTint;
            main.startColor = new Color(tint.r, tint.g, tint.b, alpha);
            main.simulationSpace = space;
            main.loop = true;

            // 配置发射：只走固定速率（没有每帧手撒，密度与帧率无关）
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = emissionRate;

            // 配置形状
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = shapeRadius;

            float radial = RadialSpeed;
            if (radial != 0f)
            {
                var velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = new ParticleSystem.MinMaxCurve(0f);
                velocity.y = new ParticleSystem.MinMaxCurve(0f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f);
                velocity.radial = new ParticleSystem.MinMaxCurve(radial);
            }

            // 配置颜色生命周期
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                // 生命周期颜色与 startColor 相乘，白色保持原始霜蓝，避免 RGB 被平方；
                // 末端乘子默认也是白色，子类可让颜色随寿命变深。
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(LifetimeEndColor, 1f) },
                // 梯度只描述**形状**，强度由 startColor 的 alpha 一次性给足。
                // 早先两边都乘 alpha，实际不透明度是 alpha²——常量的含义和注释对不上，
                // 而且再调 LocalAlpha/WorldAlpha 会得到平方响应。
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0.55f, 0.55f),
                    new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = g;

            // 配置尺寸生命周期
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, SizeOverLifetimeCurve);

            // 从零开始按速率长出来：每颗粒子自带 0→1 的淡入，整团自然浮现，不在第一帧撒一坨
            ps.Play();
        }

        /// <summary>
        /// 创建粒子材质（使用静态缓存，所有实例共享）
        /// [性能优化] 避免每次创建特效时重复创建材质和纹理
        /// </summary>
        protected virtual Material CreateMaterial()
        {
            return GetSharedParticleMaterial();
        }

        /// <summary>
        /// 共享程序化粒子材质（Alpha Blended + 全 Mod 共用软圆贴图），全 Mod 复用。
        /// 环形光环之外的程序化特效（例如新武器挥砍拖尾、遗种巢崽特效的模板）也走这里。
        /// 颜色与 alpha 都是 1×（_TintColor 中性 0.5），直接按想要的实际值写顶点色。
        /// </summary>
        internal static Material GetSharedParticleMaterial()
        {
            // 使用静态缓存的材质
            lock (materialLock)
            {
                if (cachedParticleMaterial != null)
                {
                    // 共用软圆随 Mod 卸载路径销毁；同一进程里重新启用 Mod 时补回，免得画成方片
                    if (cachedParticleMaterial.mainTexture == null)
                    {
                        cachedParticleMaterial.mainTexture = BossRushFxMaterials.GetSoftCircleTexture();
                    }
                    return cachedParticleMaterial;
                }

                string[] shaderNames = {
                    "Particles/Alpha Blended",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Mobile/Particles/Alpha Blended",
                    "UI/Default",
                    "Sprites/Default"
                };

                foreach (string shaderName in shaderNames)
                {
                    Shader s = Shader.Find(shaderName);
                    if (s != null)
                    {
                        Material mat = new Material(s);
                        mat.name = "ParticleEffectMat_Shared";
                        mat.renderQueue = 3000;
                        mat.hideFlags = HideFlags.DontSave; // 防止被意外销毁

                        if (mat.HasProperty("_TintColor"))
                        {
                            // Legacy 粒子着色器是 2 × 顶点色 × _TintColor：0.5 才是 1×（VB-27）。
                            // SetVector 写原始值——SetColor 在线性空间会先做 sRGB→线性，0.5 会变成 0.214。
                            mat.SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                        }
                        if (mat.HasProperty("_Color"))
                        {
                            mat.SetColor("_Color", Color.white);
                        }

                        mat.mainTexture = BossRushFxMaterials.GetSoftCircleTexture();
                        cachedParticleMaterial = mat;
                        return mat;
                    }
                }

                return null;
            }
        }

        // ========== 停止和清理 ==========

        private IEnumerator DoStop()
        {
            // 所有发射器（含子类加的点缀层）停发射，已发出的粒子照常走完寿命
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(false);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null)
                {
                    systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            // 等最后一颗粒子消失（或超时）再销毁，不在粒子正亮的时候一刀切
            float waited = 0f;
            while (waited < MaxStopWaitSeconds && AnyAlive(systems))
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Destroy(gameObject);
        }

        private static bool AnyAlive(ParticleSystem[] systems)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].IsAlive(false))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 清理材质（使用共享材质后不再需要销毁）
        /// [性能优化] 共享材质由静态缓存管理，不在实例销毁时清理
        /// </summary>
        private void CleanupMaterials()
        {
            // 使用共享材质后，不再需要在实例销毁时清理材质
            // 材质和纹理由静态缓存管理，在应用程序退出时自动清理
            // 这避免了频繁创建/销毁材质带来的GC压力
        }
    }
}
