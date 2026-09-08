// ============================================================================
// NewWeaponSwingFx.cs - 三把新近战武器的挥砍拖尾
// ============================================================================
// 模块说明：
//   毒蛇匕首（毒绿）、冰霜长矛（冰蓝）、召唤法杖（灵魂紫）共用同一套拖尾实现，
//   只有颜色和扫掠角度按武器不同。做法参照 FrostmourneSwingFx：
//   在 CA_Attack.OnStart 成功后生成一个绕玩家扫过去的粒子节点。
//
//   与霜之哀伤的差别：霜之哀伤要克隆龙息火焰拖尾再重染，依赖 Frostmourne 自己的资源链；
//   本实现完全程序化——粒子系统运行时构造，材质走 RingParticleEffect.GetSharedParticleMaterial()
//   （Alpha Blended + 64x64 径向渐变贴图，全 Mod 共享一份），零新增美术资源。
//
// 生命周期（AGENTS.md 4.12）：
//   对象池上限 8，用完回池、池满销毁；不持有玩家/场景引用，切图后残留对象在 Unity 里 == null，
//   Acquire 前先剔空槽。只有主玩家实际手持对应武器挥砍时才会生成，背包/仓库/NPC 不触发。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>新武器挥砍拖尾（池化，一次挥砍一个实例）</summary>
    public class NewWeaponSwingFx : MonoBehaviour
    {
        private const float BaseTrailDistance = 1.4f;
        private const float MinTrailDistance = 0.3f;
        private const float Duration = 0.2f;
        private const int MaxPoolSize = 8;

        private static readonly Stack<NewWeaponSwingFx> Pool = new Stack<NewWeaponSwingFx>();

        private Transform trailRoot;
        private Transform trailNode;
        private ParticleSystem trailParticles;
        private ParticleSystemRenderer trailRenderer;

        private float elapsed;
        private float startAngle;
        private float sweepAngle;
        private bool isPlaying;
        private bool emissionStopped;

        /// <summary>
        /// 在 position/rotation 处播放一次拖尾。
        /// </summary>
        /// <param name="coreColor">粒子起始色（武器主色）</param>
        /// <param name="fadeColor">粒子淡出色</param>
        /// <param name="sweep">扫掠角度，正值为顺时针</param>
        /// <param name="rangeScale">按武器攻击距离缩放拖尾半径</param>
        internal static void PlayAt(Vector3 position, Quaternion rotation, Color coreColor, Color fadeColor, float sweep, float rangeScale)
        {
            NewWeaponSwingFx fx = Acquire();
            if (fx == null || fx.gameObject == null) return;

            fx.transform.position = position;
            fx.transform.rotation = rotation;
            fx.gameObject.SetActive(true);
            fx.Initialize(coreColor, fadeColor, sweep, rangeScale);
        }

        private static NewWeaponSwingFx Acquire()
        {
            while (Pool.Count > 0)
            {
                NewWeaponSwingFx pooled = Pool.Pop();
                // 过图后池里的对象在 Unity 里 == null 为真，直接丢弃继续找
                if (pooled != null && pooled.gameObject != null)
                {
                    return pooled;
                }
            }

            GameObject host = new GameObject("NewWeapon_SwingFX");
            host.SetActive(false);
            return host.AddComponent<NewWeaponSwingFx>();
        }

        private void Initialize(Color coreColor, Color fadeColor, float sweep, float rangeScale)
        {
            float clampedScale = Mathf.Max(0.2f, rangeScale);
            float trailDistance = Mathf.Max(MinTrailDistance, BaseTrailDistance * clampedScale);
            float sizeScale = Mathf.Lerp(1f, clampedScale, 0.35f);

            EnsureBuilt();

            startAngle = -sweep * 0.5f;
            sweepAngle = sweep;

            trailRoot.localRotation = Quaternion.Euler(0f, startAngle, 0f);
            if (trailNode != null)
            {
                trailNode.localPosition = new Vector3(0f, 0f, trailDistance);
            }

            Tint(coreColor, fadeColor, sizeScale);
            Restart();

            elapsed = 0f;
            emissionStopped = false;
            isPlaying = true;
        }

        private void EnsureBuilt()
        {
            if (trailRoot != null && trailParticles != null) return;

            trailRoot = new GameObject("SwingTrailPivot").transform;
            trailRoot.SetParent(transform, false);
            trailRoot.localPosition = Vector3.zero;

            GameObject nodeObject = new GameObject("TrailNode");
            // 先失活再挂 ParticleSystem：playOnAwake 默认 true，挂在活跃对象上会先按默认参数
            // （白色大颗粒）自播一次，之后才被下面的配置覆盖。失活期间 Awake 不跑，配置完再激活。
            nodeObject.SetActive(false);
            trailNode = nodeObject.transform;
            trailNode.SetParent(trailRoot, false);

            trailParticles = nodeObject.AddComponent<ParticleSystem>();
            trailRenderer = nodeObject.GetComponent<ParticleSystemRenderer>();
            if (trailRenderer != null)
            {
                Material shared = RingParticleEffect.GetSharedParticleMaterial();
                if (shared != null)
                {
                    trailRenderer.sharedMaterial = shared;
                }
                trailRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                trailRenderer.alignment = ParticleSystemRenderSpace.View;
            }

            ParticleSystem.MainModule main = trailParticles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Duration;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.ShapeModule shape = trailParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            ParticleSystem.EmissionModule emission = trailParticles.emission;
            emission.enabled = true;

            nodeObject.SetActive(true);
        }

        private void Tint(Color coreColor, Color fadeColor, float sizeScale)
        {
            if (trailParticles == null) return;

            ParticleSystem.MainModule main = trailParticles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(coreColor, fadeColor);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
            main.startSizeMultiplier = 0.55f * sizeScale;

            ParticleSystem.EmissionModule emission = trailParticles.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(70f);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = trailParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(coreColor, 0f),
                    new GradientColorKey(fadeColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(coreColor.a, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private void Restart()
        {
            if (trailParticles == null) return;
            ParticleSystem.EmissionModule emission = trailParticles.emission;
            emission.enabled = true;
            trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            trailParticles.Play(true);
        }

        private void Update()
        {
            if (!isPlaying) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);

            if (trailRoot != null)
            {
                // 与霜之哀伤同款缓出曲线，起手快、收势慢
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                trailRoot.localRotation = Quaternion.Euler(0f, startAngle + sweepAngle * easeT, 0f);
            }

            if (!emissionStopped && t >= 0.8f && trailParticles != null)
            {
                emissionStopped = true;
                ParticleSystem.EmissionModule emission = trailParticles.emission;
                emission.enabled = false;
            }

            if (t >= 1f)
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            if (!isPlaying) return;
            isPlaying = false;
            emissionStopped = false;

            if (trailParticles != null)
            {
                trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (Pool.Count < MaxPoolSize)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>清空对象池（模块 OnDestroy 路径调用）。销毁池内残留对象，不只是丢引用。</summary>
        public static void ResetStaticCaches()
        {
            while (Pool.Count > 0)
            {
                NewWeaponSwingFx pooled = Pool.Pop();
                if (pooled != null && pooled.gameObject != null)
                {
                    Destroy(pooled.gameObject);
                }
            }
        }
    }
}
