// ============================================================================
// RingParticleEffect.cs - 通用环形粒子特效基类
// ============================================================================
// 模块说明：
//   提供可复用的环形粒子特效系统
//   支持Local和World双层粒子系统
//   可通过子类配置参数实现不同的视觉效果
// ============================================================================

using System;
using System.Collections;
using UnityEngine;

namespace BossRush.Common.Effects
{
    /// <summary>
    /// 通用环形粒子特效基类
    /// 创建多个环形分布的粒子发射器，支持Local和World双层系统
    /// </summary>
    public abstract class RingParticleEffect : MonoBehaviour
    {
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
        
        protected virtual int LocalMaxParticles => 100;
        protected virtual float LocalLifetime => 0.3f;
        protected virtual float LocalSpeed => 0.3f;
        protected virtual float LocalSize => 1.2f;
        protected virtual float LocalAlpha => 0.7f;
        protected virtual float LocalEmissionRate => 15f;
        protected virtual float LocalShapeRadius => 0.1f;
        protected virtual int LocalEmitPerFrame => 1;
        
        // ========== World空间粒子参数 ==========
        
        protected virtual int WorldMaxParticles => 100;
        protected virtual float WorldLifetime => 0.2f;
        protected virtual float WorldSpeed => 0.3f;
        protected virtual float WorldSize => 1.2f;
        protected virtual float WorldAlpha => 0.35f;
        protected virtual float WorldEmissionRate => 10f;
        protected virtual float WorldShapeRadius => 0.05f;
        protected virtual int WorldEmitPerFrame => 1;
        
        // ========== 内部状态 ==========
        
        private ParticleSystem[] emittersLocal;
        private ParticleSystem[] emittersWorld;
        private bool isStopping = false;
        private Coroutine stopCoroutine;
        private Transform followTarget;

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
        /// 停止特效（淡出并销毁）
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
        
        private void Start()
        {
            CreateParticleSystems();
        }
        
        private void Update()
        {
            // 跟随目标位置
            if (followTarget != null)
            {
                transform.position = followTarget.position + FollowOffset;
            }
        }
        
        private void LateUpdate()
        {
            // 每帧发射粒子
            if (!isStopping)
            {
                EmitParticles();
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
                UnityEngine.Random.Range(-0.1f, 0.1f),
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
            emitterObj.transform.SetParent(transform);
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
            // 获取参数
            int maxParticles = isLocal ? LocalMaxParticles : WorldMaxParticles;
            float lifetime = isLocal ? LocalLifetime : WorldLifetime;
            float speed = isLocal ? LocalSpeed : WorldSpeed;
            float size = isLocal ? LocalSize : WorldSize;
            float alpha = isLocal ? LocalAlpha : WorldAlpha;
            float emissionRate = isLocal ? LocalEmissionRate : WorldEmissionRate;
            float shapeRadius = isLocal ? LocalShapeRadius : WorldShapeRadius;
            
            // 配置渲染器
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.material = CreateMaterial();
            }
            
            // 配置主模块
            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = new Color(1f, 1f, 1f, alpha);
            main.simulationSpace = space;
            main.loop = true;
            
            // 配置发射
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = emissionRate;
            
            // 配置形状
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = shapeRadius;
            
            // 配置颜色生命周期
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = g;
            
            // 配置尺寸生命周期
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, 1.3f);
            
            // 播放并发射初始粒子
            ps.Play();
            ps.Emit(10);
        }
        
        /// <summary>
        /// 创建粒子材质（可被子类覆盖以自定义材质）
        /// </summary>
        protected virtual Material CreateMaterial()
        {
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
                    mat.name = "ParticleEffectMat";
                    mat.renderQueue = 3000;
                    
                    if (mat.HasProperty("_TintColor"))
                    {
                        mat.SetColor("_TintColor", Color.white);
                    }
                    if (mat.HasProperty("_Color"))
                    {
                        mat.SetColor("_Color", Color.white);
                    }
                    
                    mat.mainTexture = CreateTexture();
                    return mat;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// 创建粒子纹理（可被子类覆盖以自定义纹理）
        /// </summary>
        protected virtual Texture2D CreateTexture()
        {
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float dx = (x - size / 2f) / (size / 2f);
                    float dy = (y - size / 2f) / (size / 2f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (dist < 1f)
                    {
                        float alpha = (1f - dist) * (1f - dist);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    }
                }
            }
            
            tex.Apply();
            return tex;
        }

        // ========== 粒子发射 ==========
        
        private void EmitParticles()
        {
            if (emittersLocal != null)
            {
                foreach (var ps in emittersLocal)
                {
                    if (ps != null)
                    {
                        ps.Emit(LocalEmitPerFrame);
                    }
                }
            }
            
            if (emittersWorld != null)
            {
                foreach (var ps in emittersWorld)
                {
                    if (ps != null)
                    {
                        ps.Emit(WorldEmitPerFrame);
                    }
                }
            }
        }

        // ========== 停止和清理 ==========
        
        private IEnumerator DoStop()
        {
            // 停止所有发射器
            if (emittersLocal != null)
            {
                foreach (var ps in emittersLocal)
                {
                    if (ps != null)
                    {
                        var emission = ps.emission;
                        emission.rateOverTime = 0f;
                    }
                }
            }
            
            if (emittersWorld != null)
            {
                foreach (var ps in emittersWorld)
                {
                    if (ps != null)
                    {
                        var emission = ps.emission;
                        emission.rateOverTime = 0f;
                    }
                }
            }
            
            // 等待粒子消失
            yield return new WaitForSeconds(1f);
            
            Destroy(gameObject);
        }
        
        private void CleanupMaterials()
        {
            if (emittersLocal != null)
            {
                foreach (var ps in emittersLocal)
                {
                    if (ps != null)
                    {
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null && renderer.material != null)
                        {
                            Destroy(renderer.material);
                        }
                    }
                }
            }
            
            if (emittersWorld != null)
            {
                foreach (var ps in emittersWorld)
                {
                    if (ps != null)
                    {
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null && renderer.material != null)
                        {
                            Destroy(renderer.material);
                        }
                    }
                }
            }
        }
    }
}
