using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public sealed class PhantomWitchAmbientPresence : MonoBehaviour
    {
        private const float DefaultVeilEmissionRate = 4f;
        private const float DefaultGroundMistEmissionRate = 3f;
        private const float CloseCameraDistance = 5f;

        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");

        private Transform bossBody;
        private PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxDetailLevel.Full;
        private PhantomWitchPhase phase = PhantomWitchPhase.Phase1;
        private bool paused;
        private bool initialized;

        private ParticleSystem veilParticles;
        private ParticleSystem groundMistParticles;
        private MeshRenderer coldHaloRenderer;
        private Transform coldHaloTransform;
        private MeshRenderer heartbeatRenderer;
        private Transform heartbeatTransform;
        private Material haloMaterial;
        private Material lineMaterial;
        private Texture2D haloTexture;
        private MaterialPropertyBlock coldHaloBlock;
        private MaterialPropertyBlock heartbeatBlock;
        private float nextRuneFlashTime;
        private float nextHeartbeatTime;
        private float heartbeatVisibleUntil = -1f;
        private readonly List<GameObject> transientEffects = new List<GameObject>(8);

        public void Initialize(Transform bossBody)
        {
            this.bossBody = bossBody != null ? bossBody : transform;
            EnsureInitialized();
            SetDetailLevel(PhantomWitchFxRuntime.CurrentDetailLevel);
            SetPhase(phase);
            Resume();
        }

        internal void SetDetailLevel(PhantomWitchFxDetailLevel level)
        {
            detailLevel = level;
            EnsureInitialized();
            RefreshLayerState();
        }

        public void SetPhase(PhantomWitchPhase phase)
        {
            this.phase = phase;
        }

        public void Pause()
        {
            paused = true;
            heartbeatVisibleUntil = -1f;
            RefreshLayerState();
            HideHeartbeatFlash();
            HideColdHalo();
        }

        public void Resume()
        {
            paused = false;
            EnsureInitialized();
            ScheduleRuneFlash();
            ScheduleHeartbeat();
            RefreshLayerState();
        }

        private void Awake()
        {
            if (!initialized)
            {
                Initialize(transform);
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            PhantomWitchFxDetailLevel runtimeDetailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
            if (runtimeDetailLevel != detailLevel)
            {
                detailLevel = runtimeDetailLevel;
                RefreshLayerState();
            }

            UpdateColdHalo();
            UpdateHeartbeat();
            UpdateRuneFlash();
        }

        private void OnDestroy()
        {
            CleanupTransientEffects();
            CleanupOwnedObjects();

            if (haloMaterial != null)
            {
                Destroy(haloMaterial);
                haloMaterial = null;
            }

            if (lineMaterial != null)
            {
                Destroy(lineMaterial);
                lineMaterial = null;
            }

            if (haloTexture != null)
            {
                Destroy(haloTexture);
                haloTexture = null;
            }
        }

        private void CleanupOwnedObjects()
        {
            if (veilParticles != null)
            {
                try
                {
                    Destroy(veilParticles.gameObject);
                }
                catch
                {
                }
                veilParticles = null;
            }

            if (groundMistParticles != null)
            {
                try
                {
                    Destroy(groundMistParticles.gameObject);
                }
                catch
                {
                }
                groundMistParticles = null;
            }

            if (coldHaloTransform != null)
            {
                try
                {
                    Destroy(coldHaloTransform.gameObject);
                }
                catch
                {
                }
                coldHaloTransform = null;
                coldHaloRenderer = null;
            }

            if (heartbeatTransform != null)
            {
                try
                {
                    Destroy(heartbeatTransform.gameObject);
                }
                catch
                {
                }
                heartbeatTransform = null;
                heartbeatRenderer = null;
            }
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            coldHaloBlock = new MaterialPropertyBlock();
            heartbeatBlock = new MaterialPropertyBlock();

            CreateVeilParticles();
            CreateGroundMistParticles();
            CreateColdHalo();
            CreateHeartbeatFlash();
            ScheduleRuneFlash();
            ScheduleHeartbeat();

            initialized = true;
        }

        private void CreateVeilParticles()
        {
            GameObject veil = new GameObject("PW_AmbientVeil");
            veil.transform.SetParent(transform, false);
            veil.transform.localPosition = new Vector3(0f, 1.05f, 0f);

            veilParticles = veil.AddComponent<ParticleSystem>();
            var main = veilParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            var emission = veilParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = DefaultVeilEmissionRate;

            var shape = veilParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.radiusThickness = 0.35f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            var colorOverLifetime = veilParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient colorGradient = new Gradient();
            colorGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(PhantomWitchConfig.VioletVoidCore, 0f),
                    new GradientColorKey(PhantomWitchConfig.SilverAshDust, 0.55f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidDust, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.65f, 0.15f),
                    new GradientAlphaKey(0.45f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(colorGradient);

            var sizeOverLifetime = veilParticles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.2f, 1f),
                new Keyframe(0.7f, 0.7f),
                new Keyframe(1f, 0.1f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var velocityOverLifetime = veilParticles.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var noise = veilParticles.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;

            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(veilParticles);
            veilParticles.Play();
        }

        private void CreateGroundMistParticles()
        {
            GameObject mist = new GameObject("PW_AmbientGroundMist");
            mist.transform.SetParent(transform, false);
            mist.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            groundMistParticles = mist.AddComponent<ParticleSystem>();
            var main = groundMistParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.maxParticles = 16;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = groundMistParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = DefaultGroundMistEmissionRate;

            var shape = groundMistParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.75f;
            shape.radiusThickness = 1f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            var colorOverLifetime = groundMistParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient colorGradient = new Gradient();
            colorGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(PhantomWitchConfig.GhostBreathVeil, 0f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidDust, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.22f, 0.2f),
                    new GradientAlphaKey(0.15f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(colorGradient);

            var noise = groundMistParticles.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.2f;

            ParticleSystemRenderer renderer = groundMistParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sortMode = ParticleSystemSortMode.Distance;
            }

            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(groundMistParticles);
            groundMistParticles.Play();
        }

        private void CreateColdHalo()
        {
            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "PW_AmbientColdHalo";
            halo.transform.SetParent(transform, false);
            halo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            halo.transform.localScale = new Vector3(1.2f, 2f, 1f);

            Collider collider = halo.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            coldHaloTransform = halo.transform;
            coldHaloRenderer = halo.GetComponent<MeshRenderer>();
            haloMaterial = new Material(ResolveTransparentShader());
            haloMaterial.name = "PW_AmbientColdHalo";
            haloTexture = CreateHaloTexture();
            haloMaterial.SetTexture(MainTexPropertyId, haloTexture);

            if (coldHaloRenderer != null)
            {
                coldHaloRenderer.sharedMaterial = haloMaterial;
                PhantomWitchFxRenderUtil.SetRendererColor(coldHaloRenderer, new Color(
                    PhantomWitchConfig.GhostBreathVeil.r,
                    PhantomWitchConfig.GhostBreathVeil.g,
                    PhantomWitchConfig.GhostBreathVeil.b,
                    0f));
            }
        }

        private void CreateHeartbeatFlash()
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
            flash.name = "PW_AmbientHeartbeat";
            flash.transform.SetParent(transform, false);
            flash.transform.localPosition = new Vector3(0f, 1.2f, 0.16f);
            flash.transform.localScale = new Vector3(0.08f, 0.08f, 1f);

            Collider collider = flash.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            heartbeatTransform = flash.transform;
            heartbeatRenderer = flash.GetComponent<MeshRenderer>();
            if (heartbeatRenderer != null)
            {
                heartbeatRenderer.sharedMaterial = haloMaterial;
                heartbeatRenderer.enabled = false;
                PhantomWitchFxRenderUtil.SetRendererColor(heartbeatRenderer, new Color(
                    PhantomWitchConfig.BloodRoseMid.r,
                    PhantomWitchConfig.BloodRoseMid.g,
                    PhantomWitchConfig.BloodRoseMid.b,
                    0f));
            }
        }

        private Texture2D CreateHaloTexture()
        {
            Texture2D texture = new Texture2D(32, 64, TextureFormat.Alpha8, false);
            texture.name = "PW_AmbientHaloAlpha";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float centerX = 15.5f;
            float centerY = 31.5f;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    float nx = (x - centerX) / centerX;
                    float ny = (y - centerY) / centerY;
                    float distance = Mathf.Sqrt(nx * nx + ny * ny * 0.55f);
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha * (3f - 2f * alpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private void RefreshLayerState()
        {
            SetParticleEmission(veilParticles, ShouldShowVeil() ? DefaultVeilEmissionRate : 0f);
            SetParticleEmission(groundMistParticles, ResolveGroundMistEmissionRate());

            if (coldHaloRenderer != null)
            {
                coldHaloRenderer.enabled = ShouldShowColdHalo();
            }

            if (!ShouldShowHeartbeat())
            {
                HideHeartbeatFlash();
            }
        }

        private void SetParticleEmission(ParticleSystem particleSystem, float rate)
        {
            if (particleSystem == null)
            {
                return;
            }

            var emission = particleSystem.emission;
            emission.rateOverTime = rate;

            if (rate > 0f)
            {
                if (!particleSystem.isPlaying)
                {
                    particleSystem.Play();
                }
            }
            else if (particleSystem.isPlaying)
            {
                particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private bool ShouldShowVeil()
        {
            return !paused && detailLevel != PhantomWitchFxDetailLevel.Minimal;
        }

        private bool ShouldShowColdHalo()
        {
            return !paused && detailLevel == PhantomWitchFxDetailLevel.Full;
        }

        private bool ShouldShowHeartbeat()
        {
            return !paused && detailLevel != PhantomWitchFxDetailLevel.Minimal;
        }

        private bool ShouldShowRuneFlash()
        {
            return !paused && detailLevel == PhantomWitchFxDetailLevel.Full;
        }

        private float ResolveGroundMistEmissionRate()
        {
            if (paused || detailLevel == PhantomWitchFxDetailLevel.Minimal)
            {
                return 0f;
            }

            if (detailLevel == PhantomWitchFxDetailLevel.Reduced)
            {
                return DefaultGroundMistEmissionRate * 0.5f;
            }

            return DefaultGroundMistEmissionRate;
        }

        private void UpdateColdHalo()
        {
            if (coldHaloRenderer == null || coldHaloTransform == null)
            {
                return;
            }

            if (!ShouldShowColdHalo())
            {
                HideColdHalo();
                return;
            }

            Camera camera = PhantomWitchFxRuntime.CurrentCamera;
            if (camera != null)
            {
                coldHaloTransform.rotation = camera.transform.rotation;
            }

            float alpha = Mathf.Lerp(
                PhantomWitchConfig.AmbientHaloAlphaMin,
                PhantomWitchConfig.AmbientHaloAlphaMax + GetPhaseHaloBonus() + GetCloseCameraHaloBonus(camera),
                0.5f + 0.5f * Mathf.Sin((Time.time / PhantomWitchConfig.AmbientHaloBreathPeriod) * Mathf.PI * 2f));

            Color haloColor = new Color(
                PhantomWitchConfig.GhostBreathVeil.r,
                PhantomWitchConfig.GhostBreathVeil.g,
                PhantomWitchConfig.GhostBreathVeil.b,
                alpha);
            PhantomWitchFxRenderUtil.SetRendererColor(coldHaloRenderer, coldHaloBlock, haloColor);
        }

        private float GetPhaseHaloBonus()
        {
            if (phase == PhantomWitchPhase.Phase3)
            {
                return PhantomWitchConfig.AmbientHaloPhase2Bonus * 1.35f;
            }

            return phase == PhantomWitchPhase.Phase2 ? PhantomWitchConfig.AmbientHaloPhase2Bonus : 0f;
        }

        private float GetCloseCameraHaloBonus(Camera camera)
        {
            if (camera == null)
            {
                return 0f;
            }

            float distance = Vector3.Distance(camera.transform.position, transform.position);
            if (distance >= CloseCameraDistance)
            {
                return 0f;
            }

            float t = 1f - Mathf.Clamp01(distance / CloseCameraDistance);
            return PhantomWitchConfig.AmbientHaloAlphaCloseBonus * t;
        }

        private void HideColdHalo()
        {
            if (coldHaloRenderer == null)
            {
                return;
            }

            PhantomWitchFxRenderUtil.SetRendererColor(coldHaloRenderer, coldHaloBlock, new Color(
                PhantomWitchConfig.GhostBreathVeil.r,
                PhantomWitchConfig.GhostBreathVeil.g,
                PhantomWitchConfig.GhostBreathVeil.b,
                0f));
        }

        private void UpdateHeartbeat()
        {
            if (heartbeatRenderer == null || heartbeatTransform == null)
            {
                return;
            }

            Camera camera = PhantomWitchFxRuntime.CurrentCamera;
            if (camera != null)
            {
                heartbeatTransform.rotation = camera.transform.rotation;
            }

            if (!ShouldShowHeartbeat())
            {
                HideHeartbeatFlash();
                return;
            }

            if (Time.time >= nextHeartbeatTime && heartbeatVisibleUntil < Time.time)
            {
                heartbeatVisibleUntil = Time.time + PhantomWitchConfig.AmbientHeartbeatPulseDuration;
                heartbeatRenderer.enabled = true;
                ScheduleHeartbeat();
            }

            if (heartbeatVisibleUntil < Time.time)
            {
                HideHeartbeatFlash();
                return;
            }

            float t = Mathf.Clamp01((heartbeatVisibleUntil - Time.time) / PhantomWitchConfig.AmbientHeartbeatPulseDuration);
            float eased = 1f - Mathf.Abs(t * 2f - 1f);
            float alpha = 0.6f * eased;
            float scale = Mathf.Lerp(0.03f, 0.06f, eased);

            heartbeatTransform.localScale = new Vector3(scale, scale, 1f);
            PhantomWitchFxRenderUtil.SetRendererColor(heartbeatRenderer, heartbeatBlock, new Color(
                PhantomWitchConfig.BloodRoseMid.r,
                PhantomWitchConfig.BloodRoseMid.g,
                PhantomWitchConfig.BloodRoseMid.b,
                alpha));
        }

        private void HideHeartbeatFlash()
        {
            if (heartbeatRenderer == null)
            {
                return;
            }

            heartbeatRenderer.enabled = false;
            heartbeatVisibleUntil = -1f;
        }

        private void UpdateRuneFlash()
        {
            if (!ShouldShowRuneFlash() || Time.time < nextRuneFlashTime)
            {
                return;
            }

            SpawnRuneFlash();
            ScheduleRuneFlash();
        }

        private void ScheduleRuneFlash()
        {
            nextRuneFlashTime = Time.time + Random.Range(
                PhantomWitchConfig.AmbientRuneFlashMinInterval,
                PhantomWitchConfig.AmbientRuneFlashMaxInterval);
        }

        private void ScheduleHeartbeat()
        {
            float minInterval = PhantomWitchConfig.AmbientHeartbeatMinInterval;
            float maxInterval = PhantomWitchConfig.AmbientHeartbeatMaxInterval;

            if (ResolvePlayerHealthRatio() < 0.3f)
            {
                minInterval = PhantomWitchConfig.AmbientHeartbeatLowHealthMinInterval;
                maxInterval = PhantomWitchConfig.AmbientHeartbeatLowHealthMaxInterval;
            }

            nextHeartbeatTime = Time.time + Random.Range(minInterval, maxInterval);
        }

        private float ResolvePlayerHealthRatio()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health.MaxHealth <= 0f)
                {
                    return 1f;
                }

                return player.Health.CurrentHealth / player.Health.MaxHealth;
            }
            catch
            {
                return 1f;
            }
        }

        private void SpawnRuneFlash()
        {
            PruneDestroyedTransientEffects();

            GameObject root = new GameObject("PW_AmbientRuneFlash");
            root.transform.SetParent(transform, false);

            Vector2 horizontal = Random.insideUnitCircle.normalized * 1.5f;
            if (horizontal.sqrMagnitude < 0.01f)
            {
                horizontal = Vector2.right * 1.5f;
            }

            root.transform.localPosition = new Vector3(horizontal.x, Random.Range(1f, 1.8f), horizontal.y);
            root.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-20f, 20f));

            int lineCount = Random.Range(2, 4);
            for (int i = 0; i < lineCount; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                segment.transform.SetParent(root.transform, false);
                LineRenderer line = segment.AddComponent<LineRenderer>();
                line.sharedMaterial = GetLineMaterial();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = Random.Range(0.018f, 0.028f);
                line.startColor = new Color(
                    PhantomWitchConfig.SilverAshCore.r,
                    PhantomWitchConfig.SilverAshCore.g,
                    PhantomWitchConfig.SilverAshCore.b,
                    0.65f);
                line.endColor = new Color(
                    PhantomWitchConfig.SilverAshCore.r,
                    PhantomWitchConfig.SilverAshCore.g,
                    PhantomWitchConfig.SilverAshCore.b,
                    0.1f);

                float width = Random.Range(0.12f, 0.25f);
                float height = Random.Range(-0.12f, 0.12f);
                line.SetPosition(0, new Vector3(-width, height, 0f));
                line.SetPosition(1, new Vector3(Random.Range(-0.04f, 0.04f), height + Random.Range(0.05f, 0.12f), 0f));
                line.SetPosition(2, new Vector3(width * Random.Range(0.6f, 1f), height + Random.Range(-0.08f, 0.08f), 0f));
            }

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(0.5f, 0.4f);

            transientEffects.Add(root);
        }

        private Material GetLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private Shader ResolveTransparentShader()
        {
            return Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Additive")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Standard");
        }

        private void CleanupTransientEffects()
        {
            for (int i = 0; i < transientEffects.Count; i++)
            {
                if (transientEffects[i] != null)
                {
                    Destroy(transientEffects[i]);
                }
            }

            transientEffects.Clear();
        }

        private void PruneDestroyedTransientEffects()
        {
            for (int i = transientEffects.Count - 1; i >= 0; i--)
            {
                if (transientEffects[i] == null)
                {
                    transientEffects.RemoveAt(i);
                }
            }
        }
    }
}
