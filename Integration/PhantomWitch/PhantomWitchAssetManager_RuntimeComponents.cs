using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class PhantomWitchFxRuntime
    {
        private static int activeRootCount = 0;
        private static Camera cachedMainCamera = null;
        private static int cachedMainCameraFrame = -1;
        internal static bool HasActiveRoots => activeRootCount > 0;

        internal static Camera CurrentCamera
        {
            get
            {
                // Collapse repeated per-frame Camera.main lookups across PhantomWitch FX.
                if (cachedMainCameraFrame != Time.frameCount || cachedMainCamera == null)
                {
                    cachedMainCamera = Camera.main;
                    cachedMainCameraFrame = Time.frameCount;
                }

                return cachedMainCamera;
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
            Color color = block.GetColor(ColorPropertyId);
            if (Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f))
            {
                color = block.GetColor(TintColorPropertyId);
            }

            if (Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f))
            {
                color = block.GetColor(BaseColorPropertyId);
            }

            if (Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f))
            {
                Material shared = renderer.sharedMaterial;
                if (shared != null && shared.HasProperty(ColorPropertyId))
                {
                    color = shared.color;
                }
                else if (shared != null && shared.HasProperty(TintColorPropertyId))
                {
                    color = shared.GetColor(TintColorPropertyId);
                }
                else if (shared != null && shared.HasProperty(BaseColorPropertyId))
                {
                    color = shared.GetColor(BaseColorPropertyId);
                }
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
            Material shared = renderer.sharedMaterial;
            if (shared != null && shared.HasProperty(ColorPropertyId))
            {
                block.SetColor(ColorPropertyId, color);
            }
            else if (shared != null && shared.HasProperty(TintColorPropertyId))
            {
                block.SetColor(TintColorPropertyId, color);
            }
            else
            {
                block.SetColor(BaseColorPropertyId, color);
            }
            renderer.SetPropertyBlock(block);
        }
    }

    internal sealed class PhantomWitchFadeDestroy : MonoBehaviour
    {
        private float duration = 0.3f;
        private float fadeDuration = 0.3f;
        private float elapsed;
        private bool initialized;

        private LineRenderer[] lineRenderers = new LineRenderer[0];
        private Renderer[] renderers = new Renderer[0];
        private MaterialPropertyBlock[] rendererBlocks = new MaterialPropertyBlock[0];
        private Color[] lineStartColors = new Color[0];
        private Color[] lineEndColors = new Color[0];
        private Color[] rendererBaseColors = new Color[0];

        public void Configure(float duration)
        {
            Configure(duration, duration);
        }

        public void Configure(float duration, float fadeDuration)
        {
            this.duration = Mathf.Max(0.01f, duration);
            this.fadeDuration = Mathf.Clamp(fadeDuration <= 0f ? this.duration : fadeDuration, 0.01f, this.duration);
            this.elapsed = 0f;
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
                ApplyAlpha(1f);
            }
        }

        private void CacheTargets()
        {
            lineRenderers = GetComponentsInChildren<LineRenderer>(true);
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
                if (renderer == null || renderer is LineRenderer || renderer is ParticleSystemRenderer)
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
            elapsed += Time.deltaTime;

            float fadeStart = duration - fadeDuration;
            if (elapsed < fadeStart)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - fadeStart) / fadeDuration);
            ApplyAlpha(1f - t);

            if (elapsed >= duration)
            {
                Destroy(gameObject);
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
            BuildPathMesh(mesh, points, width);
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

        private static void BuildPathMesh(Mesh mesh, IList<Vector3> points, float width)
        {
            if (mesh == null)
            {
                return;
            }

            if (points == null || points.Count < 2)
            {
                mesh.Clear();
                return;
            }

            float halfWidth = Mathf.Max(0.001f, width) * 0.5f;
            int pointCount = points.Count;
            Vector3[] vertices = new Vector3[pointCount * 2];
            Vector2[] uv = new Vector2[pointCount * 2];
            int[] triangles = new int[(pointCount - 1) * 6];

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

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
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
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.04f, startWidth * 0.3f), t);

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
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
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
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.02f, startWidth * 0.4f), t);

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
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
            this.elapsed = 0f;
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
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.03f, startWidth * 0.35f), t);

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
                Vector3[] points = new Vector3[segments + 1];
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
