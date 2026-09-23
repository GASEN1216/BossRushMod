// ============================================================================
// PhantomWitchVfxRedesign_RuntimeComponents.cs - runtime VFX component behaviours
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal abstract class PhantomWitchRuntimeComponentBase : MonoBehaviour
    {
        private Transform cachedTransform;

        protected Transform CachedTransform
        {
            get
            {
                if (cachedTransform == null)
                {
                    cachedTransform = transform;
                }

                return cachedTransform;
            }
        }
    }

    internal sealed class PhantomWitchBillboard : PhantomWitchRuntimeComponentBase
    {
        private void LateUpdate()
        {
            Transform cameraTransform = PhantomWitchFxRuntime.CurrentCameraTransform;
            if (cameraTransform != null)
            {
                CachedTransform.rotation = cameraTransform.rotation;
            }
        }
    }

    internal sealed class PhantomWitchWarpQuad : PhantomWitchRuntimeComponentBase
    {
        private Vector3 origin;
        private float jitterAmplitude;
        private float rotationSpeed;
        private float duration;
        private float elapsed;

        public void Configure(float jitterAmplitude, float rotationSpeed, float duration)
        {
            origin = CachedTransform.localPosition;
            this.jitterAmplitude = jitterAmplitude;
            this.rotationSpeed = rotationSpeed;
            this.duration = duration;
            elapsed = 0f;
        }

        private void Awake()
        {
            origin = CachedTransform.localPosition;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float noiseX = Mathf.Sin(Time.time * 2.1f) * jitterAmplitude;
            float noiseZ = Mathf.Cos(Time.time * 1.7f) * jitterAmplitude;
            Transform selfTransform = CachedTransform;
            selfTransform.localPosition = origin + new Vector3(noiseX, 0f, noiseZ);
            selfTransform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);

            if (duration > 0f && elapsed >= duration)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchTendrilMover : PhantomWitchRuntimeComponentBase
    {
        private Vector3 start;
        private Vector3 end;
        private float duration;
        private float elapsed;

        public void Configure(Vector3 start, Vector3 end, float duration)
        {
            this.start = start;
            this.end = end;
            this.duration = Mathf.Max(0.01f, duration);
            this.elapsed = 0f;
            CachedTransform.localPosition = start;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            CachedTransform.localPosition = Vector3.Lerp(start, end, eased);

            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchVerticalLineDrift : PhantomWitchRuntimeComponentBase
    {
        private Vector3 offset;
        private float duration;
        private float elapsed;
        private Vector3 startPosition;

        public void Configure(Vector3 offset, float duration)
        {
            this.offset = offset;
            this.duration = Mathf.Max(0.01f, duration);
            this.elapsed = 0f;
            this.startPosition = CachedTransform.localPosition;
        }

        private void Awake()
        {
            startPosition = CachedTransform.localPosition;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            CachedTransform.localPosition = Vector3.Lerp(startPosition, startPosition + offset, t);
            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchPulseScale : PhantomWitchRuntimeComponentBase
    {
        private Vector3 minScale;
        private Vector3 maxScale;
        private float duration;
        private float elapsed;

        public void Configure(Vector3 minScale, Vector3 maxScale, float duration)
        {
            this.minScale = minScale;
            this.maxScale = maxScale;
            this.duration = Mathf.Max(0.01f, duration);
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI);
            CachedTransform.localScale = Vector3.Lerp(minScale, maxScale, pulse);
            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchRealmRuneFlashSpawner : PhantomWitchRuntimeComponentBase
    {
        private float radius;
        private float duration;
        private float elapsed;
        private float nextSpawnTime;

        public void Configure(float radius, float duration)
        {
            this.radius = radius;
            this.duration = duration;
            elapsed = 0f;
            ScheduleNextSpawn();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                Destroy(this);
                return;
            }

            if (elapsed < nextSpawnTime)
            {
                return;
            }

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 localPosition = new Vector3(Mathf.Cos(angle) * radius, UnityEngine.Random.Range(0.08f, 0.25f), Mathf.Sin(angle) * radius);
            GameObject rune = new GameObject("RealmRuneFlash");
            Transform runeTransform = rune.transform;
            runeTransform.SetParent(CachedTransform, false);
            runeTransform.localPosition = localPosition;
            runeTransform.localRotation = Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-20f, 20f));

            // 审查 VB-08：1–2 px 的发丝线在相机距离下只会闪烁，改 2 段、宽 0.06 m 并收尖。
            for (int i = 0; i < 2; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                Transform segmentTransform = segment.transform;
                segmentTransform.SetParent(runeTransform, false);
                LineRenderer line = segment.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = PhantomWitchFxRenderUtil.MinWorldLineWidth;
                line.widthCurve = PhantomWitchVfxRedesign.TaperedLineWidthCurve;
                line.sharedMaterial = PhantomWitchVfxRedesign.GetSharedLineMaterial();
                Color color = new Color(PhantomWitchConfig.SilverAshCore.r, PhantomWitchConfig.SilverAshCore.g, PhantomWitchConfig.SilverAshCore.b, 0.55f);
                line.startColor = color;
                line.endColor = new Color(color.r, color.g, color.b, 0.1f);
                float width = UnityEngine.Random.Range(0.09f, 0.18f);
                line.SetPosition(0, new Vector3(-width, 0f, 0f));
                line.SetPosition(1, new Vector3(0f, UnityEngine.Random.Range(0.03f, 0.08f), 0f));
                line.SetPosition(2, new Vector3(width * UnityEngine.Random.Range(0.45f, 1f), UnityEngine.Random.Range(-0.03f, 0.04f), 0f));
            }

            PhantomWitchFadeDestroy fade = rune.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(0.5f, 0.4f);
            Object.Destroy(rune, 0.5f);
            ScheduleNextSpawn();
        }

        private void ScheduleNextSpawn()
        {
            nextSpawnTime = elapsed + UnityEngine.Random.Range(1f, 2f);
        }
    }

    /// <summary>
    /// 特效点光：起爆一下再按寿命淡到 0，到点关灯（不硬切）。
    /// 2026-09-23 审查 VB-09：起爆从 ×1.5 / 范围 ×1.2 降到 ×1.2 / ×1.0，强度与范围的上限在
    /// <see cref="PhantomWitchVfxRedesign"/> 的建灯入口统一钳制，避免 10 m 半径的地面被冷紫光洗平。
    /// </summary>
    internal sealed class PhantomWitchLightPulse : MonoBehaviour
    {
        private const float BurstIntensityScale = 1.2f;

        private Light targetLight;
        private float maxIntensity;
        private float maxRange;
        private float duration;
        private float elapsed;

        public void Configure(float targetIntensity, float targetRange, float duration)
        {
            this.maxIntensity = targetIntensity;
            this.maxRange = targetRange;
            this.duration = Mathf.Max(0.01f, duration);
            this.elapsed = 0f;
            this.targetLight = GetComponent<Light>();
            if (this.targetLight != null)
            {
                this.targetLight.enabled = true;
                this.targetLight.intensity = targetIntensity * BurstIntensityScale;
                this.targetLight.range = targetRange;
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (targetLight != null)
            {
                float burst = Mathf.Exp(-t * 10f); // 起爆后迅速回落
                float fade = 1f - BossRushUI.SmoothStep(t); // 按寿命平滑淡到 0
                targetLight.intensity = maxIntensity * fade * (1f + burst * (BurstIntensityScale - 1f));
                targetLight.range = maxRange * (0.6f + 0.4f * fade);
            }

            if (t >= 1f)
            {
                if (targetLight != null)
                {
                    targetLight.intensity = 0f;
                    targetLight.enabled = false;
                }

                Destroy(this);
            }
        }
    }

    /// <summary>
    /// 幽灵女巫的地面预警（圆形领域 / 扇形挥砍共用）。2026-09-23 审查 VB-01 / VB-07：
    /// - 边界（外圈 / 扇形外沿）始终钉在判定半径与判定半角上，不收缩、不移动，只随蓄力变粗变亮；
    /// - 读时机的是填充：从圆心（扇形顶点）按时间线性长满，长满的那一刻就是结算；
    /// - 结算前最后 <see cref="FlashDuration"/> 秒外沿闪白加粗，随后 postFade 秒内淡出。
    /// 只改表现：半径、半角、时长都由调用方按判定值传入，这里不改任何玩法数值。
    /// 扇形可跟随 Boss → 目标方向（判定 `ResolveAttackForward` 在出手瞬间朝向目标，预警跟着转才是真话）。
    /// </summary>
    internal sealed class PhantomWitchTelegraphDriver : PhantomWitchRuntimeComponentBase
    {
        internal const float FlashDuration = 0.08f;
        private const float FlashWidthScale = 1.4f;

        private float chargeDuration = 0.5f;
        private float postFade = 0.15f;
        private float elapsed;
        private bool finished;

        private PhantomWitchFlatRingMesh outlineRing;
        private float outlineRingRadius;
        private PhantomWitchFlatPathMesh[] outlinePaths;
        private Vector3[][] outlinePathPoints;
        private Color outlineColor = Color.white;
        private float outlineAlphaStart = 0.55f;
        private float outlineAlphaEnd = 1f;
        private float outlineWidthStart = 0.2f;
        private float outlineWidthEnd = 0.4f;
        private Color flashColor = Color.white;
        private float lastOutlineWidth = -1f;

        private Transform fillTransform;
        private Renderer fillRenderer;
        private MaterialPropertyBlock fillBlock;
        private Vector3 fillFullScale = Vector3.one;
        private Color fillColor = Color.white;
        private float fillAlphaStart = 0.1f;
        private float fillAlphaEnd = 0.28f;

        private Transform trackOrigin;
        private Transform trackTarget;
        private float trackForwardOffset;
        private float trackHeight;
        private bool tracking;

        internal void Configure(float chargeDuration, float postFade)
        {
            this.chargeDuration = Mathf.Max(0.01f, chargeDuration);
            this.postFade = Mathf.Max(0.01f, postFade);
            elapsed = 0f;
            finished = false;
            lastOutlineWidth = -1f;
        }

        internal void SetRingOutline(PhantomWitchFlatRingMesh ring, float radius)
        {
            outlineRing = ring;
            outlineRingRadius = Mathf.Max(0.01f, radius);
        }

        internal void SetPathOutlines(PhantomWitchFlatPathMesh[] paths, Vector3[][] points)
        {
            outlinePaths = paths;
            outlinePathPoints = points;
        }

        internal void SetOutlineStyle(Color color, float alphaStart, float alphaEnd, float widthStart, float widthEnd, Color flash)
        {
            outlineColor = color;
            outlineAlphaStart = alphaStart;
            outlineAlphaEnd = alphaEnd;
            outlineWidthStart = Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, widthStart);
            outlineWidthEnd = Mathf.Max(outlineWidthStart, widthEnd);
            flashColor = flash;
        }

        internal void SetFill(Transform fill, Renderer renderer, Vector3 fullScale, Color color, float alphaStart, float alphaEnd)
        {
            fillTransform = fill;
            fillRenderer = renderer;
            fillBlock = new MaterialPropertyBlock();
            fillFullScale = fullScale;
            fillColor = color;
            fillAlphaStart = alphaStart;
            fillAlphaEnd = alphaEnd;
        }

        /// <summary>
        /// 跟随 origin → target 的水平方向。高度锁在创建时的地面 + height：Boss 蓄力时会下沉（RunBodySinkWindup），
        /// 预警跟着沉会半截埋进地里。
        /// </summary>
        internal void SetTracking(Transform origin, Transform target, float forwardOffset, float height)
        {
            trackOrigin = origin;
            trackTarget = target;
            trackForwardOffset = forwardOffset;
            trackHeight = origin != null ? origin.position.y + height : height;
            tracking = origin != null;
        }

        private void Start()
        {
            Apply();
        }

        private void LateUpdate()
        {
            if (finished)
            {
                return;
            }

            elapsed += Time.deltaTime;
            Apply();
            if (elapsed >= chargeDuration + postFade)
            {
                finished = true;
            }
        }

        private void Apply()
        {
            if (tracking)
            {
                UpdateTracking();
            }

            float charge = Mathf.Clamp01(elapsed / chargeDuration);
            float visibility = 1f;
            bool flash;
            if (elapsed < chargeDuration)
            {
                flash = chargeDuration - elapsed <= FlashDuration;
            }
            else
            {
                flash = true;
                visibility = 1f - BossRushUI.SmoothStep((elapsed - chargeDuration) / postFade);
            }

            float width = Mathf.Lerp(outlineWidthStart, outlineWidthEnd, charge) * (flash ? FlashWidthScale : 1f);
            Color outline = flash ? flashColor : outlineColor;
            outline.a = (flash ? 1f : Mathf.Lerp(outlineAlphaStart, outlineAlphaEnd, charge)) * visibility;
            ApplyOutline(width, outline);

            if (fillTransform != null)
            {
                // 线性长满：玩家读到的「还差多少」与剩余时间成正比。
                float grow = Mathf.Max(0.001f, charge);
                fillTransform.localScale = new Vector3(fillFullScale.x * grow, fillFullScale.y, fillFullScale.z * grow);
                Color fill = fillColor;
                fill.a = Mathf.Lerp(fillAlphaStart, fillAlphaEnd, charge) * visibility;
                PhantomWitchFxRenderUtil.SetRendererColor(fillRenderer, fillBlock, fill);
            }
        }

        private void ApplyOutline(float width, Color color)
        {
            bool rebuild = Mathf.Abs(width - lastOutlineWidth) > 0.004f;
            if (rebuild)
            {
                lastOutlineWidth = width;
            }

            if (outlineRing != null)
            {
                if (rebuild)
                {
                    outlineRing.SetShape(outlineRingRadius, width);
                }

                outlineRing.SetColor(color);
            }

            if (outlinePaths == null)
            {
                return;
            }

            for (int i = 0; i < outlinePaths.Length; i++)
            {
                PhantomWitchFlatPathMesh path = outlinePaths[i];
                if (path == null)
                {
                    continue;
                }

                if (rebuild && outlinePathPoints != null && i < outlinePathPoints.Length)
                {
                    path.SetPath(outlinePathPoints[i], width);
                }

                path.SetColor(color);
            }
        }

        private void UpdateTracking()
        {
            if (trackOrigin == null)
            {
                return;
            }

            Vector3 originPosition = trackOrigin.position;
            Vector3 forward = trackOrigin.forward;
            if (trackTarget != null)
            {
                Vector3 toTarget = trackTarget.position - originPosition;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    forward = toTarget;
                }
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Transform self = CachedTransform;
            Vector3 apex = originPosition + forward * trackForwardOffset;
            apex.y = trackHeight;
            self.position = apex;
            self.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
