// ============================================================================
// DragonKingWarningVisuals.cs - 龙王预警的表现组件（太阳舞落点圈、以太长矛贴地预警）
// ============================================================================
// 2026-09-23 特效审美审查 VB-11 / VB-13：WarningCircleAnimation 原在 DragonKingAbilityHelpers.cs，重做后那份文件超过
// 单文件行数预算（LargeFileBudgetGuard），整类挪到这里；DragonKingLanceWarning 是本轮新写的长矛贴地预警。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 预警圆圈缩放动画（太阳舞落点圈）
    /// 使用组件自身 Update，避免每次施法都额外启动协程
    ///
    /// 2026-09-23 特效审美审查 VB-13：旧版从 20 m 按 t² 收到一个点——开场在屏幕外（一屏高约 20 m），
    /// 前 1 s 只收到约 11 m，最后 0.3 s 猛冲到脚下，落地时已是一个点。现在：
    /// - 从 9 m（留在屏内）EaseOut 收到 Boss 占地 1.2 m，先快后慢、约 0.9 s 基本落定；
    /// - 落点的静态内圈（子物体 WarningCircleInner）一开始就画出来，外圈收到它身上读作「就是这里」；
    /// - 最后 0.2 s 外圈带宽 0.30 → 0.50 脉冲，作为「现在落下」的信号；出现时 0.15 s 淡入；
    /// - 圈摊平在地面上（TransformZ，物体绕 X 转 90°，局部 XY 即地面），不再是面向镜头、一半插进地里的带子。
    /// 时长与伤害判定不变（由太阳舞协程的 wait15s 决定）。
    /// </summary>
    public class WarningCircleAnimation : MonoBehaviour
    {
        private const int SegmentCount = 64;
        internal const float BaseWidth = 0.30f;
        private const float PulseWidth = 0.50f;
        private const float PulseSeconds = 0.2f;
        private const float AppearSeconds = 0.15f;
        /// <summary>暖金外圈（旧的是 (1,0.9,0) 纯黄硬边带）。</summary>
        internal static readonly Color RingColor = new Color(1f, 0.78f, 0.35f, 0.9f);
        /// <summary>落点内圈：更淡的暖金。</summary>
        internal static readonly Color InnerColor = new Color(1f, 0.86f, 0.55f, 0.7f);
        private static Vector3[] cachedUnitPoints = null;
        private static readonly Vector3[] positionsBuffer = new Vector3[SegmentCount];

        private LineRenderer lineRenderer;
        private LineRenderer innerRenderer;
        private float duration = 1f;
        private float startRadius = 9f;
        private float endRadius = 1.2f;
        private float elapsed = 0f;
        private bool isAnimating = false;

        void Awake()
        {
            CacheRenderers();
        }

        private void CacheRenderers()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }
            if (innerRenderer == null)
            {
                Transform inner = transform.Find("WarningCircleInner");
                innerRenderer = inner != null ? inner.GetComponent<LineRenderer>() : null;
            }
        }

        public void ResetAnimation(float durationSeconds, float fromRadius, float toRadius)
        {
            CacheRenderers();

            duration = Mathf.Max(0.01f, durationSeconds);
            startRadius = fromRadius;
            endRadius = toRadius;
            elapsed = 0f;
            isAnimating = true;

            if (lineRenderer != null)
            {
                lineRenderer.alignment = LineAlignment.TransformZ;
                lineRenderer.positionCount = SegmentCount;
                ApplyRadius(lineRenderer, startRadius);
            }
            if (innerRenderer != null)
            {
                innerRenderer.alignment = LineAlignment.TransformZ;
                innerRenderer.positionCount = SegmentCount;
                ApplyRadius(innerRenderer, Mathf.Max(0.3f, endRadius));
            }
            ApplyLook(0f);
        }

        void Update()
        {
            if (!isAnimating || lineRenderer == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float currentRadius = Mathf.Lerp(startRadius, endRadius, BossRushUI.EaseOut(t));
            ApplyRadius(lineRenderer, currentRadius);
            ApplyLook(elapsed);

            if (t >= 1f)
            {
                isAnimating = false;
            }
        }

        private void ApplyLook(float age)
        {
            float appear = BossRushUI.SmoothStep(age / AppearSeconds);
            float remaining = duration - age;
            float pulse = remaining < PulseSeconds ? BossRushUI.SmoothStep(1f - remaining / PulseSeconds) : 0f;
            if (lineRenderer != null)
            {
                float width = Mathf.Lerp(BaseWidth, PulseWidth, pulse);
                lineRenderer.startWidth = width;
                lineRenderer.endWidth = width;
                Color ring = Color.Lerp(RingColor, Color.white, 0.35f * pulse);
                ring.a = Mathf.Lerp(RingColor.a, 1f, pulse) * appear;
                lineRenderer.startColor = ring;
                lineRenderer.endColor = ring;
            }
            if (innerRenderer != null)
            {
                Color inner = InnerColor;
                inner.a *= appear;
                innerRenderer.startColor = inner;
                innerRenderer.endColor = inner;
            }
        }

        private static void ApplyRadius(LineRenderer target, float radius)
        {
            Vector3[] unitPoints = GetUnitPoints();
            for (int i = 0; i < unitPoints.Length; i++)
            {
                positionsBuffer[i] = unitPoints[i] * radius;
            }
            target.SetPositions(positionsBuffer);
        }

        /// <summary>局部 XY 平面上的单位圆：物体绕 X 转 90° 后就是地面（配 TransformZ 摊平）。</summary>
        private static Vector3[] GetUnitPoints()
        {
            if (cachedUnitPoints == null || cachedUnitPoints.Length != SegmentCount)
            {
                cachedUnitPoints = new Vector3[SegmentCount];
                for (int i = 0; i < SegmentCount; i++)
                {
                    float angle = (float)i / SegmentCount * 360f * Mathf.Deg2Rad;
                    cachedUnitPoints[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                }
            }

            return cachedUnitPoints;
        }
    }

    /// <summary>
    /// 以太长矛的贴地预警（VB-11）。旧版是每条 50 m 长、0.05 m（≈3.5 px）的彩虹细线，悬在离地 1 m 处，
    /// 表达不了「往旁边闪多少才安全」（真正的危险带是线两侧各约 2 m），彩虹色在暖色场景里也廉价。现在两层、都贴地：
    /// - 核心线：长矛自己的淡蓝，蓄力期间 0.12 → 0.25 m、不透明度 0.6 → 1（加色软边带）；发射瞬间闪白 0.06 s；
    /// - 危险带：宽 4 m（= 2 × 长矛命中半径）的软边带，不透明度随蓄力从峰值的 45% 升到峰值；十条叠起来中心最亮、自然成扇。
    /// 根物体只承载发射位置与朝向（FireLanceFromWarningLine 读它），判定与时序不变；两条线是子物体，随根物体进出对象池。
    /// </summary>
    public class DragonKingLanceWarning : MonoBehaviour
    {
        private const float CoreStartWidth = 0.12f;
        private const float CoreEndWidth = 0.25f;
        private const float CoreFlashWidth = 0.32f;
        private const float BandWidth = 4f;
        private const float BandPeakAlpha = 0.07f;
        private const float FlashSeconds = 0.06f;
        private static readonly Color CoreColor = new Color(0.8f, 0.85f, 1f, 1f);
        private static readonly Color BandColor = new Color(0.72f, 0.78f, 1f, 1f);

        private LineRenderer core;
        private LineRenderer band;
        private float spawnTime;
        private float chargeSeconds = 1f;
        private float appearSeconds = 0.08f;
        private float bandPeak = BandPeakAlpha;
        private float flashStart = -1f;
        private System.Action<GameObject> flashDone;

        /// <summary>建池时调一次：两条贴地子线。</summary>
        internal void Build()
        {
            core = CreateChild("Core", DragonKingFxShared.Band(BossRushFxBlend.Additive), 101);
            band = CreateChild("DangerBand", DragonKingFxShared.Band(BossRushFxBlend.Alpha), 99);
            band.widthMultiplier = BandWidth;
        }

        private LineRenderer CreateChild(string name, Material material, int sortingOrder)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            LineRenderer lr = child.AddComponent<LineRenderer>();
            if (material != null)
            {
                lr.sharedMaterial = material;
            }
            else
            {
                lr.enabled = false;
            }
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.TransformZ;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = sortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>
        /// 画一条预警：<paramref name="groundCenter"/> 是贴地中点，<paramref name="direction"/> 是水平方向；
        /// <paramref name="bandScale"/> 按同时在场的条数压危险带的峰值不透明度。
        /// </summary>
        internal void Begin(Vector3 groundCenter, Vector3 direction, float length, float charge, float appear, float bandScale)
        {
            if (core == null || band == null)
            {
                Build();
            }
            // 子物体世界朝向恒为「绕 X 转 90°」：Z 轴朝下，TransformZ 把带子摊平在地面上（根物体的 Y 旋转是长矛方向，不能动它）。
            Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
            core.transform.rotation = flat;
            band.transform.rotation = flat;
            Vector3 half = direction * (length * 0.5f);
            core.SetPosition(0, groundCenter - half);
            core.SetPosition(1, groundCenter + half);
            band.SetPosition(0, groundCenter - half + Vector3.down * 0.01f);
            band.SetPosition(1, groundCenter + half + Vector3.down * 0.01f);
            spawnTime = Time.time;
            chargeSeconds = Mathf.Max(0.05f, charge);
            appearSeconds = Mathf.Max(0.01f, appear);
            bandPeak = BandPeakAlpha * Mathf.Clamp(bandScale, 0.2f, 1f);
            flashStart = -1f;
            flashDone = null;
            Apply(0f);
            enabled = true;
        }

        /// <summary>发射那一刻闪白 <see cref="FlashSeconds"/> 秒，闪完调 <paramref name="onDone"/>（由控制器回池）。</summary>
        internal void Flash(System.Action<GameObject> onDone)
        {
            flashStart = Time.time;
            flashDone = onDone;
            if (core != null)
            {
                core.widthMultiplier = CoreFlashWidth;
                core.startColor = Color.white;
                core.endColor = Color.white;
            }
            if (band != null)
            {
                Color c = BandColor;
                c.a = bandPeak * 1.4f;
                band.startColor = c;
                band.endColor = c;
            }
            enabled = true;
        }

        void Update()
        {
            if (flashStart >= 0f)
            {
                if (Time.time - flashStart < FlashSeconds) return;
                System.Action<GameObject> done = flashDone;
                flashStart = -1f;
                flashDone = null;
                enabled = false;
                if (done != null)
                {
                    done(gameObject);
                }
                return;
            }
            Apply(Time.time - spawnTime);
        }

        private void Apply(float age)
        {
            float t = Mathf.Clamp01(age / chargeSeconds);
            float appear = BossRushUI.SmoothStep(age / appearSeconds);
            if (core != null)
            {
                core.widthMultiplier = Mathf.Lerp(CoreStartWidth, CoreEndWidth, t);
                Color c = CoreColor;
                c.a = Mathf.Lerp(0.6f, 1f, t) * appear;
                core.startColor = c;
                core.endColor = c;
            }
            if (band != null)
            {
                Color c = BandColor;
                c.a = Mathf.Lerp(bandPeak * 0.45f, bandPeak, t) * appear;
                band.startColor = c;
                band.endColor = c;
            }
        }

        void OnDisable()
        {
            // 回池（或死亡清理）时丢掉未完成的闪白回调：下一次 Begin 重新开始。
            flashStart = -1f;
            flashDone = null;
        }
    }
}
