using System;
using System.Collections.Generic;
using Duckov.Scenes;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 标准 BossRush 通关奖励箱的虚影跟随与落地控制器。
    /// </summary>
    public sealed class VictoryRewardShadowCrateController : MonoBehaviour
    {
        private enum ShadowCrateState
        {
            Following,
            Materializing,
            Descending,
            Completed
        }

        private const float MaterializeDurationSeconds = 0.4f;
        private const float DescendSpeedMetersPerSecond = 1.4f;
        private const float RotationSpeedDegreesPerSecond = 30f;
        private const float GhostHeroScaleMultiplier = 2f;
        private const float InitialGhostAlpha = 0.45f;
        private const float FinalGhostAlpha = 1f;
        private const float InitialGhostScale = 1.08f;
        private const float FinalGhostScale = 1f;
        private const float GroundRaycastDistance = 32f;
        private const float GhostAuraBaseIntensity = 4.5f;
        private const float GhostAuraPulseIntensity = 1.4f;
        private const float GhostAuraPulseSpeed = 4.5f;
        private const float GhostAuraRange = 6.5f;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color GhostTint = new Color(1f, 0.95f, 0.78f, 1f);
        private static readonly Color GhostAuraColor = new Color(1f, 0.82f, 0.22f, 1f);

        private readonly List<Material> ghostMaterials = new List<Material>();

        private ModBehaviour owner;
        private CharacterMainControl player;
        private Transform playerTransform;
        private InteractableLootbox sourcePrefab;
        private GameObject ghostObject;
        private Transform ghostTransform;
        private Vector3 ghostBaseScale = Vector3.one;
        private Light ghostAuraLight;
        private ShadowCrateState state = ShadowCrateState.Following;
        private Vector3 landingPosition = Vector3.zero;
        private int highQualityCount;
        private float stateElapsedSeconds;
        private bool disposed;

        public bool Initialize(
            ModBehaviour ownerInstance,
            CharacterMainControl playerCharacter,
            InteractableLootbox visualPrefab,
            int rewardHighQualityCount)
        {
            owner = ownerInstance;
            player = playerCharacter;
            playerTransform = player != null ? player.transform : null;
            sourcePrefab = visualPrefab;
            highQualityCount = rewardHighQualityCount;
            state = ShadowCrateState.Following;
            stateElapsedSeconds = 0f;

            if (owner == null || player == null || sourcePrefab == null || highQualityCount <= 0)
            {
                return false;
            }

            try
            {
                ghostObject = UnityEngine.Object.Instantiate(sourcePrefab.gameObject);
                ghostObject.name = "BossRush_VictoryRewardShadowCrate";
                ghostTransform = ghostObject.transform;
                ghostBaseScale = ghostTransform.localScale;
                MultiSceneCore.MoveToActiveWithScene(ghostObject, SceneManager.GetActiveScene().buildIndex);
                DisableGhostInteraction(ghostObject);
                CreateGhostAuraLight();
                CacheGhostMaterials();
                ApplyGhostVisual(InitialGhostAlpha, InitialGhostScale);
                UpdateFollowPose(Time.unscaledTime);
                ModBehaviour.DevLog("[BossRush] 通关奖励箱虚影已创建: pos=" + ghostTransform.position);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 创建通关奖励箱虚影失败: " + e.Message);
                CleanupAndDestroy(false);
                return false;
            }
        }

        public void CompleteAndLand()
        {
            if (disposed || state != ShadowCrateState.Following)
            {
                return;
            }

            Vector3 anchorPosition = ResolveAnchorPosition();
            landingPosition = BuildLandingPosition(anchorPosition);
            state = ShadowCrateState.Materializing;
            stateElapsedSeconds = 0f;
            ModBehaviour.DevLog("[BossRush] 通关奖励箱虚影开始凝实: anchor=" + anchorPosition + ", landing=" + landingPosition);

            if (ghostTransform != null)
            {
                Vector3 currentPosition = ghostTransform.position;
                ghostTransform.position = new Vector3(anchorPosition.x, currentPosition.y, anchorPosition.z);
            }
        }

        private void Update()
        {
            if (disposed || ghostObject == null)
            {
                return;
            }

            switch (state)
            {
                case ShadowCrateState.Following:
                    UpdateFollowPose(Time.unscaledTime);
                    break;
                case ShadowCrateState.Materializing:
                    UpdateMaterialize(Time.unscaledDeltaTime);
                    break;
                case ShadowCrateState.Descending:
                    UpdateDescending(Time.unscaledDeltaTime);
                    break;
            }
        }

        private void UpdateFollowPose(float elapsedSeconds)
        {
            if (ghostTransform == null)
            {
                return;
            }

            Vector3 anchorPosition = ResolveAnchorPosition();
            float followY = VictoryRewardShadowMath.ComputeFollowY(anchorPosition.y, elapsedSeconds);
            ghostTransform.position = new Vector3(anchorPosition.x, followY, anchorPosition.z);
            ghostTransform.Rotate(0f, RotationSpeedDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.World);
        }

        private void UpdateMaterialize(float deltaTime)
        {
            stateElapsedSeconds += deltaTime;
            float t = Mathf.Clamp01(stateElapsedSeconds / MaterializeDurationSeconds);
            float alpha = Mathf.Lerp(InitialGhostAlpha, FinalGhostAlpha, t);
            float scale = Mathf.Lerp(InitialGhostScale, FinalGhostScale, t);
            ApplyGhostVisual(alpha, scale);
            if (ghostTransform != null)
            {
                ghostTransform.Rotate(0f, RotationSpeedDegreesPerSecond * deltaTime, 0f, Space.World);
            }

            if (t >= 1f)
            {
                state = ShadowCrateState.Descending;
                stateElapsedSeconds = 0f;
            }
        }

        private void UpdateDescending(float deltaTime)
        {
            stateElapsedSeconds += deltaTime;
            if (ghostTransform == null)
            {
                return;
            }

            Vector3 currentPosition = ghostTransform.position;
            float nextY = VictoryRewardShadowMath.MoveTowardsY(
                currentPosition.y,
                landingPosition.y,
                DescendSpeedMetersPerSecond,
                deltaTime);

            ghostTransform.position = new Vector3(landingPosition.x, nextY, landingPosition.z);
            ghostTransform.Rotate(0f, RotationSpeedDegreesPerSecond * deltaTime, 0f, Space.World);

            if (Mathf.Abs(nextY - landingPosition.y) <= 0.0001f)
            {
                SpawnRealRewardAndDispose();
            }
        }

        private Vector3 ResolveAnchorPosition()
        {
            try
            {
                if (playerTransform != null)
                {
                    return playerTransform.position;
                }
            }
            catch (Exception)
            {
            }

            if (ghostTransform != null)
            {
                return ghostTransform.position;
            }

            return Vector3.zero;
        }

        private Vector3 BuildLandingPosition(Vector3 anchorPosition)
        {
            float groundY = 0f;
            bool hitGround = false;

            try
            {
                Vector3 rayStart = anchorPosition + Vector3.up * 1f;
                RaycastHit hit;

                try
                {
                    int groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                    hitGround = Physics.Raycast(
                        rayStart,
                        Vector3.down,
                        out hit,
                        GroundRaycastDistance,
                        groundMask,
                        QueryTriggerInteraction.Ignore);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 通关奖励箱虚影仅读地面层射线失败，回退全层: " + e.Message);
                    hitGround = false;
                    hit = default(RaycastHit);
                }

                if (!hitGround)
                {
                    hitGround = Physics.Raycast(
                        rayStart,
                        Vector3.down,
                        out hit,
                        GroundRaycastDistance,
                        ~0,
                        QueryTriggerInteraction.Ignore);
                }

                if (hitGround)
                {
                    groundY = hit.point.y;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 解析通关奖励箱虚影落点失败，回退玩家当前位置: " + e.Message);
                hitGround = false;
            }

            float landingY = VictoryRewardShadowMath.ComputeLandingY(anchorPosition.y, hitGround, groundY);
            return new Vector3(anchorPosition.x, landingY, anchorPosition.z);
        }

        private void CreateGhostAuraLight()
        {
            if (ghostTransform == null)
            {
                return;
            }

            GameObject auraLightObject = new GameObject("BossRush_VictoryRewardShadowAuraLight");
            auraLightObject.transform.SetParent(ghostTransform, false);
            auraLightObject.transform.localPosition = Vector3.zero;
            ghostAuraLight = auraLightObject.AddComponent<Light>();
            ghostAuraLight.type = LightType.Point;
            ghostAuraLight.color = GhostAuraColor;
            ghostAuraLight.range = GhostAuraRange;
            ghostAuraLight.intensity = GhostAuraBaseIntensity;
            ghostAuraLight.shadows = LightShadows.None;
        }

        private void DisableGhostInteraction(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        colliders[i].enabled = false;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 禁用奖励箱虚影碰撞体失败: " + e.Message);
            }

            try
            {
                Rigidbody[] rigidbodies = target.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < rigidbodies.Length; i++)
                {
                    if (rigidbodies[i] != null)
                    {
                        UnityEngine.Object.Destroy(rigidbodies[i]);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 移除奖励箱虚影刚体失败: " + e.Message);
            }

            try
            {
                Duckov.Utilities.LootBoxLoader loader = target.GetComponentInChildren<Duckov.Utilities.LootBoxLoader>(true);
                if (loader != null)
                {
                    loader.enabled = false;
                }

                BossRushDeleteLootboxInteractable deleteInteract = target.GetComponentInChildren<BossRushDeleteLootboxInteractable>(true);
                if (deleteInteract != null)
                {
                    deleteInteract.enabled = false;
                }

                InteractableLootbox lootbox = target.GetComponentInChildren<InteractableLootbox>(true);
                if (lootbox != null)
                {
                    lootbox.needInspect = false;
                    lootbox.enabled = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 禁用奖励箱虚影交互脚本失败: " + e.Message);
            }

            try
            {
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 设置奖励箱虚影渲染器失败: " + e.Message);
            }

            EnsureVisualChildrenVisible(target);
        }

        private void CacheGhostMaterials()
        {
            ghostMaterials.Clear();

            if (ghostObject == null)
            {
                return;
            }

            try
            {
                Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] materials = renderer.materials;
                    if (materials == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < materials.Length; j++)
                    {
                        Material material = materials[j];
                        if (material != null)
                        {
                            ConfigureGhostMaterialTransparency(material);
                            ghostMaterials.Add(material);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 缓存奖励箱虚影材质失败: " + e.Message);
            }
        }

        private void ApplyGhostVisual(float alpha, float scale)
        {
            if (ghostTransform != null)
            {
                ghostTransform.localScale = ghostBaseScale * (GhostHeroScaleMultiplier * scale);
            }

            for (int i = 0; i < ghostMaterials.Count; i++)
            {
                Material material = ghostMaterials[i];
                if (material == null)
                {
                    continue;
                }

                Color color = new Color(GhostTint.r, GhostTint.g, GhostTint.b, alpha);
                try
                {
                    if (material.HasProperty(ColorPropertyId))
                    {
                        material.SetColor(ColorPropertyId, color);
                    }
                }
                catch {}

                try
                {
                    if (material.HasProperty(TintColorPropertyId))
                    {
                        material.SetColor(TintColorPropertyId, color);
                    }
                }
                catch {}

                try
                {
                    if (material.HasProperty(BaseColorPropertyId))
                    {
                        material.SetColor(BaseColorPropertyId, color);
                    }
                }
                catch {}

                try
                {
                    if (material.HasProperty(EmissionColorPropertyId))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor(EmissionColorPropertyId, GhostAuraColor * (0.8f + alpha * 1.6f));
                    }
                }
                catch {}
            }

            if (ghostAuraLight != null)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * GhostAuraPulseSpeed) * 0.5f;
                ghostAuraLight.intensity = (GhostAuraBaseIntensity + GhostAuraPulseIntensity * pulse) * Mathf.Lerp(0.7f, 1f, alpha);
                ghostAuraLight.range = GhostAuraRange * Mathf.Lerp(0.85f, 1.1f, alpha);
            }
        }

        private void EnsureVisualChildrenVisible(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.gameObject.SetActive(true);
                    renderer.enabled = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 强制启用奖励箱虚影视觉渲染器失败: " + e.Message);
            }

            try
            {
                ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] == null)
                    {
                        continue;
                    }

                    particles[i].gameObject.SetActive(true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 强制启用奖励箱虚影粒子失败: " + e.Message);
            }

            try
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] == null)
                    {
                        continue;
                    }

                    lights[i].gameObject.SetActive(true);
                    lights[i].enabled = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 强制启用奖励箱虚影光源失败: " + e.Message);
            }
        }

        private void ConfigureGhostMaterialTransparency(Material material)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 1f);
                }

                if (material.HasProperty("_Mode"))
                {
                    material.SetFloat("_Mode", 3f);
                }

                if (material.HasProperty("_AlphaClip"))
                {
                    material.SetFloat("_AlphaClip", 0f);
                }

                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 配置奖励箱虚影透明材质失败: " + e.Message);
            }
        }

        private void SpawnRealRewardAndDispose()
        {
            if (state == ShadowCrateState.Completed)
            {
                return;
            }

            state = ShadowCrateState.Completed;
            ModBehaviour.DevLog("[BossRush] 通关奖励箱虚影完成落地: landing=" + landingPosition);

            try
            {
                if (owner != null)
                {
                    owner.SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(highQualityCount, landingPosition);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 奖励箱虚影落地生成实体失败，回退默认逻辑: " + e.Message);

                try
                {
                    if (owner != null)
                    {
                        owner.SpawnDifficultyRewardLootboxFallback_LootAndRewards(highQualityCount);
                    }
                }
                catch (Exception inner)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 奖励箱虚影回退默认生成也失败: " + inner.Message);
                }
            }

            CleanupAndDestroy(true);
        }

        private void CleanupAndDestroy(bool destroySelf)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (ghostObject != null)
            {
                UnityEngine.Object.Destroy(ghostObject);
                ghostObject = null;
            }
            ghostTransform = null;
            playerTransform = null;

            if (owner != null)
            {
                owner.NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(this);
            }

            if (destroySelf && gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            CleanupAndDestroy(false);
        }
    }

    internal static class VictoryRewardCrateHeroVisual
    {
        private const float HeroShellScaleMultiplier = 2f;
        private const float AuraRange = 8f;
        private const float AuraIntensity = 5.5f;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color HeroShellColor = new Color(1f, 0.84f, 0.26f, 0.92f);
        private static readonly Color HeroEmissionColor = new Color(1f, 0.72f, 0.18f, 1f);

        internal static void AttachToLootbox(InteractableLootbox lootbox, InteractableLootbox visualPrefab)
        {
            if (lootbox == null || lootbox.gameObject == null)
            {
                return;
            }

            try
            {
                Transform existing = lootbox.transform.Find("BossRush_VictoryRewardHeroShell");
                if (existing != null)
                {
                    return;
                }

                GameObject sourceObject = visualPrefab != null ? visualPrefab.gameObject : lootbox.gameObject;
                if (sourceObject == null)
                {
                    return;
                }

                GameObject shell = UnityEngine.Object.Instantiate(sourceObject);
                shell.name = "BossRush_VictoryRewardHeroShell";
                shell.transform.SetParent(lootbox.transform, false);
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localRotation = Quaternion.identity;
                shell.transform.localScale = Vector3.one * HeroShellScaleMultiplier;

                PrepareHeroShell(shell);
                CreateAuraLight(lootbox.transform);

                ModBehaviour.DevLog("[BossRush] 已为通关奖励箱附加英雄视觉外壳");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 为通关奖励箱附加英雄视觉外壳失败: " + e.Message);
            }
        }

        private static void PrepareHeroShell(GameObject shell)
        {
            if (shell == null)
            {
                return;
            }

            try
            {
                Collider[] colliders = shell.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        UnityEngine.Object.Destroy(colliders[i]);
                    }
                }
            }
            catch {}

            try
            {
                Rigidbody[] rigidbodies = shell.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < rigidbodies.Length; i++)
                {
                    if (rigidbodies[i] != null)
                    {
                        UnityEngine.Object.Destroy(rigidbodies[i]);
                    }
                }
            }
            catch {}

            DisableBehaviour<InteractableLootbox>(shell);
            DisableBehaviour<Inventory>(shell);
            DisableBehaviour<Duckov.Utilities.LootBoxLoader>(shell);
            DisableBehaviour<BossRushDeleteLootboxInteractable>(shell);
            DisableBehaviour<BossRushCarryInteractable>(shell);
            DisableBehaviour<BossRushLootboxMarker>(shell);

            try
            {
                Renderer[] renderers = shell.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    Material[] materials = renderer.materials;
                    if (materials == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < materials.Length; j++)
                    {
                        Material material = materials[j];
                        if (material == null)
                        {
                            continue;
                        }

                        ConfigureTransparentGoldMaterial(material);
                    }
                }
            }
            catch {}

            EnsureVisualChildrenVisible(shell);
        }

        private static void CreateAuraLight(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find("BossRush_VictoryRewardHeroAuraLight");
            if (existing != null)
            {
                return;
            }

            GameObject auraLightObject = new GameObject("BossRush_VictoryRewardHeroAuraLight");
            auraLightObject.transform.SetParent(parent, false);
            auraLightObject.transform.localPosition = Vector3.zero;
            Light aura = auraLightObject.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.color = HeroEmissionColor;
            aura.range = AuraRange;
            aura.intensity = AuraIntensity;
            aura.shadows = LightShadows.None;
        }

        private static void DisableBehaviour<T>(GameObject root) where T : Behaviour
        {
            if (root == null)
            {
                return;
            }

            try
            {
                T[] behaviours = root.GetComponentsInChildren<T>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] != null)
                    {
                        behaviours[i].enabled = false;
                    }
                }
            }
            catch {}
        }

        private static void EnsureVisualChildrenVisible(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.gameObject.SetActive(true);
                    renderer.enabled = true;
                }
            }
            catch {}

            try
            {
                ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null)
                    {
                        particles[i].gameObject.SetActive(true);
                    }
                }
            }
            catch {}

            try
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null)
                    {
                        lights[i].gameObject.SetActive(true);
                        lights[i].enabled = true;
                    }
                }
            }
            catch {}
        }

        private static void ConfigureTransparentGoldMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 1f);
                }

                if (material.HasProperty("_Mode"))
                {
                    material.SetFloat("_Mode", 3f);
                }

                if (material.HasProperty("_AlphaClip"))
                {
                    material.SetFloat("_AlphaClip", 0f);
                }

                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;

                if (material.HasProperty(ColorPropertyId))
                {
                    material.SetColor(ColorPropertyId, HeroShellColor);
                }

                if (material.HasProperty(TintColorPropertyId))
                {
                    material.SetColor(TintColorPropertyId, HeroShellColor);
                }

                if (material.HasProperty(BaseColorPropertyId))
                {
                    material.SetColor(BaseColorPropertyId, HeroShellColor);
                }

                if (material.HasProperty(EmissionColorPropertyId))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor(EmissionColorPropertyId, HeroEmissionColor * 2.6f);
                }
            }
            catch {}
        }
    }
}
