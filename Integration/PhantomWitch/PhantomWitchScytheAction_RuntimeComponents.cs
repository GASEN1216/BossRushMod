using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>符文环绕 Y 轴匀速旋转。</summary>
    internal sealed class PhantomWitchRingSpin : MonoBehaviour
    {
        public float rotationSpeed = 20f;
        private Transform cachedTransform;

        private void Awake()
        {
            cachedTransform = transform;
        }

        private void Update()
        {
            Transform spinTransform = cachedTransform;
            if (spinTransform == null)
            {
                spinTransform = transform;
                cachedTransform = spinTransform;
            }

            spinTransform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }

    internal sealed class PhantomWitchRuntimeMesh : MonoBehaviour
    {
        private Mesh mesh;

        internal void SetMesh(Mesh mesh)
        {
            this.mesh = mesh;
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

    internal sealed class PhantomWitchFlatRingMesh : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Mesh mesh;
        private int segments;

        internal void Configure(int segments, float radius, float width, Material material, Color color)
        {
            this.segments = Mathf.Max(3, segments);
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
                mesh.name = "PW_CurseRealm_RingMesh";
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

            SetShape(radius, width);
            SetColor(color);
        }

        internal void SetShape(float radius, float width)
        {
            if (mesh == null)
            {
                return;
            }

            PhantomWitchCurseRealmVisual.BuildRingMesh(mesh, radius, width, segments);
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

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
        }
    }

    /// <summary>LineRenderer 宽度/颜色呼吸，给环增加"能量流动"感。</summary>
    internal sealed class PhantomWitchRingPulse : MonoBehaviour
    {
        private Renderer targetRenderer;
        private MaterialPropertyBlock propertyBlock;
        private float baseScale;
        private float amplitude;
        private float frequency;
        private Color baseColor;
        private Vector3 initialScale;
        private float lastPulseAlpha;
        private Transform cachedTransform;

        private void Awake()
        {
            cachedTransform = transform;
        }

        public void Configure(float baseScale, float amplitude, float frequency, Color baseColor)
        {
            this.targetRenderer = GetComponent<Renderer>();
            this.propertyBlock = new MaterialPropertyBlock();
            this.baseScale = baseScale;
            this.amplitude = amplitude;
            this.frequency = frequency;
            this.baseColor = baseColor;
            Transform pulseTransform = cachedTransform;
            if (pulseTransform == null)
            {
                pulseTransform = transform;
                cachedTransform = pulseTransform;
            }
            this.initialScale = pulseTransform.localScale;
            this.lastPulseAlpha = Mathf.Max(0.0001f, baseColor.a);
        }

        private void Update()
        {
            if (targetRenderer == null)
            {
                return;
            }

            float s = 0.5f + 0.5f * Mathf.Sin(Time.time * frequency * Mathf.PI * 2f);
            float factor = baseScale + amplitude * s;
            Transform pulseTransform = cachedTransform;
            if (pulseTransform == null)
            {
                pulseTransform = transform;
                cachedTransform = pulseTransform;
            }
            pulseTransform.localScale = initialScale * factor;

            Color currentColor = PhantomWitchFxRenderUtil.GetRendererColor(targetRenderer, propertyBlock);
            float fadeFactor = Mathf.Clamp01(currentColor.a / Mathf.Max(0.0001f, lastPulseAlpha));
            float pulseAlpha = Mathf.Lerp(baseColor.a * 0.7f, baseColor.a, s);
            Color c = baseColor;
            c.a = pulseAlpha * fadeFactor;
            lastPulseAlpha = Mathf.Max(0.0001f, pulseAlpha);
            PhantomWitchFxRenderUtil.SetRendererColor(targetRenderer, propertyBlock, c);
        }
    }

    /// <summary>开场冲击波：半径 0→target，持续很短，自毁。</summary>
    internal sealed class PhantomWitchShockwaveAnimation : MonoBehaviour
    {
        private PhantomWitchFlatRingMesh ringMesh;
        private float targetRadius;
        private float duration;
        private Color baseColor;
        private float elapsed;

        public void Configure(float targetRadius, float duration, Color color)
        {
            this.ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.targetRadius = targetRadius;
            this.duration = duration;
            this.baseColor = color;
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (ringMesh == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // 开场快、尾端收（easeOutQuad）
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0f, targetRadius, eased);
            float width = Mathf.Lerp(0.28f, 0.06f, t);
            ringMesh.SetShape(radius, width);

            Color c = baseColor;
            c.a = baseColor.a * (1f - t);
            ringMesh.SetColor(c);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// 中心光晕：正弦缩放 + 透明度呼吸。它每帧写满 alpha，会盖掉领域淡出，所以由
    /// <see cref="PhantomWitchCurseRealmFader"/> 通过 <see cref="FadeMultiplier"/> 把淡入淡出乘进来（审查 VB-05）。
    /// </summary>
    internal sealed class PhantomWitchCorePulse : MonoBehaviour
    {
        internal float FadeMultiplier = 1f;

        private Material material;
        private Renderer targetRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Color baseColor;
        private float baseScale;
        private float frequency;
        private Vector3 initialScale;
        private Transform cachedTransform;

        private void Awake()
        {
            cachedTransform = transform;
        }

        public void Configure(Material material, Color color, float baseScale, float frequency)
        {
            this.material = material;
            this.baseColor = color;
            this.baseScale = baseScale;
            this.frequency = frequency;
            Transform pulseTransform = cachedTransform;
            if (pulseTransform == null)
            {
                pulseTransform = transform;
                cachedTransform = pulseTransform;
            }
            this.initialScale = pulseTransform.localScale;
        }

        public void Configure(Renderer renderer, Color color, float baseScale, float frequency)
        {
            this.material = null;
            this.targetRenderer = renderer;
            this.propertyBlock = new MaterialPropertyBlock();
            this.baseColor = color;
            this.baseScale = baseScale;
            this.frequency = frequency;
            Transform pulseTransform = cachedTransform;
            if (pulseTransform == null)
            {
                pulseTransform = transform;
                cachedTransform = pulseTransform;
            }
            this.initialScale = pulseTransform.localScale;
        }

        private void Update()
        {
            float s = 0.5f + 0.5f * Mathf.Sin(Time.time * frequency * Mathf.PI * 2f);
            float factor = Mathf.Lerp(0.8f, 1.15f, s);
            Transform pulseTransform = cachedTransform;
            if (pulseTransform == null)
            {
                pulseTransform = transform;
                cachedTransform = pulseTransform;
            }
            pulseTransform.localScale = initialScale * factor;

            if (material != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a * 0.55f, baseColor.a, s) * FadeMultiplier;
                material.color = c;
            }
            else if (targetRenderer != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a * 0.55f, baseColor.a, s) * FadeMultiplier;
                PhantomWitchFxRenderUtil.SetRendererColor(targetRenderer, propertyBlock, c);
            }
        }
    }

    /// <summary>
    /// 领域开头 0.2 s 淡入、结束前 0.7 s 让所有层淡出，避免视觉骤然出现 / 消失。
    /// 2026-09-23 审查 VB-05：粒子渲染器也按同一 alpha 淡出（此前跳过，黑烟 / 亡魂在销毁那一帧整片消失），
    /// 淡出过半停止发射；中心光晕的呼吸改为乘上淡入淡出系数，不再每帧写满 alpha 盖掉淡出。
    /// </summary>
    internal sealed class PhantomWitchCurseRealmFader : MonoBehaviour
    {
        private const float FadeInDuration = 0.2f;
        private const float FadeOutDuration = 0.7f;

        private float totalDuration;
        private float elapsed;
        private bool emissionStopped;
        private LineRenderer[] lines;
        private Renderer[] otherRenderers;
        private MaterialPropertyBlock[] otherPropertyBlocks;
        private Color[] lineStartColors;
        private Color[] lineEndColors;
        private Color[] otherBaseColors;
        private ParticleSystem[] particleSystems;
        private PhantomWitchCorePulse[] corePulses;

        internal void Initialize(float duration)
        {
            totalDuration = Mathf.Max(0.05f, duration);
            elapsed = 0f;
            emissionStopped = false;
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            corePulses = GetComponentsInChildren<PhantomWitchCorePulse>(true);

            lines = GetComponentsInChildren<LineRenderer>(true);
            lineStartColors = new Color[lines.Length];
            lineEndColors = new Color[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                {
                    lineStartColors[i] = lines[i].startColor;
                    lineEndColors[i] = lines[i].endColor;
                }
            }

            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            List<Renderer> rendererList = new List<Renderer>(8);
            List<MaterialPropertyBlock> blockList = new List<MaterialPropertyBlock>(8);
            List<Color> colorList = new List<Color>(8);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null || r is LineRenderer)
                {
                    continue;
                }

                // 中心光晕由 CorePulse 自己写颜色（乘淡入淡出系数）；开场冲击波 0.35 s 内自己淡完。这里都不抢写。
                if (r.GetComponent<PhantomWitchCorePulse>() != null || r.GetComponent<PhantomWitchShockwaveAnimation>() != null)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rendererList.Add(r);
                blockList.Add(block);
                colorList.Add(PhantomWitchFxRenderUtil.GetRendererColor(r, block));
            }
            otherRenderers = rendererList.ToArray();
            otherPropertyBlocks = blockList.ToArray();
            otherBaseColors = colorList.ToArray();

            ApplyAlpha(0f);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float remaining = totalDuration - elapsed;
            if (remaining >= FadeOutDuration && elapsed >= FadeInDuration)
            {
                return;
            }

            float fadeIn = BossRushUI.SmoothStep(elapsed / FadeInDuration);
            float fadeOut = BossRushUI.SmoothStep(remaining / FadeOutDuration);
            float alpha = Mathf.Min(fadeIn, fadeOut);
            ApplyAlpha(alpha);

            if (!emissionStopped && remaining < FadeOutDuration * 0.5f)
            {
                emissionStopped = true;
                if (particleSystems != null)
                {
                    for (int i = 0; i < particleSystems.Length; i++)
                    {
                        if (particleSystems[i] != null)
                        {
                            particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                        }
                    }
                }
            }
        }

        private void ApplyAlpha(float alpha)
        {
            if (corePulses != null)
            {
                for (int i = 0; i < corePulses.Length; i++)
                {
                    if (corePulses[i] != null)
                    {
                        corePulses[i].FadeMultiplier = alpha;
                    }
                }
            }

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    LineRenderer lr = lines[i];
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
            }

            if (otherRenderers != null)
            {
                for (int i = 0; i < otherRenderers.Length; i++)
                {
                    Renderer renderer = otherRenderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }
                    Color c = otherBaseColors[i];
                    c.a *= alpha;
                    PhantomWitchFxRenderUtil.SetRendererColor(renderer, otherPropertyBlocks[i], c);
                }
            }
        }

        private void OnDestroy()
        {
            otherRenderers = null;
            otherPropertyBlocks = null;
            lines = null;
        }
    }
}
