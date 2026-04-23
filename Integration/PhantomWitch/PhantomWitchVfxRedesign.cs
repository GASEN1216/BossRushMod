using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class PhantomWitchVfxRedesign
    {
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");

        private static Material cachedLineMaterial;
        private static Material cachedGroundLineMaterial;
        private static Material cachedQuadMaterial;
        private static Material cachedTrailMaterial;
        private static Material cachedAltarMaterial;
        private static Material cachedBrokenAltarMaterial;
        private static Mesh cachedQuadMesh;
        private static Texture2D cachedAltarProjectionTexture;
        private static Texture2D cachedBrokenAltarProjectionTexture;

        private static readonly Dictionary<string, Stack<GameObject>> VfxPools = new Dictionary<string, Stack<GameObject>>();

        private static GameObject GetOrBuildVfx(string key, Vector3 position, float duration, System.Action<GameObject> builder)
        {
            GameObject root;
            if (!TryAcquireCleanPooledRoot(key, position, out root))
            {
                root = CreateRoot(key, position);
            }
            builder(root);

            PhantomWitchVfxRecycler recycler = root.GetComponent<PhantomWitchVfxRecycler>();
            if (recycler == null)
            {
                recycler = root.AddComponent<PhantomWitchVfxRecycler>();
            }
            recycler.Schedule(key, duration);

            return root;
        }

        private static bool TryAcquireCleanPooledRoot(string key, Vector3 position, out GameObject root)
        {
            root = null;

            if (!VfxPools.TryGetValue(key, out Stack<GameObject> stack))
            {
                return false;
            }

            while (stack.Count > 0)
            {
                GameObject candidate = stack.Pop();
                if (candidate == null)
                {
                    continue;
                }

                if (!IsReusablePooledRoot(candidate))
                {
                    Object.Destroy(candidate);
                    continue;
                }

                root = candidate;
                root.transform.position = position;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                root.SetActive(true);
                return true;
            }

            return false;
        }

        private static bool IsReusablePooledRoot(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            if (root.transform.childCount > 0)
            {
                return false;
            }

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null ||
                    component is Transform ||
                    component is PhantomWitchVfxRecycler ||
                    component is PhantomWitchFxRootTracker)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void CleanupRootForPooling(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child != null)
                {
                    Object.Destroy(child.gameObject);
                }
            }

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null ||
                    component is Transform ||
                    component is PhantomWitchVfxRecycler ||
                    component is PhantomWitchFxRootTracker)
                {
                    continue;
                }

                Object.Destroy(component);
            }

            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }

        internal sealed class PhantomWitchVfxRecycler : MonoBehaviour
        {
            private string poolKey;
            private float recycleTime;
            private bool isScheduled;

            public void Schedule(string key, float duration)
            {
                this.poolKey = key;
                this.recycleTime = Time.time + duration;
                this.isScheduled = true;
            }

            private void Update()
            {
                if (isScheduled && Time.time >= recycleTime)
                {
                    isScheduled = false;
                    CleanupRootForPooling(gameObject);
                    gameObject.SetActive(false);
                    if (!VfxPools.TryGetValue(poolKey, out Stack<GameObject> stack))
                    {
                        stack = new Stack<GameObject>();
                        VfxPools[poolKey] = stack;
                    }
                    if (stack.Count < 20)
                    {
                        stack.Push(gameObject);
                    }
                    else
                    {
                        Destroy(gameObject);
                    }
                }
            }
        }

        internal static GameObject CreateChannelChargeEffect(Vector3 position, float radius, float duration, bool useBloodAccent)
        {
            GameObject root = CreateRoot("PW_ChannelChargeFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 1.2f, 0f), PhantomWitchConfig.VioletVoidCore, radius * 1.8f, 4.8f);
            CreateFakeWarpField(root.transform, Mathf.Max(0.8f, radius), 0.10f, duration);
            CreateSoulFlameEmitter(root.transform, new Vector3(0f, 1.15f, 0f), 8f, 1.2f, 1.8f, 0.3f, 0.8f, 0.08f, 0.16f, false, ParticleSystemShapeType.Cone);
            CreateSoulMistEmitter(root.transform, Mathf.Max(0.42f, radius * 0.55f), 10f, 0.9f, 1.4f, false, 0.08f);
            CreateBrokenRing(root.transform, Mathf.Max(0.52f, radius * 0.88f), 0.04f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.82f), 0.09f, ResolveRingSegments(), 0.80f, 0.008f);
            CreatePartialRing(root.transform, Mathf.Max(0.45f, radius * 0.75f), 0.04f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.72f), 0.10f, ResolveRingSegments(), 0.72f, 0.01f);
            CreateRuneFlashSpawner(root.transform, Mathf.Max(0.35f, radius * 0.55f), 7, 0.14f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.72f));
            CreateStardustEmitter(root.transform, Mathf.Max(0.45f, radius * 0.8f), 26f, duration);
            AttachTransientRequiemLine(root, Mathf.Max(0.95f, radius * 0.9f), Mathf.Max(0.22f, duration * 0.35f), true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.84f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.20f, radius * 0.22f), 1.08f, useBloodAccent, 0.22f);

            GameObject heart = CreateBillboardQuad(root.transform, 0.08f, 0.08f, new Vector3(0f, 1.35f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.55f), true);
            heart.AddComponent<PhantomWitchPulseScale>().Configure(new Vector3(0.04f, 0.04f, 1f), new Vector3(0.09f, 0.09f, 1f), duration);

            if (useBloodAccent)
            {
                CreateBillboardQuad(root.transform, 0.05f, 0.05f, new Vector3(0f, 1.20f, 0.06f), WithAlpha(PhantomWitchConfig.BloodRoseMid, 0.45f), true);
            }

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, Mathf.Min(duration, 0.25f));
            Object.Destroy(root, duration);
            return root;
        }

        internal static GameObject CreateTeleportEffect(Vector3 position, bool isAppear)
        {
            float rootLifetime = isAppear
                ? Mathf.Max(PhantomWitchConfig.TeleportFxDuration, 0.8f)
                : Mathf.Max(PhantomWitchConfig.TeleportFxDuration, 1.5f);

            GameObject root = GetOrBuildVfx(isAppear ? "PW_BlinkInFX" : "PW_BlinkOutFX", position, rootLifetime, (rootObj) =>
            {
                if (isAppear)
                {
                    CreatePointLight(rootObj.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidCore, PhantomWitchConfig.TeleportExpandRadius * 1.8f, 6.2f);
                    PhantomWitchFlatRingMesh ring = CreateRing(rootObj.transform, PhantomWitchConfig.TeleportShrinkRadius, 0.055f, WithAlpha(PhantomWitchConfig.SilverAshMid, 0.82f), 0.06f);
                    PhantomWitchShrinkRing shrink = ring.gameObject.AddComponent<PhantomWitchShrinkRing>();
                    shrink.Configure(PhantomWitchConfig.TeleportShrinkRadius, 0.22f, WithAlpha(PhantomWitchConfig.SilverAshMid, 0.82f), 0.055f);

                    CreateRuneFlashSpawner(rootObj.transform, PhantomWitchConfig.TeleportExpandRadius * 0.65f, 8, 0.10f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.78f));
                    CreateSoulMistEmitter(rootObj.transform, 0.82f, 11f, 1.0f, 1.6f, false, 0.03f);
                    CreateStardustEmitter(rootObj.transform, PhantomWitchConfig.TeleportExpandRadius * 1.2f, 28f, rootLifetime);
                    CreateBillboardQuad(rootObj.transform, 0.28f, 0.28f, new Vector3(0f, 0.95f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.38f), true);
                    PhantomWitchScytheSwingFx.SpawnSmokeBurst(
                        rootObj.transform,
                        new Vector3(0f, 0.95f, 0f),
                        Quaternion.identity,
                        0.95f,
                        0.55f);
                }
                else
                {
                    CreateEdgeDissolveBurst(rootObj.transform, 42, 0.46f, 0.7f, PhantomWitchConfig.VioletVoidMid);
                    CreateGroundStain(rootObj.transform, 1.6f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.12f), 1.5f);
                    CreateSoulMistEmitter(rootObj.transform, 0.90f, 9f, 0.8f, 1.2f, false, 0.03f);
                    CreateStardustEmitter(rootObj.transform, 1.8f, 28f, rootLifetime);
                    PhantomWitchScytheSwingFx.SpawnSmokeBurst(
                        rootObj.transform,
                        new Vector3(0f, 0.95f, 0f),
                        Quaternion.identity,
                        1.15f,
                        0.95f);
                }
            });

            AttachTransientRequiemLine(
                root,
                isAppear ? 1.05f : 1.25f,
                isAppear ? 0.18f : 0.24f,
                isAppear,
                WithAlpha(PhantomWitchConfig.SilverAshCore, 0.80f));
            AttachTransientSilverCrossFlash(root, isAppear ? 0.28f : 0.38f, 0.98f, !isAppear, isAppear ? 0.18f : 0.22f);
            return root;
        }

        internal static GameObject CreateTrackedTeleportMarkerEffect(Vector3 position, float duration)
        {
            float safeDuration = Mathf.Max(duration, PhantomWitchConfig.BlinkTrackedMarkerFxDuration);
            GameObject root = CreateRoot("PW_BlinkTrackedMarkerFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 0.22f, 0f), PhantomWitchConfig.VioletVoidCore, 4.2f, 5.5f, safeDuration);
            PhantomWitchScytheSwingFx.SpawnSmokeBurst(
                root.transform,
                new Vector3(0f, 0.95f, 0f),
                Quaternion.identity,
                1.9f,
                safeDuration);

            GameObject auraAnchor = new GameObject("PW_BlinkTrackedAuraAnchor");
            auraAnchor.transform.SetParent(root.transform, false);
            auraAnchor.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(auraAnchor);
            RetintTeleportMarkerAura(auraAnchor, 2.2f);

            CreateBrokenRing(root.transform, 0.56f, 0.04f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.88f), 0.06f, ResolveRingSegments(), 0.82f, 0.01f);
            CreatePartialRing(root.transform, 0.40f, 0.03f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.74f), 0.10f, ResolveRingSegments(), 0.68f, 0.008f);
            CreateSoulMistEmitter(root.transform, 0.58f, 12f, 0.8f, 1.2f, false, 0.08f);
            CreateStardustEmitter(root.transform, 0.85f, 24f, safeDuration);
            CreateBillboardQuad(root.transform, 0.24f, 0.24f, new Vector3(0f, 0.08f, 0f), WithAlpha(PhantomWitchConfig.VioletVoidVeil, 0.42f), true);

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(safeDuration, Mathf.Min(0.22f, safeDuration * 0.4f));
            Object.Destroy(root, safeDuration);
            return root;
        }

        internal static GameObject CreateTrackedTeleportFlashEffect(Vector3 position)
        {
            float duration = Mathf.Max(0.18f, PhantomWitchConfig.BlinkTrackedFlashLeadDuration + 0.08f);
            GameObject root = CreateRoot("PW_BlinkTrackedFlashFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 0.18f, 0f), PhantomWitchConfig.VioletVoidCore, 5.2f, 11f, duration);
            CreateEdgeDissolveBurst(root.transform, 28, 0.34f, 0.32f, PhantomWitchConfig.VioletVoidCore);
            CreateStardustEmitter(root.transform, 0.65f, 32f, duration);
            CreateBillboardQuad(root.transform, 0.36f, 0.36f, new Vector3(0f, 0.12f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.62f), true);
            CreateSilverCrossFlash(root.transform, 0.38f, 0.96f, false, duration);

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, duration);
            Object.Destroy(root, duration);
            return root;
        }

        internal static GameObject CreateCurseAuraEffect(Vector3 position, float radius)
        {
            GameObject root = GetOrBuildVfx("PW_CurseAuraFX", position, PhantomWitchConfig.CurseAuraFxDuration, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.8f, 0f), PhantomWitchConfig.VioletVoidMid, radius * 1.4f, 6.0f);
                CreateGroundStain(rootObj.transform, radius * 2.6f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.14f), PhantomWitchConfig.CurseAuraFxDuration);

                PhantomWitchFlatRingMesh ring = CreateRing(rootObj.transform, radius, 0.06f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.88f), 0.07f);
                PhantomWitchExpandRing expand = ring.gameObject.AddComponent<PhantomWitchExpandRing>();
                expand.Configure(radius, 0.25f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.88f), 0.06f);
                CreateBrokenRing(rootObj.transform, radius * 0.92f, 0.04f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.72f), 0.07f, ResolveRingSegments(), 0.85f, 0.008f);

                CreateRuneFlashSpawner(rootObj.transform, radius * 0.90f, 8, 0.10f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.72f));
                CreateSoulMistEmitter(rootObj.transform, radius * 0.78f, 10f, 1.0f, 1.8f, true, 0.03f);
                CreateSoulFlameEmitter(rootObj.transform, new Vector3(0f, 0.14f, 0f), 6f, 0.9f, 1.4f, 0.18f, 0.42f, 0.08f, 0.13f, false, ParticleSystemShapeType.Circle);
                CreateStardustEmitter(rootObj.transform, radius * 1.2f, 24f, PhantomWitchConfig.CurseAuraFxDuration);
                CreateBillboardQuad(rootObj.transform, 0.22f, 0.22f, new Vector3(0f, 0.7f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.52f), true);

                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(PhantomWitchConfig.CurseAuraFxDuration, 0.45f);
            });
            AttachTransientRequiemLine(root, Mathf.Max(1.1f, radius * 0.42f), 0.26f, true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.86f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.26f, radius * 0.12f), 0.86f, false, 0.22f);
            return root;
        }

        internal static GameObject CreateScytheSweepEffect(Vector3 position, Vector3 forward, float radius, float halfAngle)
        {
            forward = NormalizeFlatForward(forward);
            PlayBossScytheSwingOverlay(position + Vector3.up * 0.18f, forward, radius * 1.02f);

            GameObject root = GetOrBuildVfx("PW_ScytheSweepFX", position, 0.72f, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.5f, 0f), PhantomWitchConfig.SilverAshCore, radius * 1.45f, 4.8f);

                PhantomWitchFlatPathMesh mainArc = CreateArc(rootObj.transform, radius * 0.18f, halfAngle, forward, 0.075f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.98f), 0.14f);
                PhantomWitchExpandArc mainExpand = mainArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                mainExpand.Configure(radius * 0.18f, radius, halfAngle, forward, 0.16f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.98f), 0.075f);

                PhantomWitchFlatPathMesh ghostArcA = CreateArc(rootObj.transform, radius * 0.15f, halfAngle * 0.92f, forward, 0.038f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.70f), 0.18f);
                PhantomWitchExpandArc ghostExpandA = ghostArcA.gameObject.AddComponent<PhantomWitchExpandArc>();
                ghostExpandA.Configure(radius * 0.15f, radius * 0.96f, halfAngle * 0.92f, forward, 0.22f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.70f), 0.038f);

                PhantomWitchFlatPathMesh ghostArcB = CreateArc(rootObj.transform, radius * 0.10f, halfAngle * 0.84f, forward, 0.026f, WithAlpha(PhantomWitchConfig.VioletVoidVeil, 0.52f), 0.22f);
                PhantomWitchExpandArc ghostExpandB = ghostArcB.gameObject.AddComponent<PhantomWitchExpandArc>();
                ghostExpandB.Configure(radius * 0.10f, radius * 0.90f, halfAngle * 0.84f, forward, 0.26f, WithAlpha(PhantomWitchConfig.VioletVoidVeil, 0.52f), 0.026f);

                CreateArcRuneFlashes(rootObj.transform, radius * 0.78f, halfAngle, forward, 14, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.9f));
                CreateSoulMistEmitter(rootObj.transform, radius * 0.55f, 14f, 0.8f, 1.4f, true, 0.03f);
                CreateStardustEmitter(rootObj.transform, radius * 0.8f, 30f, 0.7f);
                CreateBrokenRing(rootObj.transform, radius * 0.60f, 0.032f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.58f), 0.07f, ResolveRingSegments(), 0.82f, 0.01f);

                Vector3 start = ArcPoint(forward, radius * 0.28f, -halfAngle * Mathf.Deg2Rad);
                Vector3 end = ArcPoint(forward, radius, halfAngle * Mathf.Deg2Rad);
                CreateSoulTendril(rootObj.transform, start + new Vector3(0f, 0.08f, 0f), end + new Vector3(0f, 0.08f, 0f), 0.22f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.90f), true);

                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(0.72f, 0.32f);
            });
            AttachTransientRequiemLine(root, Mathf.Max(1.1f, radius * 0.33f), 0.20f, true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.84f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.22f, radius * 0.10f), 0.78f, false, 0.18f);
            return root;
        }

        internal static GameObject CreateHeavySlashEffect(Vector3 position, Vector3 forward, float radius)
        {
            forward = NormalizeFlatForward(forward);
            PlayBossScytheSwingOverlay(position + Vector3.up * 0.24f, forward, radius * 1.18f);

            GameObject root = GetOrBuildVfx("PW_HeavySlashFX", position, 0.78f, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.8f, 0f), PhantomWitchConfig.VioletVoidCore, radius * 1.75f, 8.2f, 1.5f);

                PhantomWitchFlatPathMesh burstRing = CreatePartialRing(rootObj.transform, radius * 0.92f, 0.065f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.88f), 0.10f, ResolveRingSegments(), 0.84f, 0.008f);
                PhantomWitchRingSpin spin = burstRing.gameObject.AddComponent<PhantomWitchRingSpin>();
                spin.rotationSpeed = 8f;

                PhantomWitchFlatPathMesh slashArc = CreateArc(rootObj.transform, 0.01f, 40f, forward, 0.10f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.96f), 0.14f);
                PhantomWitchExpandArc arcExpand = slashArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                arcExpand.Configure(0.01f, radius, 40f, forward, 0.18f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.96f), 0.10f);
                CreateBrokenRing(rootObj.transform, radius * 0.72f, 0.04f, WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.72f), 0.09f, ResolveRingSegments(), 0.88f, 0.008f);
                CreateSoulTendril(rootObj.transform, new Vector3(0f, 0.10f, 0f), forward * radius, 0.26f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.92f), true);

                CreateRuneFlashSpawner(rootObj.transform, radius * 0.72f, 14, 0.12f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.78f));
                CreateSoulMistEmitter(rootObj.transform, radius * 0.62f, 14f, 0.9f, 1.5f, true, 0.04f);
                CreateStardustEmitter(rootObj.transform, radius * 0.88f, 32f, 0.8f);
                CreateBillboardQuad(rootObj.transform, 0.18f, 0.18f, new Vector3(0f, 0.9f, 0f), WithAlpha(PhantomWitchConfig.GhostBreathCore, 0.44f), true);

                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(0.78f, 0.38f);
            });
            AttachTransientRequiemLine(root, Mathf.Max(1.35f, radius * 0.42f), 0.24f, true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.90f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.28f, radius * 0.12f), 0.96f, true, 0.20f);
            return root;
        }

        internal static GameObject CreateWraithWindupOutlineEffect(Vector3 position, Vector3 forward, float radius, float duration)
        {
            forward = NormalizeFlatForward(forward);
            GameObject root = GetOrBuildVfx("PW_WraithWindupOutlineFX", position, duration, (rootObj) =>
            {
                float safeRadius = Mathf.Max(radius, 0.8f);
                float safeDuration = Mathf.Max(duration, 0.1f);
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.55f, 0f), PhantomWitchConfig.SilverAshCore, safeRadius * 1.25f, 3.8f, safeDuration);

                PhantomWitchFlatPathMesh outlineArc = CreateArc(rootObj.transform, safeRadius * 0.42f, 52f, forward, 0.055f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.92f), 0.12f);
                PhantomWitchExpandArc outlineExpand = outlineArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                outlineExpand.Configure(safeRadius * 0.42f, safeRadius, 52f, forward, safeDuration * 0.55f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.92f), 0.055f);

                PhantomWitchFlatPathMesh ghostArc = CreateArc(rootObj.transform, safeRadius * 0.30f, 58f, forward, 0.032f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.68f), 0.15f);
                PhantomWitchExpandArc ghostExpand = ghostArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                ghostExpand.Configure(safeRadius * 0.30f, safeRadius * 0.92f, 58f, forward, safeDuration * 0.70f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.68f), 0.032f);

                CreateArcRuneFlashes(rootObj.transform, safeRadius * 0.82f, 58f, forward, 8, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.78f));
                CreateSoulMistEmitter(rootObj.transform, safeRadius * 0.52f, 7f, 0.7f, 1.1f, true, 0.02f);
                CreateStardustEmitter(rootObj.transform, safeRadius * 0.85f, 18f, safeDuration);

                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(safeDuration, Mathf.Min(safeDuration * 0.45f, 0.18f));
            });
            AttachTransientRequiemLine(root, Mathf.Max(0.95f, radius * 0.34f), Mathf.Min(Mathf.Max(duration * 0.35f, 0.18f), 0.30f), true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.80f));
            return root;
        }

        internal static GameObject CreateSemiStealthWindupEffect(Transform bossBody)
        {
            if (bossBody == null)
            {
                return null;
            }

            GameObject root = new GameObject("PW_SemiStealthWindupFX");
            root.transform.SetParent(bossBody, false);
            root.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            PhantomWitchFxRuntime.RegisterEffectRoot(root);

            CreateEdgeDissolveLoop(root.transform, 0.55f, 7f, 0.45f);
            CreateSoulMistEmitter(root.transform, 0.42f, 3.5f, 0.7f, 1.1f, false, 0.02f);
            CreateBillboardQuad(root.transform, 0.18f, 0.18f, new Vector3(0f, 0.18f, 0f), WithAlpha(PhantomWitchConfig.SilverAshCore, 0.10f), true);
            return root;
        }

        internal static GameObject CreateSummonCircleEffect(Vector3 position)
        {
            GameObject root = GetOrBuildVfx("PW_SummonCircleFX", position, PhantomWitchConfig.SummonCircleFxDuration, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidMid, PhantomWitchConfig.SummonCircleRadius * 1.8f, 5.8f);
                CreateAltarProjection(rootObj.transform, PhantomWitchConfig.SummonCircleRadius, 0.02f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.52f), PhantomWitchConfig.SummonCircleFxDuration, false);
                CreateFakeWarpField(rootObj.transform, PhantomWitchConfig.SummonCircleRadius * 0.95f, 0.10f, PhantomWitchConfig.SummonCircleFxDuration);
                
                CreateBrokenRing(rootObj.transform, PhantomWitchConfig.SummonCircleRadius, 0.045f, WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.92f), 0.09f, ResolveRingSegments(), 0.85f, 0.01f);
                PhantomWitchFlatPathMesh reverseRing = CreatePartialRing(rootObj.transform, PhantomWitchConfig.SummonCircleRadius * 0.8f, 0.035f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.82f), 0.14f, ResolveRingSegments(), 0.7f, 0.005f);
                reverseRing.gameObject.AddComponent<PhantomWitchRingSpin>().rotationSpeed = -6f;
                CreateSoulMistEmitter(rootObj.transform, PhantomWitchConfig.SummonCircleRadius * 0.78f, 10f, 0.9f, 1.5f, true, 0.05f);

                CreateSoulFlameEmitter(rootObj.transform, Vector3.zero, 7f, 1.2f, 1.8f, 0.25f, 0.55f, 0.09f, 0.14f, false, ParticleSystemShapeType.Circle);
                CreateStardustEmitter(rootObj.transform, PhantomWitchConfig.SummonCircleRadius * 1.1f, 24f, PhantomWitchConfig.SummonCircleFxDuration);
                CreateRuneFlashSpawner(rootObj.transform, PhantomWitchConfig.SummonCircleRadius * 0.78f, 7, 0.12f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.76f));

                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(PhantomWitchConfig.SummonCircleFxDuration, 0.55f);
            });
            AttachTransientRequiemLine(root, 1.45f, 0.28f, true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.86f));
            AttachTransientSilverCrossFlash(root, 0.38f, 1.05f, true, 0.24f);
            return root;
        }

        internal static GameObject CreateMinionSpawnEffect(Vector3 position)
        {
            GameObject root = GetOrBuildVfx("PW_MinionSpawnFX", position, PhantomWitchConfig.MinionSpawnFxDuration, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.7f, 0f), PhantomWitchConfig.VioletVoidMid, 2.4f, 3.6f, PhantomWitchConfig.MinionSpawnFxDuration);
                CreateTearLine(rootObj.transform, 0.9f, WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.88f), 0.05f);
                CreateSoulTendril(rootObj.transform, Vector3.zero, new Vector3(0f, 1.5f, 0f), 0.46f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.90f), true);
                CreateSoulFlameEmitter(rootObj.transform, new Vector3(0f, 0.02f, 0f), 6f, 1.0f, 1.4f, 0.12f, 0.32f, 0.07f, 0.11f, false, ParticleSystemShapeType.Cone);
                CreateSoulMistEmitter(rootObj.transform, 0.58f, 7f, 0.8f, 1.2f, true, 0.03f);
                CreateStardustEmitter(rootObj.transform, 1.4f, 18f, PhantomWitchConfig.MinionSpawnFxDuration);
            });
            AttachTransientSilverCrossFlash(root, 0.22f, 0.95f, false, 0.18f);
            return root;
        }

        internal static GameObject CreateDamageHitEffect(Vector3 position)
        {
            return GetOrBuildVfx("PW_HitFX", position, 0.8f, (root) =>
            {
                CreateSilkTearBurst(root.transform, 4, 0.18f, 0.28f, WithAlpha(PhantomWitchConfig.SilverAshMid, 0.9f), WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.65f), true, 0.24f);
                GameObject bloodAccent = CreateBillboardQuad(root.transform, 0.08f, 0.08f, new Vector3(0f, 0.12f, 0f), WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.75f), true);
                PhantomWitchFadeDestroy bloodFade = bloodAccent.AddComponent<PhantomWitchFadeDestroy>();
                bloodFade.Configure(0.12f, 0.12f);
                CreateGroundStain(root.transform, 0.45f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.08f), 0.8f);
                CreateStardustEmitter(root.transform, 0.8f, 10f, 0.5f);
            });
        }

        internal static GameObject CreatePhaseTransitionEffect(Vector3 position)
        {
            GameObject root = CreateRoot("PW_PhaseTransitionFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 1.5f, 0f), PhantomWitchConfig.SilverAshCore, 6f, 8f);
            CreateSilkTearBurst(root.transform, 5, 0.22f, 0.42f, WithAlpha(PhantomWitchConfig.SilverAshMid, 0.9f), WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.8f), true, 0.6f);
            
            // Nested counter-rotating phase disruption rings
            CreateBrokenRing(root.transform, 2.8f, 0.045f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.85f), 0.1f, ResolveRingSegments(), 0.9f, 0.015f);
            CreateBrokenRing(root.transform, 2.4f, 0.03f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.75f), 0.15f, ResolveRingSegments(), 0.7f, 0.008f);
            PhantomWitchFlatPathMesh innerRing = CreatePartialRing(root.transform, 1.8f, 0.035f, WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.7f), 0.2f, ResolveRingSegments(), 0.8f, 0f);
            innerRing.gameObject.AddComponent<PhantomWitchRingSpin>().rotationSpeed = -8f;
            
            CreateRequiemLine(root.transform, 2.1f, 0.35f, true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.75f));
            CreateSilverCrossFlash(root.transform, 0.75f, 1.05f, inverted: true, duration: 0.30f);
            CreateSoulMistEmitter(root.transform, 2.8f, 16f, 1.2f, 1.8f, true, 0.05f);
            CreateStardustEmitter(root.transform, 3.5f, 25f, PhantomWitchConfig.PhaseTransitionFxDuration);
            CreateAltarProjection(root.transform, 2.4f, 0.02f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.35f), 0.8f, true);
            CreateFakeWarpField(root.transform, 2.6f, 0.08f, 1.0f);

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(1.25f, 0.55f);
            Object.Destroy(root, PhantomWitchConfig.PhaseTransitionFxDuration);
            return root;
        }

        internal static GameObject CreateSpawnEffect(Vector3 position, float duration)
        {
            GameObject root = CreateRoot("PW_SpawnFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidCore, 4f, 4f);
            CreateGroundStain(root.transform, 1.8f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.08f), duration);
            CreateRuneFlashSpawner(root.transform, 1.2f, 4, 0.10f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.55f));
            CreateSoulFlameEmitter(root.transform, new Vector3(0f, 0.2f, 0f), 5f, 1.0f, 1.5f, 0.25f, 0.55f, 0.08f, 0.12f, false, ParticleSystemShapeType.Cone);
            CreateStardustEmitter(root.transform, 1.5f, 15f, duration);

            Object.Destroy(root, duration);
            return root;
        }

        internal static GameObject CreateDeathEffect(Vector3 position)
        {
            GameObject root = CreateRoot("PW_DeathFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 1.2f, 0f), PhantomWitchConfig.GhostBreathCore, 6f, 7f);
            CreateEdgeDissolveBurst(root.transform, 48, 0.45f, 0.9f, PhantomWitchConfig.GhostBreathCore);
            CreateRequiemLine(root.transform, 2.0f, 0.32f, false, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.8f));
            CreateSoulTendril(root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 1.9f, 0f), 0.52f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.85f), true);
            CreateSilverCrossFlash(root.transform, 0.42f, 1.1f, false, 0.30f);
            CreateSoulMistEmitter(root.transform, 1.1f, 8f, 1.4f, 2.0f, true, 0.04f);
            CreateSoulFlameEmitter(root.transform, new Vector3(0f, 0.5f, 0f), 6f, 1.1f, 1.6f, 0.18f, 0.38f, 0.07f, 0.11f, false, ParticleSystemShapeType.Cone);
            CreateStardustEmitter(root.transform, 3f, 35f, PhantomWitchConfig.DeathFxDuration);
            CreateGroundStain(root.transform, PhantomWitchConfig.DeathFxRadius * 1.1f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.12f), 1.0f);

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(PhantomWitchConfig.DeathFxDuration, 0.8f);
            Object.Destroy(root, PhantomWitchConfig.DeathFxDuration);
            return root;
        }

        internal static GameObject CreateCurseRealmVisual(Vector3 origin, float radius, float duration)
        {
            GameObject root = CreateRoot("PhantomWitch_CurseRealm_Visual", origin);
            CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidMid, radius * 1.5f, 4f);
            CreateGroundStain(root.transform, radius * 2.4f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.12f), duration);
            CreateBrokenRing(root.transform, radius, 0.035f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.65f), 0.08f, ResolveRingSegments(), 0.85f, 0.02f);
            CreateSoulMistEmitter(root.transform, radius * 0.95f, 12f, 1.6f, 2.4f, true, 0.03f);
            CreateFakeWarpField(root.transform, radius * 0.9f, 0.07f, duration);
            CreateRealmRuneFlashSpawner(root.transform, radius * 0.8f, duration);
            CreateSoulFlameEmitter(root.transform, new Vector3(0f, 0.12f, 0f), 4f, 1.2f, 1.8f, 0.22f, 0.45f, 0.10f, 0.16f, false, ParticleSystemShapeType.Cone);

            PhantomWitchCurseRealmFader fader = root.AddComponent<PhantomWitchCurseRealmFader>();
            fader.Initialize(duration);

            Object.Destroy(root, duration);
            return root;
        }

        internal static GameObject CreateCurseRealmWarningCircle(Vector3 origin, float radius, float duration)
        {
            GameObject root = CreateRoot("PhantomWitch_CurseRealmWarning", origin);
            CreatePointLight(root.transform, new Vector3(0f, 0.15f, 0f), PhantomWitchConfig.SilverAshCore, radius * 1.45f, 4.8f, duration);
            CreateGroundStain(root.transform, radius * 2.35f, WithAlpha(PhantomWitchConfig.BloodRoseVeil, 0.13f), duration);

            PhantomWitchFlatRingMesh outerRing = CreateRing(
                root.transform,
                Mathf.Max(radius, PhantomWitchConfig.CurseRealmWarningMinRadius),
                0.055f,
                WithAlpha(PhantomWitchConfig.SilverAshCore, 0.92f),
                0.04f);
            PhantomWitchShrinkRing shrink = outerRing.gameObject.AddComponent<PhantomWitchShrinkRing>();
            shrink.Configure(Mathf.Max(radius, PhantomWitchConfig.CurseRealmWarningMinRadius), duration, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.92f), 0.055f);

            PhantomWitchFlatRingMesh innerRing = CreateRing(
                root.transform,
                Mathf.Max(radius * 0.72f, PhantomWitchConfig.CurseRealmWarningMinRadius),
                0.035f,
                WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.82f),
                0.045f);
            PhantomWitchShrinkRing innerShrink = innerRing.gameObject.AddComponent<PhantomWitchShrinkRing>();
            innerShrink.Configure(Mathf.Max(radius * 0.72f, PhantomWitchConfig.CurseRealmWarningMinRadius), duration, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.82f), 0.035f);

            CreateBrokenRing(root.transform, Mathf.Max(radius * 0.88f, 0.5f), 0.032f, WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.70f), 0.06f, ResolveRingSegments(), 0.86f, 0.008f);
            CreateRuneFlashSpawner(root.transform, Mathf.Max(radius * 0.65f, 0.6f), 6, 0.12f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.70f));
            CreateSoulMistEmitter(root.transform, Mathf.Max(radius * 0.72f, 0.6f), 8f, 0.8f, 1.2f, true, 0.04f);
            CreateStardustEmitter(root.transform, Mathf.Max(radius * 0.95f, 0.8f), 18f, duration);
            CreateBillboardQuad(root.transform, 0.16f, 0.16f, new Vector3(0f, 0.12f, 0f), WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.42f), true);
            AttachTransientRequiemLine(root, Mathf.Max(1.05f, radius * 0.30f), Mathf.Min(duration, 0.22f), true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.82f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.22f, radius * 0.10f), 0.84f, true, Mathf.Min(duration, 0.18f));

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, Mathf.Min(duration, 0.2f));
            Object.Destroy(root, duration);
            return root;
        }

        internal static void ClearCache()
        {
            foreach (var kv in VfxPools)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                while (kv.Value.Count > 0)
                {
                    GameObject pooledRoot = kv.Value.Pop();
                    if (pooledRoot != null)
                    {
                        Object.Destroy(pooledRoot);
                    }
                }
            }
            VfxPools.Clear();

            if (cachedLineMaterial != null)
            {
                Object.Destroy(cachedLineMaterial);
                cachedLineMaterial = null;
            }

            if (cachedGroundLineMaterial != null)
            {
                Object.Destroy(cachedGroundLineMaterial);
                cachedGroundLineMaterial = null;
            }

            if (cachedQuadMaterial != null)
            {
                Object.Destroy(cachedQuadMaterial);
                cachedQuadMaterial = null;
            }

            if (cachedTrailMaterial != null)
            {
                Object.Destroy(cachedTrailMaterial);
                cachedTrailMaterial = null;
            }

            if (cachedAltarMaterial != null)
            {
                Object.Destroy(cachedAltarMaterial);
                cachedAltarMaterial = null;
            }

            if (cachedBrokenAltarMaterial != null)
            {
                Object.Destroy(cachedBrokenAltarMaterial);
                cachedBrokenAltarMaterial = null;
            }

            if (cachedQuadMesh != null)
            {
                Object.Destroy(cachedQuadMesh);
                cachedQuadMesh = null;
            }

            if (cachedAltarProjectionTexture != null)
            {
                Object.Destroy(cachedAltarProjectionTexture);
                cachedAltarProjectionTexture = null;
            }

            if (cachedBrokenAltarProjectionTexture != null)
            {
                Object.Destroy(cachedBrokenAltarProjectionTexture);
                cachedBrokenAltarProjectionTexture = null;
            }
        }

        private static GameObject CreateRoot(string name, Vector3 position)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;
            PhantomWitchFxRuntime.RegisterEffectRoot(root);
            return root;
        }

        private static int ResolveRingSegments()
        {
            return ResolveAdaptiveCount(64, 32, 20);
        }

        private static int ResolveAdaptiveCount(int full, int reduced, int minimal)
        {
            return ResolveAdaptiveCount(PhantomWitchFxRuntime.CurrentDetailLevel, full, reduced, minimal);
        }

        private static int ResolveAdaptiveCount(PhantomWitchFxDetailLevel detailLevel, int full, int reduced, int minimal)
        {
            switch (detailLevel)
            {
                case PhantomWitchFxDetailLevel.Minimal:
                    return Mathf.Max(0, minimal);
                case PhantomWitchFxDetailLevel.Reduced:
                    return Mathf.Max(0, reduced);
                default:
                    return Mathf.Max(0, full);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        private static Vector3 NormalizeFlatForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        private static void PlayBossScytheSwingOverlay(Vector3 position, Vector3 forward, float radius)
        {
            float rangeScale = 1f;
            if (PhantomWitchScytheConfig.BaseAttackRange > 0.01f)
            {
                rangeScale = Mathf.Max(0.9f, radius / PhantomWitchScytheConfig.BaseAttackRange);
            }

            PhantomWitchScytheSwingFx.PlayAt(position, Quaternion.LookRotation(NormalizeFlatForward(forward)), rangeScale);
        }

        private static void AttachTransientSilverCrossFlash(GameObject root, float size, float yOffset, bool inverted, float duration)
        {
            if (root == null || duration <= 0f)
            {
                return;
            }

            CreateSilverCrossFlash(root.transform, size, yOffset, inverted, duration);
        }

        private static void AttachTransientRequiemLine(GameObject root, float height, float duration, bool rise, Color color)
        {
            if (root == null || duration <= 0f)
            {
                return;
            }

            CreateRequiemLine(root.transform, height, duration, rise, color);
        }

        private static Shader ResolveTransparentShader()
        {
            return Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Additive")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Standard");
        }

        private static GameObject CreatePointLight(Transform parent, Vector3 localPos, Color color, float range, float intensity, float duration = 3f)
        {
            GameObject lightGo = new GameObject("PW_PointLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            
            PhantomWitchLightPulse pulser = lightGo.AddComponent<PhantomWitchLightPulse>();
            pulser.Configure(intensity, range, duration);
            
            return lightGo;
        }

        private static void RetintTeleportMarkerAura(GameObject root, float densityMultiplier)
        {
            if (root == null)
            {
                return;
            }

            Color coreColor = WithAlpha(PhantomWitchConfig.VioletVoidCore, 0.92f);
            Color fadeColor = WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.55f);
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if (ps == null)
                {
                    continue;
                }

                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(coreColor, fadeColor);
                main.startSizeMultiplier *= 1.25f;

                var emission = ps.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(Mathf.Max(12f, 8f * densityMultiplier));

                var colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    Gradient gradient = new Gradient();
                    gradient.SetKeys(
                        new GradientColorKey[]
                        {
                            new GradientColorKey(coreColor, 0f),
                            new GradientColorKey(PhantomWitchConfig.VioletVoidVeil, 0.55f),
                            new GradientColorKey(fadeColor, 1f)
                        },
                        new GradientAlphaKey[]
                        {
                            new GradientAlphaKey(0f, 0f),
                            new GradientAlphaKey(0.9f, 0.12f),
                            new GradientAlphaKey(0.45f, 0.65f),
                            new GradientAlphaKey(0f, 1f)
                        });
                    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                }
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterials == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (material == null)
                    {
                        continue;
                    }

                    if (material.HasProperty("_Color"))
                    {
                        material.color = coreColor;
                    }
                    if (material.HasProperty("_TintColor"))
                    {
                        material.SetColor("_TintColor", coreColor);
                    }
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", coreColor * 0.55f);
                    }
                }
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                {
                    continue;
                }

                light.color = coreColor;
                light.range = Mathf.Max(light.range, 1.6f);
                light.intensity = Mathf.Max(light.intensity, 1.8f);
            }
        }

        private static Material GetLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private static Material GetGroundLineMaterial()
        {
            if (cachedGroundLineMaterial != null)
            {
                return cachedGroundLineMaterial;
            }

            Shader shader = ResolveTransparentShader();
            if (shader == null)
            {
                return null;
            }

            cachedGroundLineMaterial = new Material(shader);
            cachedGroundLineMaterial.name = "PW_Redesign_GroundLine";
            cachedGroundLineMaterial.enableInstancing = true;
            if (cachedGroundLineMaterial.HasProperty("_MainTex"))
            {
                cachedGroundLineMaterial.mainTexture = Texture2D.whiteTexture;
            }
            cachedGroundLineMaterial.renderQueue = 3000;
            return cachedGroundLineMaterial;
        }

        internal static Material GetSharedLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private static Material GetQuadMaterial()
        {
            return PhantomWitchAssetManager.GetQuadMaterial();
        }

        private static Material GetTrailMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private static Material GetAltarMaterial(bool broken)
        {
            if (broken)
            {
                if (cachedBrokenAltarMaterial == null)
                {
                    cachedBrokenAltarMaterial = new Material(GetQuadMaterial());
                    cachedBrokenAltarMaterial.name = "PW_BrokenAltarProjection";
                    cachedBrokenAltarMaterial.SetTexture(MainTexPropertyId, GetBrokenAltarProjectionTexture());
                }

                return cachedBrokenAltarMaterial;
            }

            if (cachedAltarMaterial == null)
            {
                cachedAltarMaterial = new Material(GetQuadMaterial());
                cachedAltarMaterial.name = "PW_AltarProjection";
                cachedAltarMaterial.SetTexture(MainTexPropertyId, GetAltarProjectionTexture());
            }

            return cachedAltarMaterial;
        }

        private static Mesh GetQuadMesh()
        {
            if (cachedQuadMesh != null)
            {
                return cachedQuadMesh;
            }

            cachedQuadMesh = new Mesh();
            cachedQuadMesh.name = "PW_Redesign_QuadMesh";
            cachedQuadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            cachedQuadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            cachedQuadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            cachedQuadMesh.RecalculateNormals();
            return cachedQuadMesh;
        }

        private static GameObject CreateGroundStain(Transform parent, float scale, Color color, float duration)
        {
            GameObject stain = CreateFlatQuad(parent, "GroundStain", scale, 0.02f, color);
            PhantomWitchFadeDestroy fade = stain.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, Mathf.Min(0.7f, duration));
            return stain;
        }

        private static GameObject CreateFlatQuad(Transform parent, string name, float scale, float yOffset, Color color)
        {
            GameObject quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, yOffset, 0f);
            quad.transform.localScale = new Vector3(scale, 1f, scale);

            MeshFilter filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = GetQuadMesh();

            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetQuadMaterial();
            PhantomWitchFxRenderUtil.SetRendererColor(renderer, color);
            return quad;
        }

        private static GameObject CreateBillboardQuad(Transform parent, float width, float height, Vector3 localPosition, Color color, bool addBillboard)
        {
            GameObject quad = new GameObject("BillboardQuad");
            MeshFilter filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = GetQuadMesh();
            
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetQuadMaterial();
            PhantomWitchFxRenderUtil.SetRendererColor(renderer, color);

            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = new Vector3(width, height, 1f);

            if (addBillboard)
            {
                quad.AddComponent<PhantomWitchBillboard>();
            }

            return quad;
        }

        private static PhantomWitchFlatRingMesh CreateRing(Transform parent, float radius, float width, Color color, float yOffset)
        {
            GameObject ring = new GameObject("Ring");
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, Mathf.Max(yOffset, 0.02f), 0f);

            PhantomWitchFlatRingMesh ringMesh = ring.AddComponent<PhantomWitchFlatRingMesh>();
            ringMesh.Configure(ResolveRingSegments(), radius, width, GetGroundLineMaterial(), color);
            return ringMesh;
        }

        private static PhantomWitchFlatPathMesh CreateArc(Transform parent, float radius, float halfAngle, Vector3 forward, float width, Color color, float yOffset)
        {
            GameObject arc = new GameObject("Arc");
            arc.transform.SetParent(parent, false);
            arc.transform.localPosition = new Vector3(0f, Mathf.Max(yOffset, 0.02f), 0f);

            int segments = ResolveAdaptiveCount(32, 18, 10);
            PhantomWitchFlatPathMesh pathMesh = arc.AddComponent<PhantomWitchFlatPathMesh>();
            pathMesh.Configure(BuildArcPoints(radius, halfAngle, forward, segments), width, GetGroundLineMaterial(), color);
            return pathMesh;
        }

        private static PhantomWitchFlatPathMesh CreatePartialRing(Transform parent, float radius, float width, Color color, float yOffset, int segments, float coverage, float jitter)
        {
            GameObject ring = new GameObject("BrokenRing");
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, Mathf.Max(yOffset, 0.02f), 0f);

            PhantomWitchFlatPathMesh pathMesh = ring.AddComponent<PhantomWitchFlatPathMesh>();
            pathMesh.Configure(BuildPartialRingPoints(radius, segments, coverage, jitter), width, GetGroundLineMaterial(), color);
            return pathMesh;
        }

        private static void CreateBrokenRing(Transform parent, float radius, float width, Color color, float yOffset, int segments, float coverage, float jitter)
        {
            PhantomWitchFlatPathMesh line = CreatePartialRing(parent, radius, width, color, yOffset, segments, coverage, jitter);
            PhantomWitchRingSpin spin = line.gameObject.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = 6f;
        }

        private static Vector3[] BuildArcPoints(float radius, float halfAngle, Vector3 forward, int segments)
        {
            segments = Mathf.Max(1, segments);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            float baseAngle = Mathf.Atan2(forward.x, forward.z);
            float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
            float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = Mathf.Lerp(startAngle, endAngle, t);
                points[i] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            }

            return points;
        }

        private static Vector3[] BuildPartialRingPoints(float radius, int segments, float coverage, float jitter)
        {
            segments = Mathf.Max(1, segments);
            Vector3[] points = new Vector3[segments + 1];
            float totalAngle = Mathf.PI * 2f * coverage;
            float startAngle = -totalAngle * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = startAngle + totalAngle * t;
                float irregularRadius = radius + Mathf.Sin(t * Mathf.PI * 3f) * jitter;
                points[i] = new Vector3(Mathf.Cos(angle) * irregularRadius, 0f, Mathf.Sin(angle) * irregularRadius);
            }

            return points;
        }

        private static void CreateRealmRuneFlashSpawner(Transform parent, float radius, float duration)
        {
            PhantomWitchRealmRuneFlashSpawner spawner = parent.gameObject.AddComponent<PhantomWitchRealmRuneFlashSpawner>();
            spawner.Configure(radius, duration);
        }

        private static void CreateRuneFlashSpawner(Transform parent, float radius, int count, float yOffset, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = ((float)i / Mathf.Max(1, count)) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.25f, 0.25f);
                CreateRuneFlash(parent, new Vector3(Mathf.Cos(angle) * radius, yOffset + UnityEngine.Random.Range(-0.05f, 0.08f), Mathf.Sin(angle) * radius), color, 0.45f);
            }
        }

        private static void CreateArcRuneFlashes(Transform parent, float radius, float halfAngle, Vector3 forward, int count, Color color)
        {
            float baseAngle = Mathf.Atan2(forward.x, forward.z);
            for (int i = 0; i < count; i++)
            {
                float t = (count == 1) ? 0.5f : (float)i / (count - 1);
                float angle = Mathf.Lerp(baseAngle - halfAngle * Mathf.Deg2Rad, baseAngle + halfAngle * Mathf.Deg2Rad, t);
                CreateRuneFlash(parent, new Vector3(Mathf.Sin(angle) * radius, 0.12f + UnityEngine.Random.Range(-0.03f, 0.05f), Mathf.Cos(angle) * radius), color, 0.4f);
            }
        }

        private static void CreateRuneFlash(Transform parent, Vector3 localPosition, Color color, float duration)
        {
            GameObject rune = new GameObject("RuneFlash");
            rune.transform.SetParent(parent, false);
            rune.transform.localPosition = localPosition;
            rune.transform.localRotation = Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-25f, 25f));

            int segmentCount = UnityEngine.Random.Range(2, 4);
            for (int i = 0; i < segmentCount; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                segment.transform.SetParent(rune.transform, false);
                LineRenderer line = segment.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = UnityEngine.Random.Range(0.018f, 0.03f);
                line.sharedMaterial = PhantomWitchVfxRedesign.GetSharedLineMaterial();
                line.startColor = color;
                line.endColor = WithAlpha(color, color.a * 0.2f);

                float width = UnityEngine.Random.Range(0.10f, 0.22f);
                float height = UnityEngine.Random.Range(-0.10f, 0.10f);
                line.SetPosition(0, new Vector3(-width, height, 0f));
                line.SetPosition(1, new Vector3(UnityEngine.Random.Range(-0.04f, 0.04f), height + UnityEngine.Random.Range(0.03f, 0.10f), 0f));
                line.SetPosition(2, new Vector3(width * UnityEngine.Random.Range(0.5f, 1f), height + UnityEngine.Random.Range(-0.08f, 0.08f), 0f));
            }

            PhantomWitchFadeDestroy fade = rune.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, duration * 0.8f);
            Object.Destroy(rune, duration);
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

    internal sealed class PhantomWitchBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                transform.rotation = camera.transform.rotation;
            }
        }
    }

    internal sealed class PhantomWitchWarpQuad : MonoBehaviour
    {
        private Vector3 origin;
        private float jitterAmplitude;
        private float rotationSpeed;
        private float duration;
        private float elapsed;

        public void Configure(float jitterAmplitude, float rotationSpeed, float duration)
        {
            origin = transform.localPosition;
            this.jitterAmplitude = jitterAmplitude;
            this.rotationSpeed = rotationSpeed;
            this.duration = duration;
            elapsed = 0f;
        }

        private void Awake()
        {
            origin = transform.localPosition;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float noiseX = Mathf.Sin(Time.time * 2.1f) * jitterAmplitude;
            float noiseZ = Mathf.Cos(Time.time * 1.7f) * jitterAmplitude;
            transform.localPosition = origin + new Vector3(noiseX, 0f, noiseZ);
            transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);

            if (duration > 0f && elapsed >= duration)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchTendrilMover : MonoBehaviour
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
            transform.localPosition = start;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.localPosition = Vector3.Lerp(start, end, eased);

            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchVerticalLineDrift : MonoBehaviour
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
            this.startPosition = transform.localPosition;
        }

        private void Awake()
        {
            startPosition = transform.localPosition;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localPosition = Vector3.Lerp(startPosition, startPosition + offset, t);
            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchPulseScale : MonoBehaviour
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
            transform.localScale = Vector3.Lerp(minScale, maxScale, pulse);
            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchRealmRuneFlashSpawner : MonoBehaviour
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
            rune.transform.SetParent(transform, false);
            rune.transform.localPosition = localPosition;
            rune.transform.localRotation = Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-20f, 20f));

            for (int i = 0; i < 3; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                segment.transform.SetParent(rune.transform, false);
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
