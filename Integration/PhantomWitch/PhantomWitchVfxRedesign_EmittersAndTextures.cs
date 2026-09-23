// ============================================================================
// PhantomWitchVfxRedesign_EmittersAndTextures.cs - particle emitters and texture helpers
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static partial class PhantomWitchVfxRedesign
    {
        private static Mesh GetBillboardQuadMesh()
        {
            if (cachedBillboardQuadMesh != null) return cachedBillboardQuadMesh;
            cachedBillboardQuadMesh = Object.Instantiate(GetQuadMesh());
            cachedBillboardQuadMesh.name = "PW_Redesign_BillboardQuadMesh";
            cachedBillboardQuadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            cachedBillboardQuadMesh.RecalculateNormals();
            cachedBillboardQuadMesh.RecalculateBounds();
            return cachedBillboardQuadMesh;
        }

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
            else if (shapeType == ParticleSystemShapeType.Circle)
            {
                // 审查 VB-04：Circle 默认在局部 XY 平面（竖着），放平到地面。
                shape.rotation = new Vector3(90f, 0f, 0f);
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
            // 雾走半透明混合：加色的大雾团会叠成一片发白的光膜。
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps, BossRushFxBlend.Alpha);

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

            // 审查 VB-04：Circle 默认竖着，魂雾此前是一面竖直的雾扇；用 shape.rotation 放平（速度轴不受影响）。
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);

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
                    new GradientAlphaKey(0.45f, 0.15f),
                    new GradientAlphaKey(0.3f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // 雾团出生偏小、边走边散开，不是一出生就满尺寸的圆片。
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(1f, 1.25f)));

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

            // 审查 VB-05 / 口径第 7 条：星点 <=0.2 m；寿命跟特效时长走（此前固定 1.5-3.5 s，0.72 s 的横扫回收时正处在最亮处被一刀切）。
            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Clamp(duration * 0.5f, 0.4f, 0.9f),
                Mathf.Clamp(duration * 0.8f, 0.6f, 1.4f));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); // 细小亮点
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

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.2f, 1f),
                new Keyframe(1f, 0.35f)));

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

            // 审查 VB-08：0.018-0.024 m 的十字只剩 1-2 px、一闪一闪；只留一道 0.08 m 收尖的竖向闪光。
            LineRenderer vertical = CreateCrossLine(root.transform, new Vector3(0f, size * 0.7f, 0f), new Vector3(0f, -size, 0f), 0.08f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.9f));
            if (vertical != null)
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
            line.widthMultiplier = Mathf.Max(PhantomWitchFxRenderUtil.MinWorldLineWidth, width);
            line.widthCurve = TaperedLineWidthCurve;
            line.sharedMaterial = GetLineMaterial();
            line.startColor = color;
            line.endColor = WithAlpha(color, color.a * 0.35f);
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
                line.widthMultiplier = PhantomWitchFxRenderUtil.MinWorldLineWidth;
                line.widthCurve = TaperedLineWidthCurve;
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
            line.widthMultiplier = PhantomWitchFxRenderUtil.MinWorldLineWidth;
            line.widthCurve = TaperedLineWidthCurve;
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

            // 审查 VB-08：0.02-0.05 m 的微粒只有 1-3 px，改 0.06-0.12 m、数量减半（不再是一片噪点）。
            int burstCount = Mathf.Max(3, count / 2);
            var main = ps.main;
            main.duration = lifetime + 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = color;
            main.maxParticles = burstCount + 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)burstCount) });

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

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.3f)));

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
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
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

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.35f)));

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
            // RGBA（白色 + alpha）：Alpha8 在部分图形 API 上采样出 rgb = 0，颜色走顶点色 / 属性块时会变黑。
            // 会话常驻（HideAndDontSave）：它是共享材质工厂的缓存键，清缓存时不销毁。
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = broken ? "PW_BrokenAltarProjection" : "PW_AltarProjection";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

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

            // 审查 VB-08：3 条 0.012 m（约 0.8 px）的发丝线看不见只会闪；改 2 条 0.06 / 0.045 m、自下而上收尖。
            for (int i = 0; i < 2; i++)
            {
                GameObject lineGo = new GameObject("Line_" + i);
                lineGo.transform.SetParent(root.transform, false);
                lineGo.transform.localPosition = new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), 0f, UnityEngine.Random.Range(-0.12f, 0.12f));
                LineRenderer line = lineGo.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 2;
                line.widthMultiplier = i == 0 ? PhantomWitchFxRenderUtil.MinWorldLineWidth : 0.045f;
                line.widthCurve = TaperedLineWidthCurve;
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

        // ==================== 地面预警（审查 VB-01 / VB-07）与尺寸缩放工具 ====================
        // 从 PhantomWitchVfxRedesign.cs 原样挪来（LargeFileBudgetGuard：主文件不超过 1200 行）。

        private static void CreateCircleTelegraph(Transform parent, float radius, float chargeDuration)
        {
            GameObject telegraph = new GameObject("CircleTelegraph");
            telegraph.transform.SetParent(parent, false);
            telegraph.transform.localPosition = Vector3.zero;
            PhantomWitchTelegraphDriver driver = telegraph.AddComponent<PhantomWitchTelegraphDriver>();

            GameObject fill = CreateFlatQuad(telegraph.transform, "TelegraphFill", radius * 2f, 0.025f, WithAlpha(TelegraphFillColor, 0f));
            MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
            Material fillMaterial = GetTelegraphFillMaterial();
            if (fillRenderer != null && fillMaterial != null)
            {
                fillRenderer.sharedMaterial = fillMaterial;
            }

            PhantomWitchFlatRingMesh ring = CreateRing(telegraph.transform, radius, 0.20f, WithAlpha(TelegraphOutlineColor, 0f), 0.035f);

            driver.Configure(chargeDuration, TelegraphPostFade);
            driver.SetRingOutline(ring, radius);
            driver.SetOutlineStyle(TelegraphOutlineColor, 0.55f, 1f, 0.20f, 0.40f, TelegraphFlashColor);
            driver.SetFill(fill.transform, fillRenderer, new Vector3(radius * 2f, 1f, radius * 2f), TelegraphFillColor, 0.10f, 0.28f);
        }

        /// <summary>
        /// 扇形出手预警（审查 VB-07）。apex 在 origin 前方 forwardOffset 处，半径 / 半角取判定值；
        /// 每帧跟随 origin → target 的水平方向（与判定 ResolveAttackForward 在出手瞬间的朝向一致）。
        /// outlineOnly = true 时只画虚线外沿、不画填充，用来标第二段更远的判定。
        /// </summary>
        internal static GameObject CreateConeTelegraph(Transform origin, Transform target, float radius, float halfAngle, float forwardOffset, float chargeDuration, bool outlineOnly)
        {
            float safeRadius = Mathf.Max(0.3f, radius);
            float safeHalfAngle = Mathf.Clamp(halfAngle, 1f, 179f);
            float safeDuration = Mathf.Max(0.05f, chargeDuration);

            GameObject root = CreateRoot(outlineOnly ? "PW_ConeTelegraphOutline" : "PW_ConeTelegraph", origin.position);
            PhantomWitchTelegraphDriver driver = root.AddComponent<PhantomWitchTelegraphDriver>();
            driver.Configure(safeDuration, TelegraphPostFade);
            driver.SetTracking(origin, target, forwardOffset, 0.03f);

            int segments = ResolveAdaptiveCount(24, 16, 10);
            if (!outlineOnly)
            {
                GameObject fill = new GameObject("TelegraphFill");
                fill.transform.SetParent(root.transform, false);
                fill.transform.localPosition = Vector3.zero;
                Mesh mesh = BuildSectorFillMesh(safeRadius, safeHalfAngle, segments);
                MeshFilter filter = fill.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                fill.AddComponent<PhantomWitchRuntimeMesh>().SetMesh(mesh);
                MeshRenderer renderer = fill.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = GetTelegraphFillMaterial();
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, WithAlpha(TelegraphFillColor, 0f));
                driver.SetFill(fill.transform, renderer, Vector3.one, TelegraphFillColor, 0.12f, 0.30f);
            }

            Vector3[] arcPoints = BuildArcPoints(safeRadius, safeHalfAngle, Vector3.forward, segments);
            Vector3[][] outlinePoints = new Vector3[][]
            {
                arcPoints,
                new Vector3[] { Vector3.zero, arcPoints[0] },
                new Vector3[] { Vector3.zero, arcPoints[arcPoints.Length - 1] }
            };
            Material outlineMaterial = outlineOnly ? GetDashedGroundLineMaterial() : GetGroundLineMaterial();
            PhantomWitchFlatPathMesh[] outlines = new PhantomWitchFlatPathMesh[outlinePoints.Length];
            for (int i = 0; i < outlinePoints.Length; i++)
            {
                GameObject edge = new GameObject(i == 0 ? "TelegraphArc" : "TelegraphEdge");
                edge.transform.SetParent(root.transform, false);
                edge.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                PhantomWitchFlatPathMesh path = edge.AddComponent<PhantomWitchFlatPathMesh>();
                path.Configure(outlinePoints[i], 0.16f, outlineMaterial, WithAlpha(TelegraphOutlineColor, 0f));
                outlines[i] = path;
            }

            driver.SetPathOutlines(outlines, outlinePoints);
            if (outlineOnly)
            {
                driver.SetOutlineStyle(TelegraphOutlineColor, 0.35f, 0.75f, 0.14f, 0.24f, TelegraphFlashColor);
            }
            else
            {
                driver.SetOutlineStyle(TelegraphOutlineColor, 0.5f, 1f, 0.16f, 0.30f, TelegraphFlashColor);
            }

            Object.Destroy(root, safeDuration + TelegraphPostFade + 0.05f);
            return root;
        }

        /// <summary>扇形填充网格：顶点在原点、朝 +Z。UV 按位置线性映射到软圆盘贴图，弧边自然羽化。</summary>
        private static Mesh BuildSectorFillMesh(float radius, float halfAngle, int segments)
        {
            segments = Mathf.Max(2, segments);
            Mesh mesh = new Mesh();
            mesh.name = "PW_ConeTelegraphFill";
            Vector3[] vertices = new Vector3[segments + 2];
            Vector2[] uv = new Vector2[segments + 2];
            int[] triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            float halfRadians = halfAngle * Mathf.Deg2Rad;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-halfRadians, halfRadians, (float)i / segments);
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);
                vertices[i + 1] = new Vector3(sin * radius, 0f, cos * radius);
                uv[i + 1] = new Vector2(0.5f + 0.5f * sin, 0.5f + 0.5f * cos);
            }

            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 按倍率缩放粒子初始尺寸的两端。`startSizeMultiplier` 在 TwoConstants 模式下只写上限常数，
        /// 会把 0.1–0.2 m 的粒子拉成 0.1–2 m 的雾团（审查 VB-03）。
        /// </summary>
        internal static ParticleSystem.MinMaxCurve ScaleStartSize(ParticleSystem.MinMaxCurve size, float scale)
        {
            switch (size.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(size.constant * scale);
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(size.constantMin * scale, size.constantMax * scale);
                default:
                    size.curveMultiplier = size.curveMultiplier * scale;
                    return size;
            }
        }

        private static Material GetLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        // ==================== 程序化贴图（会话常驻，是 BossRushFxMaterials 共享材质的缓存键） ====================

        /// <summary>
        /// 柔边带：沿长度（u）不变，横向（v）中间实心、两侧各约 1/3 羽化到 0。
        /// 给贴地环 / 弧 / 扇形边用（PhantomWitchFlatRingMesh 与 PhantomWitchFlatPathMesh 的 v 都是横跨带宽）。
        /// 此前是 whiteTexture 硬边带（审查 VB-10）。
        /// </summary>
        internal static Texture2D GetSoftBandTexture()
        {
            if (cachedSoftBandTexture == null)
            {
                cachedSoftBandTexture = CreateBandTexture("PW_SoftBand", 4, 0);
            }

            return cachedSoftBandTexture;
        }

        /// <summary>虚线柔边带：沿长度切成 9 段短划（第二段 / 延伸判定的扇形外沿）。</summary>
        private static Texture2D GetDashedBandTexture()
        {
            if (cachedDashedBandTexture == null)
            {
                cachedDashedBandTexture = CreateBandTexture("PW_DashedBand", 72, 9);
            }

            return cachedDashedBandTexture;
        }

        private static Texture2D CreateBandTexture(string name, int width, int dashCount)
        {
            const int height = 32;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float across = Mathf.Abs(v * 2f - 1f);
                float band = 1f - BossRushUI.SmoothStep((across - 0.35f) / 0.65f);
                for (int x = 0; x < width; x++)
                {
                    float dash = 1f;
                    if (dashCount > 0)
                    {
                        float phase = ((x + 0.5f) / width) * dashCount;
                        phase -= Mathf.Floor(phase);
                        // 每段前 60% 实、后 40% 空，两头各 0.08 羽化，避免锯齿状硬断。
                        dash = Mathf.Clamp01(Mathf.Min(phase, 0.6f - phase) / 0.08f);
                    }

                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(band * dash) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>预警填充用的实心软边圆盘：半径 0.82 内不透明，之外 SmoothStep 羽化到 0。</summary>
        private static Texture2D GetSoftDiscTexture()
        {
            if (cachedSoftDiscTexture != null)
            {
                return cachedSoftDiscTexture;
            }

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "PW_SoftDisc";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 1f - BossRushUI.SmoothStep((r - 0.82f) / 0.18f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            cachedSoftDiscTexture = texture;
            return cachedSoftDiscTexture;
        }
    }
}
