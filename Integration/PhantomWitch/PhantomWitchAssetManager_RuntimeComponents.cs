using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class PhantomWitchFxRuntime
    {
        private static int activeRootCount = 0;
        private static Camera cachedMainCamera = null;
        private static Transform cachedMainCameraTransform = null;
        private static int cachedMainCameraFrame = -1;
        internal static bool HasActiveRoots => activeRootCount > 0;

        internal static Camera CurrentCamera
        {
            get
            {
                RefreshCurrentCamera();
                return cachedMainCamera;
            }
        }

        internal static Transform CurrentCameraTransform
        {
            get
            {
                RefreshCurrentCamera();
                return cachedMainCameraTransform;
            }
        }

        private static void RefreshCurrentCamera()
        {
            // Collapse repeated per-frame Camera.main and camera.transform lookups across PhantomWitch FX.
            if (cachedMainCameraFrame != Time.frameCount || cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main;
                cachedMainCameraTransform = cachedMainCamera != null ? cachedMainCamera.transform : null;
                cachedMainCameraFrame = Time.frameCount;
            }
        }

        internal static PhantomWitchFxDetailLevel CurrentDetailLevel
        {
            get
            {
                return PhantomWitchPerformancePolicy.ResolveFxDetailLevel(
                    activeRootCount,
                    PhantomWitchConfig.FxReducedActiveRootThreshold,
                    PhantomWitchConfig.FxMinimalActiveRootThreshold);
            }
        }

        internal static bool ShouldSkipEffect(PhantomWitchFxEffectImportance importance)
        {
            return PhantomWitchPerformancePolicy.ShouldSkipEffect(
                CurrentDetailLevel,
                activeRootCount,
                PhantomWitchConfig.FxReducedActiveRootThreshold,
                PhantomWitchConfig.FxMinimalActiveRootThreshold,
                importance);
        }

        internal static void RegisterEffectRoot(GameObject root)
        {
            if (root == null || root.GetComponent<PhantomWitchFxRootTracker>() != null)
            {
                return;
            }

            root.AddComponent<PhantomWitchFxRootTracker>();
        }

        internal static void AdjustActiveRootCount(int delta)
        {
            activeRootCount += delta;
            if (activeRootCount < 0)
            {
                activeRootCount = 0;
            }

            PhantomWitchAssetManager.TryFinalizePendingCacheCleanup();
        }

        internal static void Reset()
        {
            activeRootCount = 0;
            cachedMainCamera = null;
            cachedMainCameraTransform = null;
            cachedMainCameraFrame = -1;
        }
    }

    internal sealed class PhantomWitchFxRootTracker : MonoBehaviour
    {
        private bool registered = false;

        private void OnEnable()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PhantomWitchFxRuntime.AdjustActiveRootCount(1);
        }

        private void OnDisable()
        {
            if (!registered)
            {
                return;
            }

            registered = false;
            PhantomWitchFxRuntime.AdjustActiveRootCount(-1);
        }

        private void OnDestroy()
        {
            if (registered)
            {
                registered = false;
                PhantomWitchFxRuntime.AdjustActiveRootCount(-1);
            }
        }
    }

    internal static class PhantomWitchFxRenderUtil
    {
        internal static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        internal static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
        internal static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// 世界空间线 / 地面带的最小宽度（米）。相机俯仰约 55°、臂长 45 m，1080p 下 0.1 m ≈ 7 px；
        /// 低于 0.06 m 的线只剩 1–2 px，会闪烁成噪点（审查 VB-08）。
        /// </summary>
        internal const float MinWorldLineWidth = 0.06f;

        /// <summary>
        /// 着色器真正读取的颜色属性。材质来自 <see cref="BossRushFxMaterials"/>：
        /// URP `Particles/Unlit` 读 `_BaseColor`（它另有一个废弃的隐藏 `_Color`，写进去不生效，所以 `_BaseColor` 必须排第一）；
        /// 兜底的 Legacy `Particles/Alpha Blended` 读 `_TintColor`。
        /// </summary>
        private static int ResolveColorPropertyId(Material shared)
        {
            if (shared == null)
            {
                return BaseColorPropertyId;
            }

            if (shared.HasProperty(BaseColorPropertyId))
            {
                return BaseColorPropertyId;
            }

            if (shared.HasProperty(TintColorPropertyId))
            {
                return TintColorPropertyId;
            }

            if (shared.HasProperty(ColorPropertyId))
            {
                return ColorPropertyId;
            }

            return BaseColorPropertyId;
        }

        private static bool IsUnset(Color color)
        {
            return Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f);
        }

        internal static Color GetRendererColor(Renderer renderer, MaterialPropertyBlock block)
        {
            if (renderer == null)
            {
                return Color.white;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(block);
            Material shared = renderer.sharedMaterial;
            int propertyId = ResolveColorPropertyId(shared);
            Color color = block.GetColor(propertyId);
            if (IsUnset(color) && shared != null && shared.HasProperty(propertyId))
            {
                color = shared.GetColor(propertyId);
            }

            // Legacy Particles 片元是 2 × 顶点色 × _TintColor：存的是半值，读回时还原成 1× 语义。
            if (propertyId == TintColorPropertyId)
            {
                color *= 2f;
            }

            return color;
        }

        internal static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            SetRendererColor(renderer, block, color);
        }

        internal static void SetRendererColor(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(block);
            int propertyId = ResolveColorPropertyId(renderer.sharedMaterial);
            block.SetColor(propertyId, propertyId == TintColorPropertyId ? color * 0.5f : color);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// 淡出后销毁。2026-09-23 审查 VB-05：此前跳过粒子渲染器、到点整棵 Destroy，星尘 / 魂雾在亮度峰值处一帧消失。
    /// 现在粒子渲染器也按同一 alpha 淡出（URP 粒子材质的 `_BaseColor` 乘在每颗粒子上），淡出过半时停止发射；
    /// 挂在对象池根上时不销毁根，交给 <see cref="PhantomWitchVfxRedesign.PhantomWitchVfxRecycler"/> 回收。
    /// </summary>
    internal sealed class PhantomWitchFadeDestroy : MonoBehaviour
    {
        private float duration = 0.3f;
        private float fadeDuration = 0.3f;
        private float elapsed;
        private bool initialized;
        private bool emissionStopped;
        private bool finished;

        private LineRenderer[] lineRenderers = new LineRenderer[0];
        private Renderer[] renderers = new Renderer[0];
        private MaterialPropertyBlock[] rendererBlocks = new MaterialPropertyBlock[0];
        private Color[] lineStartColors = new Color[0];
        private Color[] lineEndColors = new Color[0];
        private Color[] rendererBaseColors = new Color[0];
        private ParticleSystem[] particleSystems = new ParticleSystem[0];

        public void Configure(float duration)
        {
            Configure(duration, duration);
        }

        public void Configure(float duration, float fadeDuration)
        {
            this.duration = Mathf.Max(0.01f, duration);
            this.fadeDuration = Mathf.Clamp(fadeDuration <= 0f ? this.duration : fadeDuration, 0.01f, this.duration);
            this.elapsed = 0f;
            this.emissionStopped = false;
            this.finished = false;
            CacheTargets();
            initialized = true;
        }

        private void Awake()
        {
            if (!initialized)
            {
                Configure(duration, fadeDuration);
            }
        }

        private void OnEnable()
        {
            if (initialized)
            {
                elapsed = 0f;
                emissionStopped = false;
                finished = false;
                ApplyAlpha(1f);
            }
        }

        /// <summary>
        /// 每个渲染器 / 粒子只归离它最近的那个 FadeDestroy 管（子物体自带淡出时，根上的淡出不再抢写它的颜色，
        /// 否则两边在重叠的几帧里交替写 alpha 会闪）。预警圈 / 扇形由 PhantomWitchTelegraphDriver 驱动，这里跳过。
        /// </summary>
        private bool Owns(Component component)
        {
            if (component == null)
            {
                return false;
            }

            if (component.GetComponentInParent<PhantomWitchTelegraphDriver>() != null)
            {
                return false;
            }

            return component.GetComponentInParent<PhantomWitchFadeDestroy>() == this;
        }

        private void CacheTargets()
        {
            ParticleSystem[] allParticles = GetComponentsInChildren<ParticleSystem>(true);
            List<ParticleSystem> particleList = new List<ParticleSystem>(allParticles.Length);
            for (int i = 0; i < allParticles.Length; i++)
            {
                if (Owns(allParticles[i]))
                {
                    particleList.Add(allParticles[i]);
                }
            }

            particleSystems = particleList.ToArray();

            LineRenderer[] allLines = GetComponentsInChildren<LineRenderer>(true);
            List<LineRenderer> lineList = new List<LineRenderer>(allLines.Length);
            for (int i = 0; i < allLines.Length; i++)
            {
                if (Owns(allLines[i]))
                {
                    lineList.Add(allLines[i]);
                }
            }

            lineRenderers = lineList.ToArray();
            lineStartColors = new Color[lineRenderers.Length];
            lineEndColors = new Color[lineRenderers.Length];

            for (int i = 0; i < lineRenderers.Length; i++)
            {
                if (lineRenderers[i] == null)
                {
                    continue;
                }

                lineStartColors[i] = lineRenderers[i].startColor;
                lineEndColors[i] = lineRenderers[i].endColor;
            }

            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            List<Renderer> rendererList = new List<Renderer>(4);
            List<MaterialPropertyBlock> blockList = new List<MaterialPropertyBlock>(4);
            List<Color> colorList = new List<Color>(4);

            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer renderer = allRenderers[i];
                if (renderer == null || renderer is LineRenderer)
                {
                    continue;
                }

                if (!Owns(renderer))
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rendererList.Add(renderer);
                blockList.Add(block);
                colorList.Add(PhantomWitchFxRenderUtil.GetRendererColor(renderer, block));
            }

            renderers = rendererList.ToArray();
            rendererBlocks = blockList.ToArray();
            rendererBaseColors = colorList.ToArray();
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float fadeStart = duration - fadeDuration;
            if (elapsed < fadeStart)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - fadeStart) / fadeDuration);
            ApplyAlpha(1f - BossRushUI.SmoothStep(t));

            // 淡出过半再停发射：t=0 的 burst 一定已经发出，新粒子也不会在 alpha 接近 0 时白白生成。
            if (!emissionStopped && t >= 0.5f)
            {
                emissionStopped = true;
                StopEmission();
            }

            if (elapsed >= duration)
            {
                finished = true;
                // 对象池根由回收器负责清子物体并入池；这里销毁根会让池子永远拿不到可复用的根。
                if (GetComponent<PhantomWitchVfxRedesign.PhantomWitchVfxRecycler>() != null)
                {
                    return;
                }

                Destroy(gameObject);
            }
        }

        private void StopEmission()
        {
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps != null)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private void ApplyAlpha(float alpha)
        {
            for (int i = 0; i < lineRenderers.Length; i++)
            {
                LineRenderer lr = lineRenderers[i];
                if (lr == null)
                {
                    continue;
                }

                Color start = lineStartColors[i];
                Color end = lineEndColors[i];
                start.a *= alpha;
                end.a *= alpha;
                lr.startColor = start;
                lr.endColor = end;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color c = rendererBaseColors[i];
                c.a *= alpha;
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, rendererBlocks[i], c);
            }
        }
    }

    internal sealed class PhantomWitchFlatPathMesh : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Mesh mesh;
        private Vector3[] verticesBuffer;
        private Vector2[] uvBuffer;
        private int[] trianglesBuffer;

        internal int PointCount { get; private set; }

        internal void Configure(IList<Vector3> points, float width, Material material, Color color)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "PW_FlatPathMesh";
            }

            meshFilter.sharedMesh = mesh;
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            SetPath(points, width);
            SetColor(color);
        }

        internal void SetPath(IList<Vector3> points, float width)
        {
            PointCount = points != null ? points.Count : 0;
            BuildPathMesh(points, width);
        }

        internal void SetColor(Color color)
        {
            if (meshRenderer == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, propertyBlock, color);
        }

        private void BuildPathMesh(IList<Vector3> points, float width)
        {
            Mesh targetMesh = mesh;
            if (targetMesh == null)
            {
                return;
            }

            if (points == null || points.Count < 2)
            {
                targetMesh.Clear();
                return;
            }

            float halfWidth = Mathf.Max(0.001f, width) * 0.5f;
            int pointCount = points.Count;
            int vertexCount = pointCount * 2;
            int triangleCount = (pointCount - 1) * 6;
            if (verticesBuffer == null || verticesBuffer.Length != vertexCount || uvBuffer == null || uvBuffer.Length != vertexCount)
            {
                verticesBuffer = new Vector3[vertexCount];
                uvBuffer = new Vector2[vertexCount];
            }
            if (trianglesBuffer == null || trianglesBuffer.Length != triangleCount)
            {
                trianglesBuffer = new int[triangleCount];
            }

            Vector3[] vertices = verticesBuffer;
            Vector2[] uv = uvBuffer;
            int[] triangles = trianglesBuffer;

            for (int i = 0; i < pointCount; i++)
            {
                Vector3 tangent;
                if (i == 0)
                {
                    tangent = points[1] - points[0];
                }
                else if (i == pointCount - 1)
                {
                    tangent = points[pointCount - 1] - points[pointCount - 2];
                }
                else
                {
                    tangent = points[i + 1] - points[i - 1];
                }

                if (tangent.sqrMagnitude < 0.0001f)
                {
                    tangent = Vector3.right;
                }

                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized) * halfWidth;
                int vertexIndex = i * 2;
                vertices[vertexIndex] = points[i] - side;
                vertices[vertexIndex + 1] = points[i] + side;

                float u = pointCount > 1 ? (float)i / (pointCount - 1) : 0f;
                uv[vertexIndex] = new Vector2(u, 0f);
                uv[vertexIndex + 1] = new Vector2(u, 1f);

                if (i >= pointCount - 1)
                {
                    continue;
                }

                int triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 1;
                triangles[triangleIndex + 2] = vertexIndex + 2;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + 3;
                triangles[triangleIndex + 5] = vertexIndex + 2;
            }

            targetMesh.Clear();
            targetMesh.vertices = vertices;
            targetMesh.uv = uv;
            targetMesh.triangles = triangles;
            targetMesh.RecalculateNormals();
            targetMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
        }
    }

    internal sealed class PhantomWitchExpandRing : MonoBehaviour
    {
        public float delay = 0f;

        private LineRenderer lineRenderer;
        private PhantomWitchFlatRingMesh ringMesh;
        private float targetRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;

        public void Configure(float targetRadius, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.targetRadius = Mathf.Max(0f, targetRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, lineRenderer != null ? lineRenderer.widthMultiplier : width);
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null && ringMesh == null)
            {
                Destroy(this);
                return;
            }

            if (lineRenderer != null && !positionsNormalized)
            {
                int count = lineRenderer.positionCount;
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)i / count * Mathf.PI * 2f;
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            if (elapsed < delay)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - delay) / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0f, targetRadius, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, startWidth * 0.3f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                ringMesh.SetShape(radius, width);
                ringMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchShrinkRing : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private PhantomWitchFlatRingMesh ringMesh;
        private float startRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;

        public void Configure(float startRadius, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.startRadius = Mathf.Max(0f, startRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, lineRenderer != null ? lineRenderer.widthMultiplier : width);
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null && ringMesh == null)
            {
                Destroy(this);
                return;
            }

            if (lineRenderer != null && !positionsNormalized)
            {
                int count = lineRenderer.positionCount;
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)i / count * Mathf.PI * 2f;
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t;
            float radius = Mathf.Lerp(startRadius, 0f, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, startWidth * 0.4f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                ringMesh.SetShape(radius, width);
                ringMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchExpandArc : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private PhantomWitchFlatPathMesh pathMesh;
        private float startRadius;
        private float targetRadius;
        private float halfAngle;
        private Vector3 forward;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;
        private Vector3[] pathPointsBuffer;

        public void Configure(float startRadius, float targetRadius, float halfAngle, Vector3 forward, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            pathMesh = GetComponent<PhantomWitchFlatPathMesh>();
            this.startRadius = startRadius;
            this.targetRadius = targetRadius;
            this.halfAngle = halfAngle;
            this.forward = forward;
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, lineRenderer != null ? lineRenderer.widthMultiplier : width);
            this.elapsed = 0f;
            this.pathPointsBuffer = null;
        }

        private void Update()
        {
            if (lineRenderer == null && pathMesh == null)
            {
                Destroy(this);
                return;
            }

            Vector3 localForward = forward;
            localForward.y = 0f;
            if (localForward.sqrMagnitude < 0.01f)
            {
                localForward = Vector3.forward;
            }
            localForward.Normalize();

            if (lineRenderer != null && !positionsNormalized)
            {
                float baseAngle = Mathf.Atan2(localForward.x, localForward.z);
                float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
                float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
                int segments = Mathf.Max(1, lineRenderer.positionCount - 1);

                for (int i = 0; i <= segments; i++)
                {
                    float segmentT = (float)i / segments;
                    float angle = Mathf.Lerp(startAngle, endAngle, segmentT);
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(startRadius, targetRadius, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, startWidth * 0.35f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                int segments = Mathf.Max(1, pathMesh.PointCount - 1);
                float baseAngle = Mathf.Atan2(localForward.x, localForward.z);
                float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
                float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
                int pointCount = segments + 1;
                Vector3[] points = pathPointsBuffer;
                if (points == null || points.Length != pointCount)
                {
                    points = new Vector3[pointCount];
                    pathPointsBuffer = points;
                }
                for (int i = 0; i <= segments; i++)
                {
                    float segmentT = (float)i / segments;
                    float angle = Mathf.Lerp(startAngle, endAngle, segmentT);
                    points[i] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                }

                pathMesh.SetPath(points, width);
                pathMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
