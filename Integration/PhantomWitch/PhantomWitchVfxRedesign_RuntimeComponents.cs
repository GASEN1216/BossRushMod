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

            for (int i = 0; i < 3; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                Transform segmentTransform = segment.transform;
                segmentTransform.SetParent(runeTransform, false);
                LineRenderer line = segment.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = 0.018f;
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

    internal sealed class PhantomWitchLightPulse : MonoBehaviour
    {
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
                this.targetLight.intensity = targetIntensity * 1.5f;
                this.targetLight.range = targetRange * 1.2f;
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (targetLight != null)
            {
                float burst = Mathf.Exp(-t * 10f); // Rapid flash decay
                float fade = 1f - Mathf.Pow(t, 2f); // Smooth slow fade
                targetLight.intensity = maxIntensity * (fade + burst * 0.5f);
                targetLight.range = maxRange * (fade * 0.8f + 0.2f + burst * 0.2f);
            }
            if (t >= 1f) Destroy(this);
        }
    }
}
