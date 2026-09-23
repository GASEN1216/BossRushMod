using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static partial class PhantomWitchVfxRedesign
    {
        private static Mesh cachedQuadMesh;
        private static Mesh cachedBillboardQuadMesh;
        private static Texture2D cachedAltarProjectionTexture;
        private static Texture2D cachedBrokenAltarProjectionTexture;
        private static Texture2D cachedSoftBandTexture;
        private static Texture2D cachedDashedBandTexture;
        private static Texture2D cachedSoftDiscTexture;

        private static readonly Dictionary<string, Stack<GameObject>> VfxPools = new Dictionary<string, Stack<GameObject>>();

        // ---- 2026-09-23 审美审查（VB-01/05/07/08/09）的表现口径 ----
        /// <summary>单个特效根的点光强度上限（此前 7–16，把 10 m 半径的地面洗平）。</summary>
        private const float MaxLightIntensity = 3.5f;
        /// <summary>点光照亮半径上限（米）。</summary>
        private const float MaxLightRange = 5f;
        /// <summary>统一灯色：SilverAshMid 与 VioletVoidVeil 之间的低饱和灰紫，不再用 VioletVoidCore 这种深紫把暖地面染灰。</summary>
        private static readonly Color WitchLightColor = new Color(0.65f, 0.58f, 0.72f, 1f);
        /// <summary>预警的「危险填充」色与外沿色：领域、扇形共用一套语言，玩家认得出「这圈就是等一下那一击」。</summary>
        private static readonly Color TelegraphFillColor = PhantomWitchConfig.BloodRoseMid;
        private static readonly Color TelegraphOutlineColor = PhantomWitchConfig.VioletVoidVeil;
        private static readonly Color TelegraphFlashColor = PhantomWitchConfig.SilverAshCore;
        /// <summary>
        /// 贴地环 / 弧的最小宽度（米）。柔边带两侧各羽化约 1/3，0.08 m 的带子实心部分约 0.05 m ≈ 4 px；
        /// 此前 0.026–0.055 m 的带子在相机距离下只剩 1–2 px（审查 VB-08）。
        /// </summary>
        private const float MinGroundBandWidth = 0.08f;
        /// <summary>预警结算后的淡出时长（只是表现，判定早已结算）。</summary>
        private const float TelegraphPostFade = 0.15f;
        /// <summary>细线收尖：线头 1 → 线尾 0.2，避免恒宽线头硬断（VB-08）。</summary>
        internal static readonly AnimationCurve TaperedLineWidthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));

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
            internal PhantomWitchAbilityController Owner { get; private set; }

            internal void SetOwner(PhantomWitchAbilityController owner)
            {
                if (Owner == owner) return;
                if (Owner != null) Owner.UntrackPooledEffect(gameObject);
                Owner = owner;
            }

            private void OnDestroy()
            {
                SetOwner(null);
            }

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
                    SetOwner(null);
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
            CreatePointLight(root.transform, new Vector3(0f, 1.2f, 0f), PhantomWitchConfig.VioletVoidCore, radius * 1.8f, 3.2f, duration);
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
                    CreatePointLight(rootObj.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidCore, PhantomWitchConfig.TeleportExpandRadius * 1.8f, 3.5f, rootLifetime);
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

                // 审查 VB-05：池化根到点整棵回收，此前魂雾 / 星尘在亮度峰值一帧消失；根上加一层淡出。
                PhantomWitchFadeDestroy rootFade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                rootFade.Configure(rootLifetime, Mathf.Min(0.35f, rootLifetime * 0.45f));
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
            CreatePointLight(root.transform, new Vector3(0f, 0.22f, 0f), PhantomWitchConfig.VioletVoidCore, 3.2f, 3.0f, safeDuration);
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
            CreatePointLight(root.transform, new Vector3(0f, 0.18f, 0f), PhantomWitchConfig.VioletVoidCore, 4.8f, MaxLightIntensity, duration);
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
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.8f, 0f), PhantomWitchConfig.VioletVoidMid, radius + 1f, 3.2f, PhantomWitchConfig.CurseAuraFxDuration);
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
            // 审查 VB-09：Boss 横扫已有自己的点光，叠加的挥砍拖尾不再另开一盏。
            PlayBossScytheSwingOverlay(position + Vector3.up * 0.18f, forward, radius * 1.02f);

            GameObject root = GetOrBuildVfx("PW_ScytheSweepFX", position, 0.72f, (rootObj) =>
            {
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.5f, 0f), PhantomWitchConfig.SilverAshCore, radius + 1f, 3.2f, 0.72f);

                PhantomWitchFlatPathMesh mainArc = CreateArc(rootObj.transform, radius * 0.18f, halfAngle, forward, 0.14f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.98f), 0.14f);
                PhantomWitchExpandArc mainExpand = mainArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                mainExpand.Configure(radius * 0.18f, radius, halfAngle, forward, 0.16f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.98f), 0.14f);

                PhantomWitchFlatPathMesh ghostArcA = CreateArc(rootObj.transform, radius * 0.15f, halfAngle * 0.92f, forward, 0.09f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.70f), 0.18f);
                PhantomWitchExpandArc ghostExpandA = ghostArcA.gameObject.AddComponent<PhantomWitchExpandArc>();
                ghostExpandA.Configure(radius * 0.15f, radius * 0.96f, halfAngle * 0.92f, forward, 0.22f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.70f), 0.09f);

                PhantomWitchFlatPathMesh ghostArcB = CreateArc(rootObj.transform, radius * 0.10f, halfAngle * 0.84f, forward, 0.07f, WithAlpha(PhantomWitchConfig.VioletVoidVeil, 0.52f), 0.22f);
                PhantomWitchExpandArc ghostExpandB = ghostArcB.gameObject.AddComponent<PhantomWitchExpandArc>();
                ghostExpandB.Configure(radius * 0.10f, radius * 0.90f, halfAngle * 0.84f, forward, 0.26f, WithAlpha(PhantomWitchConfig.VioletVoidVeil, 0.52f), 0.07f);

                CreateArcRuneFlashes(rootObj.transform, radius * 0.78f, halfAngle, forward, 3, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.9f));
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
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.8f, 0f), PhantomWitchConfig.VioletVoidCore, radius + 1f, 3.5f, 0.78f);

                PhantomWitchFlatPathMesh burstRing = CreatePartialRing(rootObj.transform, radius * 0.92f, 0.10f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.88f), 0.10f, ResolveRingSegments(), 0.84f, 0.008f);
                PhantomWitchRingSpin spin = burstRing.gameObject.AddComponent<PhantomWitchRingSpin>();
                spin.rotationSpeed = 8f;

                PhantomWitchFlatPathMesh slashArc = CreateArc(rootObj.transform, 0.01f, 40f, forward, 0.16f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.96f), 0.14f);
                PhantomWitchExpandArc arcExpand = slashArc.gameObject.AddComponent<PhantomWitchExpandArc>();
                arcExpand.Configure(0.01f, radius, 40f, forward, 0.18f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.96f), 0.16f);
                CreateBrokenRing(rootObj.transform, radius * 0.72f, 0.07f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.72f), 0.09f, ResolveRingSegments(), 0.88f, 0.008f);
                CreateSoulTendril(rootObj.transform, new Vector3(0f, 0.10f, 0f), forward * radius, 0.26f, WithAlpha(PhantomWitchConfig.VioletVoidMid, 0.92f), true);

                CreateRuneFlashSpawner(rootObj.transform, radius * 0.72f, 3, 0.12f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.78f));
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

        /// <summary>
        /// 怨灵拖斩的蓄力氛围。2026-09-23 审查 VB-07：原来这里画两条 52° / 58°、半径 3.0 的外扩弧并按 (1-t) 淡出，
        /// 与两段判定（3.2 m / 48°、3.6 m / 54°）对不上，而且出手时已经淡没了。范围与时机改由
        /// <see cref="CreateConeTelegraph"/> 按判定值画（控制器里与本效果同时创建），这里只留魂雾、星尘与灯。
        /// </summary>
        internal static GameObject CreateWraithWindupOutlineEffect(Vector3 position, Vector3 forward, float radius, float duration)
        {
            forward = NormalizeFlatForward(forward);
            GameObject root = GetOrBuildVfx("PW_WraithWindupOutlineFX", position, duration, (rootObj) =>
            {
                float safeRadius = Mathf.Max(radius, 0.8f);
                float safeDuration = Mathf.Max(duration, 0.1f);
                CreatePointLight(rootObj.transform, new Vector3(0f, 0.55f, 0f), PhantomWitchConfig.SilverAshCore, safeRadius + 1f, 2.8f, safeDuration);

                CreateArcRuneFlashes(rootObj.transform, safeRadius * 0.82f, 48f, forward, 3, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.78f));
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
                CreatePointLight(rootObj.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidMid, PhantomWitchConfig.SummonCircleRadius + 1f, 3.2f, PhantomWitchConfig.SummonCircleFxDuration);
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

                // 审查 VB-05：池化根到点整棵回收，补一段淡出，粒子不再一帧消失。
                PhantomWitchFadeDestroy fade = rootObj.AddComponent<PhantomWitchFadeDestroy>();
                fade.Configure(PhantomWitchConfig.MinionSpawnFxDuration, 0.3f);
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
            CreatePointLight(root.transform, new Vector3(0f, 1.5f, 0f), PhantomWitchConfig.SilverAshCore, MaxLightRange, MaxLightIntensity, 1.25f);
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
            CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidCore, 4f, 3f, duration);
            CreateGroundStain(root.transform, 1.8f, WithAlpha(PhantomWitchConfig.VioletVoidDust, 0.08f), duration);
            CreateRuneFlashSpawner(root.transform, 1.2f, 3, 0.10f, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.55f));
            CreateSoulFlameEmitter(root.transform, new Vector3(0f, 0.2f, 0f), 5f, 1.0f, 1.5f, 0.25f, 0.55f, 0.08f, 0.12f, false, ParticleSystemShapeType.Cone);
            CreateStardustEmitter(root.transform, 1.5f, 15f, duration);

            // 审查 VB-05：此前到点整棵 Destroy，魂焰与星尘一帧消失。
            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(duration, Mathf.Min(0.4f, duration * 0.5f));
            Object.Destroy(root, duration);
            return root;
        }

        internal static GameObject CreateDeathEffect(Vector3 position)
        {
            GameObject root = CreateRoot("PW_DeathFX", position);
            CreatePointLight(root.transform, new Vector3(0f, 1.2f, 0f), PhantomWitchConfig.GhostBreathCore, MaxLightRange, MaxLightIntensity, PhantomWitchConfig.DeathFxDuration);
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
            CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), PhantomWitchConfig.VioletVoidMid, radius + 1f, 3f, duration);
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

        /// <summary>
        /// 诅咒领域落点预警。2026-09-23 审查 VB-01：此前外圈 4 px、按 t² 从判定半径缩到 0、alpha 按 (1-t) 淡出，
        /// 地面染色在最后 0.7 s 淡掉——危险越近预警越看不见，玩家读到的是「圈在往里收，站外面就安全」。
        /// 现在：外圈钉在判定半径（只随蓄力变粗变亮，最后 0.08 s 闪白），内部填充从圆心线性长满、长满即落下；
        /// 地面暗染改成偏暗的污染色，领域落下后 0.15 s 再淡出。判定半径与 1.05 s 时序不变。
        /// </summary>
        internal static GameObject CreateCurseRealmWarningCircle(Vector3 origin, float radius, float duration)
        {
            float safeRadius = Mathf.Max(radius, PhantomWitchConfig.CurseRealmWarningMinRadius);
            float safeDuration = Mathf.Max(0.05f, duration);
            float lifetime = safeDuration + TelegraphPostFade;

            GameObject root = CreateRoot("PhantomWitch_CurseRealmWarning", origin);
            CreatePointLight(root.transform, new Vector3(0f, 0.15f, 0f), PhantomWitchConfig.SilverAshCore, safeRadius + 1f, 2.4f, lifetime);

            GameObject stain = CreateFlatQuad(root.transform, "GroundStain", safeRadius * 2.35f, 0.02f, WarningGroundStainColor);
            PhantomWitchFadeDestroy stainFade = stain.AddComponent<PhantomWitchFadeDestroy>();
            stainFade.Configure(lifetime, TelegraphPostFade);

            CreateSoulMistEmitter(root.transform, Mathf.Max(safeRadius * 0.72f, 0.6f), 8f, 0.8f, 1.2f, true, 0.04f);
            CreateStardustEmitter(root.transform, Mathf.Max(safeRadius * 0.9f, 0.8f), 14f, safeDuration);
            CreateBillboardQuad(root.transform, 0.16f, 0.16f, new Vector3(0f, 0.12f, 0f), WithAlpha(PhantomWitchConfig.BloodRoseCore, 0.42f), true);

            PhantomWitchFadeDestroy fade = root.AddComponent<PhantomWitchFadeDestroy>();
            fade.Configure(lifetime, TelegraphPostFade + 0.1f);

            // 边界 + 计时填充（由 PhantomWitchTelegraphDriver 驱动，根上的淡出不抢写它们）。
            CreateCircleTelegraph(root.transform, safeRadius, safeDuration);

            AttachTransientRequiemLine(root, Mathf.Max(1.05f, safeRadius * 0.30f), Mathf.Min(safeDuration, 0.22f), true, WithAlpha(PhantomWitchConfig.SilverAshCore, 0.82f));
            AttachTransientSilverCrossFlash(root, Mathf.Max(0.22f, safeRadius * 0.10f), 0.84f, true, Mathf.Min(safeDuration, 0.18f));

            Object.Destroy(root, lifetime + 0.05f);
            return root;
        }

        /// <summary>领域预警的地面暗染：读作「被污染的地」，不是一层紫光。</summary>
        private static readonly Color WarningGroundStainColor = new Color(0.30f, 0.12f, 0.20f, 0.20f);

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

            // 线 / 面片 / 地面带 / 祭坛材质都归 BossRushFxMaterials（全 Mod 共享），这里不销毁。
            // 女巫自己的程序化贴图（柔边带、圆盘、祭坛投影）是工厂材质的缓存键，按会话常驻（HideAndDontSave），
            // 每次清缓存都重建会让工厂按新贴图实例多攒一份材质。
            if (cachedBillboardQuadMesh != null)
            {
                Object.Destroy(cachedBillboardQuadMesh);
                cachedBillboardQuadMesh = null;
            }
            if (cachedQuadMesh != null)
            {
                Object.Destroy(cachedQuadMesh);
                cachedQuadMesh = null;
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

            // 审查 VB-09：Boss 横扫 / 重斩的特效根已有自己的点光，叠加的挥砍拖尾不再另开一盏。
            PhantomWitchScytheSwingFx.PlayAt(position, Quaternion.LookRotation(NormalizeFlatForward(forward)), rangeScale, false);
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

        /// <summary>
        /// 建一盏特效点光。2026-09-23 审查 VB-05 / VB-09：
        /// - duration 必须传特效根的时长（此前默认 3 s，根 0.72 s 就回收时灯还剩 94% 亮度，被一刀切）；
        /// - 强度钳到 <see cref="MaxLightIntensity"/>、半径钳到 <see cref="MaxLightRange"/>；
        /// - 灯色一律用低饱和灰紫 <see cref="WitchLightColor"/>（color 参数只保留调用处的语义，不再直接上色），
        ///   深紫 / 冷白大灯会把原版暖琥珀地面洗成灰紫。
        /// </summary>
        private static GameObject CreatePointLight(Transform parent, Vector3 localPos, Color color, float range, float intensity, float duration)
        {
            float safeIntensity = Mathf.Clamp(intensity, 0f, MaxLightIntensity);
            float safeRange = Mathf.Clamp(range, 0.5f, MaxLightRange);

            GameObject lightGo = new GameObject("PW_PointLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = WitchLightColor;
            light.range = safeRange;
            light.intensity = safeIntensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;

            PhantomWitchLightPulse pulser = lightGo.AddComponent<PhantomWitchLightPulse>();
            pulser.Configure(safeIntensity, safeRange, Mathf.Max(0.05f, duration));

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
                // 审查 VB-03：startSizeMultiplier 在「两常数随机」模式下只改上限，改为显式缩放两端。
                main.startSize = ScaleStartSize(main.startSize, 1.25f);

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

                // 只写这个渲染器自己的 MaterialPropertyBlock，不碰 sharedMaterials（2026-09-23）：
                // 这里的渲染器是从霜之哀伤冰焰借来的克隆，sharedMaterial 与玩家手里的霜之哀伤、以及
                // BossRushFxMaterials 的全 Mod 共享材质是同一个对象——旧写法直接改共享材质，
                // 女巫瞬移一次，玩家的冰焰和所有共用那份材质的特效都被染成紫色。
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, coreColor);
            }

            // 审查 VB-09：标记根已有一盏受控点光，借来的冰焰灯不再叠加。
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                {
                    continue;
                }

                light.enabled = false;
            }
        }

        /// <summary>
        /// 贴地环 / 弧 / 扇形边：柔边带贴图（横向羽化）的半透明材质。此前是 whiteTexture 硬边带（审查 VB-02 / VB-10）。
        /// 材质归 BossRushFxMaterials 所有；贴图是女巫自己的，按贴图实例取同一份共享材质。
        /// </summary>
        private static Material GetGroundLineMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, GetSoftBandTexture());
        }

        /// <summary>给诅咒领域（PhantomWitchCurseRealmVisual）复用的同一份柔边带材质。</summary>
        internal static Material GetSharedGroundLineMaterial()
        {
            return GetGroundLineMaterial();
        }

        /// <summary>虚线柔边带：第二段 / 延伸判定的扇形外沿。</summary>
        private static Material GetDashedGroundLineMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, GetDashedBandTexture());
        }

        /// <summary>预警填充：实心软边圆盘贴图（0.82 半径内不透明，之外羽化到 0）。</summary>
        private static Material GetTelegraphFillMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, GetSoftDiscTexture());
        }

        internal static Material GetSharedLineMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        private static Material GetQuadMaterial()
        {
            return PhantomWitchAssetManager.GetQuadMaterial();
        }

        private static Material GetGlowQuadMaterial()
        {
            return PhantomWitchAssetManager.GetGlowQuadMaterial();
        }

        private static Material GetTrailMaterial()
        {
            return PhantomWitchAssetManager.GetLineMaterial();
        }

        /// <summary>祭坛投影：共享半透明材质 + 女巫自己的投影贴图（不再 new Material 派生一份再销毁）。</summary>
        private static Material GetAltarMaterial(bool broken)
        {
            return BossRushFxMaterials.Get(
                BossRushFxBlend.Alpha,
                broken ? GetBrokenAltarProjectionTexture() : GetAltarProjectionTexture());
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
            filter.sharedMesh = GetBillboardQuadMesh();

            // 广告牌光点是「魂焰核心 / 闪光」：加色（审查 VB-02）。
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetGlowQuadMaterial();
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
            ringMesh.Configure(ResolveRingSegments(), radius, Mathf.Max(MinGroundBandWidth, width), GetGroundLineMaterial(), color);
            return ringMesh;
        }

        private static PhantomWitchFlatPathMesh CreateArc(Transform parent, float radius, float halfAngle, Vector3 forward, float width, Color color, float yOffset)
        {
            GameObject arc = new GameObject("Arc");
            arc.transform.SetParent(parent, false);
            arc.transform.localPosition = new Vector3(0f, Mathf.Max(yOffset, 0.02f), 0f);

            int segments = ResolveAdaptiveCount(32, 18, 10);
            PhantomWitchFlatPathMesh pathMesh = arc.AddComponent<PhantomWitchFlatPathMesh>();
            pathMesh.Configure(BuildArcPoints(radius, halfAngle, forward, segments), Mathf.Max(MinGroundBandWidth, width), GetGroundLineMaterial(), color);
            return pathMesh;
        }

        private static PhantomWitchFlatPathMesh CreatePartialRing(Transform parent, float radius, float width, Color color, float yOffset, int segments, float coverage, float jitter)
        {
            GameObject ring = new GameObject("BrokenRing");
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, Mathf.Max(yOffset, 0.02f), 0f);

            PhantomWitchFlatPathMesh pathMesh = ring.AddComponent<PhantomWitchFlatPathMesh>();
            pathMesh.Configure(BuildPartialRingPoints(radius, segments, coverage, jitter), Mathf.Max(MinGroundBandWidth, width), GetGroundLineMaterial(), color);
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

        /// <summary>审查 VB-08：一次符文闪最多 3 个（此前 6–14 个、每个 2–3 条发丝线，只剩一片噪点）。</summary>
        private const int MaxRuneFlashesPerBurst = 3;

        private static void CreateRuneFlashSpawner(Transform parent, float radius, int count, float yOffset, Color color)
        {
            count = Mathf.Min(count, MaxRuneFlashesPerBurst);
            for (int i = 0; i < count; i++)
            {
                float angle = ((float)i / Mathf.Max(1, count)) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.25f, 0.25f);
                CreateRuneFlash(parent, new Vector3(Mathf.Cos(angle) * radius, yOffset + UnityEngine.Random.Range(-0.05f, 0.08f), Mathf.Sin(angle) * radius), color, 0.45f);
            }
        }

        private static void CreateArcRuneFlashes(Transform parent, float radius, float halfAngle, Vector3 forward, int count, Color color)
        {
            count = Mathf.Min(count, MaxRuneFlashesPerBurst);
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

            const int segmentCount = 2;
            for (int i = 0; i < segmentCount; i++)
            {
                GameObject segment = new GameObject("Segment_" + i);
                segment.transform.SetParent(rune.transform, false);
                LineRenderer line = segment.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 3;
                line.widthMultiplier = UnityEngine.Random.Range(0.06f, 0.08f);
                line.widthCurve = TaperedLineWidthCurve;
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


    }


}
