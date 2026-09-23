// ============================================================================
// NewWeaponSwingFx.cs - 三把新近战武器的挥砍拖尾
// ============================================================================
// 模块说明：
//   毒蛇匕首（毒绿）、冰霜长矛（冰蓝）、召唤法杖（灵魂紫）共用同一套拖尾实现，
//   只有颜色和扫掠角度按武器不同。做法参照 FrostmourneSwingFx：
//   在 CA_Attack.OnStart 成功后生成一个绕玩家扫过去的粒子节点。
//
//   与霜之哀伤的差别：霜之哀伤要克隆龙息火焰拖尾再重染，依赖 Frostmourne 自己的资源链；
//   本实现完全程序化——拖尾与粒子运行时构造，材质走共享特效层，零新增美术资源。
//
// 2026-09-23 审美修（VA-09）：旧版只有一串 0.22 m 的圆点粒子，间距 3.8 cm、每颗与相邻 6 颗重叠，
//   出来是一根粗细均匀、中心不透明的荧光管（毒蛇匕首还是 #7CFC00 霓虹绿再 ×2 进 HDR）。现在：
//     - 主体：一条 TrailRenderer 刀光（条带贴图、加色、头宽尾尖、0.16 s 淡尽），读得出刀锋前缘；
//     - 粒子降为点缀：每米 10 颗、5–9 cm 的 HDR 星屑，沿弧逐点撒（密度与帧率无关）；
//     - 配色去霓虹（NewWeaponPalette）。
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
        private const float Duration = 0.22f;
        private const float ParticleTailDuration = 0.3f;
        private const int MaxPoolSize = 8;

        /// <summary>每米弧长撒几颗星屑。乘上 0.22 秒扫过的弧长（约 2.3–3.4 米）得到一条挥击的总量。</summary>
        private const float ParticlesPerMeter = 10f;

        /// <summary>刀光拖尾的存活秒数（挥击结束后 0.16 秒内淡尽，早于粒子尾巴）。</summary>
        private const float BladeTrailTime = 0.16f;

        /// <summary>单帧最多补几个采样点。60fps 下一帧只走 1/13 的弧，够用；掉帧时也不会一次撒爆。</summary>
        private const int MaxSubStepsPerFrame = 12;

        /// <summary>粒子上限。ParticlesPerMeter × 最长弧长（约 3.4 米）留一倍余量。</summary>
        private const int MaxParticles = 192;

        private static readonly Stack<NewWeaponSwingFx> Pool = new Stack<NewWeaponSwingFx>();

        private Transform trailRoot;
        private Transform trailNode;
        private ParticleSystem trailParticles;
        private ParticleSystemRenderer trailRenderer;
        private TrailRenderer bladeTrail;

        private float elapsed;
        private float startAngle;
        private float sweepAngle;
        private bool isPlaying;

        // 手撒粒子需要的状态：上一帧撒到哪个角度、这一次挥击的半径与配色。
        private float lastEmittedAngle;
        private float trailRadius;
        private float particleSize;

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
            trailRadius = trailDistance;
            particleSize = 0.07f * sizeScale;
            lastEmittedAngle = startAngle;

            trailRoot.localRotation = Quaternion.Euler(0f, startAngle, 0f);
            if (trailNode != null)
            {
                trailNode.localPosition = new Vector3(0f, 0f, trailDistance);
            }

            Tint(coreColor, fadeColor, sizeScale);
            Restart();
            // 池对象复用：先清掉上一次挥砍留下的顶点，否则会从上一次的位置拉出一条长线
            if (bladeTrail != null)
            {
                bladeTrail.Clear();
                bladeTrail.emitting = true;
            }

            elapsed = 0f;
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
                Material sparkle = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
                if (sparkle == null) sparkle = RingParticleEffect.GetSharedParticleMaterial();
                if (sparkle != null)
                {
                    trailRenderer.sharedMaterial = sparkle;
                }
                trailRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                trailRenderer.alignment = ParticleSystemRenderSpace.View;
            }

            ParticleSystem.MainModule main = trailParticles.main;
            main.loop = false;
            main.playOnAwake = false;
            // 手撒粒子期间系统必须一直在跑：duration 设成整条尾迹的寿命，
            // 否则 0.22 秒一到系统就 Stop，后面几帧 Emit 会被吞掉。
            main.duration = Duration + ParticleTailDuration;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 位置由 Update 逐点给出，形状发射器不参与。
            ParticleSystem.ShapeModule shape = trailParticles.shape;
            shape.enabled = false;

            // 自动发射一律关掉。rateOverDistance 是「这一帧走了多远就撒多少颗」，
            // 但 Unity 把这一批全撒在**当帧的那一个位置**上；挥击用的是缓出曲线，
            // 起手一帧就吃掉小半条弧，于是起手处堆成一坨、后半条几乎是空的
            // ——2026-09-19 实测反馈的「只在一开始弄一坨」就是这个。
            ParticleSystem.EmissionModule emission = trailParticles.emission;
            emission.enabled = false;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);

            // 尾迹尾部收细，避免整条粗细一样像一根棍子。
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = trailParticles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve taper = new AnimationCurve();
            taper.AddKey(0f, 1f);
            taper.AddKey(0.35f, 0.85f);
            taper.AddKey(1f, 0.15f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, taper);

            // 刀光主体：世界空间拖尾，头宽尾尖；材质是共享条带贴图 + 加色亮度档
            Material blade = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (blade != null)
            {
                bladeTrail = nodeObject.AddComponent<TrailRenderer>();
                bladeTrail.sharedMaterial = blade;
                bladeTrail.time = BladeTrailTime;
                bladeTrail.minVertexDistance = 0.04f;
                bladeTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.32f), new Keyframe(1f, 0f));
                bladeTrail.textureMode = LineTextureMode.Stretch;
                bladeTrail.alignment = LineAlignment.View;
                bladeTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bladeTrail.receiveShadows = false;
                bladeTrail.emitting = false;
            }

            nodeObject.SetActive(true);
        }

        private void Tint(Color coreColor, Color fadeColor, float sizeScale)
        {
            if (trailParticles == null) return;

            ParticleSystem.MainModule main = trailParticles.main;
            main.startColor = Color.white;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSizeMultiplier = 0.07f * sizeScale;

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

            if (bladeTrail != null)
            {
                bladeTrail.widthMultiplier = sizeScale;
                Gradient blade = new Gradient();
                blade.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(coreColor, 0f), new GradientColorKey(fadeColor, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(0f, 1f) });
                bladeTrail.colorGradient = blade;
            }
        }

        private void Restart()
        {
            if (trailParticles == null) return;
            trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            trailParticles.Play(true);
        }

        private void Update()
        {
            if (!isPlaying) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);

            // 与霜之哀伤同款缓出曲线，起手快、收势慢
            float easeT = 1f - Mathf.Pow(1f - t, 3f);
            float currentAngle = startAngle + sweepAngle * easeT;

            if (trailRoot != null)
            {
                trailRoot.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
            }

            EmitAlongArc(currentAngle);
            lastEmittedAngle = currentAngle;

            // 挥击停发后让世界空间粒子自然淡出，不能在 0.22 秒时直接清空整条尾迹。
            if (elapsed >= Duration + ParticleTailDuration)
            {
                Recycle();
            }
        }

        /// <summary>
        /// 在上一帧与这一帧之间按等弧长插值补点，逐点 <see cref="ParticleSystem.Emit"/>。
        /// 这是「拖尾均匀」的关键：不能把一帧的量全撒在当帧位置上。
        /// </summary>
        private void EmitAlongArc(float currentAngle)
        {
            if (trailParticles == null || trailRoot == null) return;

            float deltaDegrees = currentAngle - lastEmittedAngle;
            if (Mathf.Abs(deltaDegrees) < 0.001f) return;

            float arcLength = Mathf.Abs(deltaDegrees) * Mathf.Deg2Rad * trailRadius;
            int steps = Mathf.Clamp(Mathf.CeilToInt(arcLength * ParticlesPerMeter), 1, MaxSubStepsPerFrame);

            Transform pivotParent = trailRoot.parent != null ? trailRoot.parent : transform;
            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
            emitParams.applyShapeToPosition = false;
            emitParams.startColor = Color.white;
            emitParams.velocity = Vector3.zero;

            for (int i = 1; i <= steps; i++)
            {
                float angle = Mathf.Lerp(lastEmittedAngle, currentAngle, (float)i / steps);
                Vector3 local = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, trailRadius);
                // 加一点点抖动，免得整条尾迹是一根几何上完美的细线。
                local += new Vector3(
                    UnityEngine.Random.Range(-0.05f, 0.05f),
                    UnityEngine.Random.Range(-0.06f, 0.06f),
                    UnityEngine.Random.Range(-0.05f, 0.05f));

                emitParams.position = pivotParent.TransformPoint(local);
                emitParams.startSize = particleSize * UnityEngine.Random.Range(0.75f, 1.25f);
                emitParams.startLifetime = UnityEngine.Random.Range(0.14f, 0.3f);
                trailParticles.Emit(emitParams, 1);
            }
        }

        private void Recycle()
        {
            if (!isPlaying) return;
            isPlaying = false;

            if (trailParticles != null)
            {
                trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (bladeTrail != null)
            {
                bladeTrail.emitting = false;
                bladeTrail.Clear();
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
