// ============================================================================
// PhantomWitchVfxRedesign_EmittersAndTextures.cs - particle emitters and texture helpers
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static partial class PhantomWitchVfxRedesign
    {
        private static ParticleSystem CreateSoulFlameEmitter(Transform parent, Vector3 localPosition, float rate, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, bool worldSpace, ParticleSystemShapeType shapeType)
        {
            GameObject go = new GameObject("SoulFlame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = 30f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = Color.white;
            main.maxParticles = 32;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.25f;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.radius = 0.25f;
            if (shapeType == ParticleSystemShapeType.Cone)
            {
                shape.angle = 10f;
            }

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidCore, 0.2f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidMid, 0.6f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidDust, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.15f),
                    new GradientAlphaKey(0.7f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.3f, 1f),
                new Keyframe(0.9f, 0.4f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.4f;

            ps.Play();
            return ps;
        }

        private static ParticleSystem CreateSoulMistEmitter(Transform parent, float radius, float rate, float lifeMin, float lifeMax, bool worldSpace, float yOffset)
        {
            GameObject go = new GameObject("SoulMist");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = 30f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startColor = Color.white;
            main.maxParticles = 24;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 1f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(PhantomWitchConfig.VioletVoidCore, 0f),
                    new GradientColorKey(PhantomWitchConfig.SilverAshCore, 0.35f),
                    new GradientColorKey(PhantomWitchConfig.GhostBreathVeil, 0.7f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidDust, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.15f),
                    new GradientAlphaKey(0.6f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play();
            return ps;
        }

        internal static ParticleSystem CreateStardustEmitter(Transform parent, float radius, float rate, float duration)
        {
            GameObject go = new GameObject("StardustAmbient");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.18f); // Very small, bright dots
            main.startColor = Color.white;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f; // Float gently upwards

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(PhantomWitchConfig.SilverAshCore, 0.45f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidMid, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.2f;
            noise.frequency = 0.6f;

            ps.Play();
            return ps;
        }

        private static void CreateSilverCrossFlash(Transform parent, float size, float yOffset, bool inverted, float duration)
        {
            GameObject root = new GameObject(inverted ? "InvertedCrossFlash" : "SilverCrossFlash");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, yOffset, 0f);
            root.transform.localRotation = Quaternion.Euler(0f, 0f, inverted ? 180f : 0f);
            root.AddComponent<PhantomWitchBillboard>();

            LineRenderer vertical = CreateCrossLine(root.transform, new Vector3(0f, size * 0.7f, 0f), new Vector3(0f, -size, 0f), 0.024f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.9f));
            LineRenderer horizontal = CreateCrossLine(root.transform, new Vector3(-size * 0.35f, size * 0.15f, 0f), new Vector3(size * 0.35f, size * 0.15f, 0f), 0.018f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.75f));
            if (vertical != null && horizontal != null)
            {
                PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(duration, duration);
            }
            Object.Destroy(root, duration);
        }

        private static LineRenderer CreateCrossLine(Transform parent, Vector3 start, Vector3 end, float width, Color color)
        {
            GameObject lineGo = new GameObject("CrossLine");
            lineGo.transform.SetParent(parent, false);
            LineRenderer line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.sharedMaterial = GetLineMaterial();
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            return line;
        }

        private static void CreateSilkTearBurst(Transform parent, int count, float minLength, float maxLength, Color lineColor, Color particleColor, bool includeBlood, float duration)
        {
            GameObject root = new GameObject("SilkTearBurst");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                GameObject lineGo = new GameObject("Tear_" + i);
                lineGo.transform.SetParent(root.transform, false);
                lineGo.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-30f, 30f));
                lineGo.transform.localPosition = new Vector3(UnityEngine.Random.Range(-0.08f, 0.08f), UnityEngine.Random.Range(0.02f, 0.18f), UnityEngine.Random.Range(-0.08f, 0.08f));

                LineRenderer line = lineGo.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = 0.018f;
                line.sharedMaterial = PhantomWitchVfxRedesign.GetSharedLineMaterial();
                line.startColor = lineColor;
                line.endColor = WithAlpha(lineColor, lineColor.a * 0.2f);

                float length = UnityEngine.Random.Range(minLength, maxLength);
                line.SetPosition(0, new Vector3(-length * 0.5f, 0f, 0f));
                line.SetPosition(1, new Vector3(0f, UnityEngine.Random.Range(0.02f, 0.05f), 0f));
                line.SetPosition(2, new Vector3(length * 0.5f, UnityEngine.Random.Range(-0.03f, 0.03f), 0f));
            }

            CreateEdgeDissolveBurst(root.transform, 6, 0.25f, 0.15f, particleColor);
            if (includeBlood)
            {
                CreateBillboardQuad(root.transform, 0.08f, 0.08f, new Vector3(0f, 0.12f, 0f), WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.8f), true);
            }

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, duration);
            Object.Destroy(root, duration);
        }

        private static void CreateTearLine(Transform parent, float length, Color color, float duration)
        {
            GameObject lineGo = new GameObject("TearLine");
            lineGo.transform.SetParent(parent, false);
            lineGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            lineGo.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            LineRenderer line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 4;
            line.widthMultiplier = 0.03f;
            line.sharedMaterial = GetLineMaterial();
            line.startColor = color;
            line.endColor = WithAlpha(color, color.a * 0.35f);
            line.SetPosition(0, new Vector3(-length * 0.5f, 0f, 0f));
            line.SetPosition(1, new Vector3(-length * 0.18f, 0.02f, 0f));
            line.SetPosition(2, new Vector3(length * 0.16f, -0.03f, 0f));
            line.SetPosition(3, new Vector3(length * 0.5f, 0.01f, 0f));

            PhantomWitchFadeDestroy fade = lineGo.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, duration);
            Object.Destroy(lineGo, duration);
        }

        private static void CreateEdgeDissolveBurst(Transform parent, int count, float lifetime, float radius, Color color)
        {
            GameObject go = new GameObject("EdgeDissolve");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = lifetime + 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = color;
            main.maxParticles = count + 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(PhantomWitchConfig.VioletVoidDust, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.2f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play();
            Object.Destroy(go, lifetime + 0.2f);
        }

        private static void CreateEdgeDissolveLoop(Transform parent, float radius, float rate, float yOffset)
        {
            GameObject go = new GameObject("EdgeDissolveLoop");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = WithAlpha(PhantomWitchConfig.SilverAshCore, 0.75f);
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.03f;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(PhantomWitchConfig.SilverAshCore, 0f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidMid, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.75f, 0.15f),
                    new GradientAlphaKey(0.25f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play();
        }

        private static void CreateFakeWarpField(Transform parent, float radius, float yOffset, float duration)
        {
            GameObject quad = CreateFlatQuad(parent, "FakeWarpField", radius * 2f, yOffset, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.08f));
            quad.AddComponent<PhantomWitchWarpQuad>().Configure(0.03f, 2f, duration);

            if (PhantomWitchFxRuntime.CurrentDetailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                CreateBillboardQuad(parent, radius * 0.18f, radius * 0.18f, new Vector3(0f, yOffset + 0.45f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.12f), true);
            }
        }

        private static void CreateSoulTendril(Transform parent, Vector3 start, Vector3 end, float duration, Color color, bool fullDetail)
        {
            GameObject root = new GameObject("SoulTendril");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = start;

            CreateTrail(root.transform, color, 0.15f, 0.02f, 1f, Vector3.zero);
            if (fullDetail)
            {
                CreateTrail(root.transform, WithAlpha(color, color.a * 0.5f), 0.08f, 0.015f, 0.8f, new Vector3(0.03f, 0f, 0f));
                CreateTrail(root.transform, WithAlpha(color, color.a * 0.45f), 0.08f, 0.015f, 0.8f, new Vector3(-0.03f, 0f, 0f));
            }

            PhantomWitchTendrilMover mover = root.AddComponent<PhantomWitchTendrilMover>();
            mover.Configure(start, end, duration);
            Object.Destroy(root, duration + 1.2f);
        }

        private static void CreateTrail(Transform parent, Color color, float widthStart, float widthEnd, float time, Vector3 offset)
        {
            GameObject trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(parent, false);
            trailGo.transform.localPosition = offset;

            TrailRenderer trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = GetTrailMaterial();
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, widthStart),
                new Keyframe(1f, widthEnd));
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 0.6f), new GradientColorKey(color, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.autodestruct = false;
        }

        private static GameObject CreateAltarProjection(Transform parent, float radius, float yOffset, Color color, float duration, bool broken)
        {
            GameObject quad = CreateFlatQuad(parent, broken ? "BrokenAltarProjection" : "AltarProjection", radius * 2f, yOffset, color);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetAltarMaterial(broken);
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, color);
            }

            PhantomWitchFadeDestroy fade = quad.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, Mathf.Min(0.7f, duration));
            return quad;
        }

        private static Texture2D GetAltarProjectionTexture()
        {
            if (cachedAltarProjectionTexture == null)
            {
                cachedAltarProjectionTexture = CreateProjectionTexture(false);
            }
            return cachedAltarProjectionTexture;
        }

        private static Texture2D GetBrokenAltarProjectionTexture()
        {
            if (cachedBrokenAltarProjectionTexture == null)
            {
                cachedBrokenAltarProjectionTexture = CreateProjectionTexture(true);
            }
            return cachedBrokenAltarProjectionTexture;
        }

        private static Texture2D CreateProjectionTexture(bool broken)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.Alpha8, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - center) / center;
                    float ny = (y - center) / center;
                    float radial = Mathf.Sqrt(nx * nx + ny * ny);
                    float angle = Mathf.Atan2(ny, nx);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(radial - 0.62f) * 8f);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(radial - 0.34f) * 10f) * 0.45f;
                    float spokes = Mathf.Clamp01(Mathf.Sin(angle * 6f) * 0.5f + 0.5f) * Mathf.Clamp01(1f - radial);
                    float noise = Mathf.Clamp01(Mathf.Sin((x * 0.17f) + (y * 0.11f)) * 0.5f + 0.5f);
                    float alpha = Mathf.Max(ring, inner + spokes * 0.25f) * noise;
                    if (broken)
                    {
                        float crack = Mathf.Abs(nx + ny * 0.25f);
                        if (crack < 0.04f || Mathf.Abs(nx - ny * 0.55f) < 0.03f)
                        {
                            alpha *= 0.15f;
                        }
                    }
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                }
            }

            texture.Apply();
            return texture;
        }

        private static void CreateRequiemLine(Transform parent, float height, float duration, bool rise, Color color)
        {
            GameObject root = new GameObject("RequiemLine");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0.1f, 0f);

            for (int i = 0; i < 3; i++)
            {
                GameObject lineGo = new GameObject("Line_" + i);
                lineGo.transform.SetParent(root.transform, false);
                lineGo.transform.localPosition = new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), 0f, UnityEngine.Random.Range(-0.12f, 0.12f));
                LineRenderer line = lineGo.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 2;
                line.widthMultiplier = 0.012f;
                line.sharedMaterial = PhantomWitchVfxRedesign.GetSharedLineMaterial();
                line.startColor = color;
                line.endColor = WithAlpha(color, color.a * 0.2f);
                line.SetPosition(0, new Vector3(0f, 0f, 0f));
                line.SetPosition(1, new Vector3(0f, height, 0f));
            }

            if (rise)
            {
                root.AddComponent<PhantomWitchVerticalLineDrift>().Configure(new Vector3(0f, 0.65f, 0f), duration);
            }

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, duration);
            Object.Destroy(root, duration);
        }

        private static Vector3 ArcPoint(Vector3 forward, float radius, float angleOffset)
        {
            float baseAngle = Mathf.Atan2(forward.x, forward.z) + angleOffset;
            return new Vector3(Mathf.Sin(baseAngle) * radius, 0f, Mathf.Cos(baseAngle) * radius);
        }
    }
}
