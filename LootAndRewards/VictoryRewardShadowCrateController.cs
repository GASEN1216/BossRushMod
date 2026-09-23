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
    ///
    /// 表现口径（2026-09-23 特效审美审查 VA-16 / VA-17）：
    ///   - 视觉模板（实机日志：Box_EnemyDie_Red）的材质是 SodaCraft/SodaLit_EdgeLight，只有 ShadowCaster / DepthOnly /
    ///     DepthNormals / UniversalGBuffer 四个 pass，混合与 ZWrite 写死（UnityPy 直读 resources.assets）。
    ///     旧版把它改到 renderQueue 3000 伪造半透明：URP Deferred 的透明队列只画 Forward 系 pass，虚影和英雄外壳因此根本不画；
    ///     写的 _Color / _BaseColor / _TintColor 着色器也不认。
    ///   - 现在不伪造透明：虚影期缩到 0.6 倍、淡金加一点自发光；凝实时 0.4 s EaseOut 长到 1 倍、换成金色，叠一圈金环。
    ///     颜色只写官方的 _Tint 与 _EmissionColor，走 MaterialPropertyBlock，不复制材质、不改渲染队列（VictoryRewardCrateTint）。
    ///   - 下落先慢后快，触地前约 0.05 s 压扁，真箱外壳接着回弹；落地放金环、碎片、尘与轻微震屏。
    ///   - 灯只照出箱子周围一小片暖斑：淡入、缓呼吸，落地 / 打开 / 销毁时淡出（BossRushFxLightFade）。
    ///   只在通关奖励演出期间存在；触地那一帧生成真箱的时序与奖励逻辑不变。
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

        private const float AppearDurationSeconds = 0.3f;
        private const float MaterializeDurationSeconds = 0.4f;
        private const float DescendStartSpeedMetersPerSecond = 0.3f;
        private const float DescendAccelerationMetersPerSecondSquared = 3.5f;
        private const float LandingSquashHeight = 0.24f;
        private const float LandingSquashY = 0.9f;
        private const float RotationSpeedDegreesPerSecond = 30f;
        private const float GhostHeroScaleMultiplier = 2f;
        private const float InitialGhostScale = 0.6f;
        private const float FinalGhostScale = 1f;
        private const float GroundRaycastDistance = 32f;
        private const float GhostAuraBaseIntensity = 1.6f;
        private const float GhostAuraBreatheAmplitude = 0.25f;
        private const float GhostAuraBreathePeriod = 2.9f;
        private const float GhostAuraRange = 3.8f;
        private const float GhostAuraFadeInSeconds = 0.4f;
        private const float GhostAuraFadeOutSeconds = 0.3f;
        private const float MaterializeRingRadius = 1.2f;
        private const float LandingRingRadius = 1.8f;
        private const float RingLifeSeconds = 0.6f;
        private const int LandingShardCount = 8;
        private const int LandingDustCount = 6;
        private const float LandingShakeStrength = 0.15f;

        private static readonly Color RingColor = new Color(
            BossRushUIColors.RarityLegendary.r, BossRushUIColors.RarityLegendary.g, BossRushUIColors.RarityLegendary.b, 0.85f);
        private static readonly Color LandingDustColor = new Color(0.62f, 0.55f, 0.44f, 0.32f);

        private ModBehaviour owner;
        private CharacterMainControl player;
        private Transform playerTransform;
        private InteractableLootbox sourcePrefab;
        private GameObject ghostObject;
        private Transform ghostTransform;
        private Vector3 ghostBaseScale = Vector3.one;
        private Renderer[] ghostRenderers;
        private float appliedSolidity = -1f;
        private Light ghostAuraLight;
        private BossRushFxLightFade ghostAuraFade;
        private ShadowCrateState state = ShadowCrateState.Following;
        private Vector3 landingPosition = Vector3.zero;
        private int highQualityCount;
        private float stateElapsedSeconds;
        private float appearElapsedSeconds;
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
            appearElapsedSeconds = 0f;

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
                ghostRenderers = ghostObject.GetComponentsInChildren<Renderer>(true);
                // 从 0 长出来（AppearDurationSeconds 内 SmoothStep 到虚影尺寸），不在头顶一帧弹出
                ApplyGhostVisual(0f, 0f);
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
                // 凝实的那一下：绕箱子一圈金环（不开灯，箱子自己的灯已经够了）
                NewWeaponFx.PlayBurst(ghostTransform.position, RingColor, MaterializeRingRadius, RingLifeSeconds, 0, false);
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

            if (appearElapsedSeconds < AppearDurationSeconds)
            {
                appearElapsedSeconds += Time.unscaledDeltaTime;
                ApplyGhostVisual(InitialGhostScale * BossRushUI.SmoothStep(appearElapsedSeconds / AppearDurationSeconds), 0f);
            }
        }

        private void UpdateMaterialize(float deltaTime)
        {
            stateElapsedSeconds += deltaTime;
            float t = Mathf.Clamp01(stateElapsedSeconds / MaterializeDurationSeconds);
            float eased = BossRushUI.EaseOut(t);
            ApplyGhostVisual(Mathf.Lerp(InitialGhostScale, FinalGhostScale, eased), eased);
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

            // 先慢后快的落体：速度随下落时间线性增加，3.2 m 约 1.3 s 落地（旧版 1.4 m/s 匀速约 2.3 s）
            Vector3 currentPosition = ghostTransform.position;
            float descendSpeed = DescendStartSpeedMetersPerSecond + DescendAccelerationMetersPerSecondSquared * stateElapsedSeconds;
            float nextY = VictoryRewardShadowMath.MoveTowardsY(
                currentPosition.y,
                landingPosition.y,
                descendSpeed,
                deltaTime);

            ghostTransform.position = new Vector3(landingPosition.x, nextY, landingPosition.z);
            ghostTransform.Rotate(0f, RotationSpeedDegreesPerSecond * deltaTime, 0f, Space.World);

            // 触地前最后约 0.05 s 压扁（y 到 0.9、水平略鼓），真箱外壳接着回弹（VictoryRewardCrateLandingBounce）
            float remaining = Mathf.Abs(nextY - landingPosition.y);
            if (remaining < LandingSquashHeight)
            {
                float squashY = Mathf.Lerp(LandingSquashY, 1f, remaining / LandingSquashHeight);
                float bulge = 1f + (1f - squashY) * 0.5f;
                ghostTransform.localScale = Vector3.Scale(
                    ghostBaseScale * (GhostHeroScaleMultiplier * FinalGhostScale),
                    new Vector3(bulge, squashY, bulge));
            }

            if (remaining <= 0.0001f)
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

            // 光源在虚影底部（头顶约 3.2 m），3.8 m 的范围只在地面照出约 2 m 半径的暖斑；0.4 s 淡入，约 2.9 s 一次 ±0.4 的呼吸
            GameObject auraLightObject = new GameObject("BossRush_VictoryRewardShadowAuraLight");
            auraLightObject.transform.SetParent(ghostTransform, false);
            auraLightObject.transform.localPosition = Vector3.zero;
            ghostAuraLight = auraLightObject.AddComponent<Light>();
            ghostAuraLight.type = LightType.Point;
            ghostAuraLight.color = VictoryRewardCrateTint.AuraLightColor;
            ghostAuraLight.range = GhostAuraRange;
            ghostAuraLight.intensity = 0f;
            ghostAuraLight.shadows = LightShadows.None;
            ghostAuraFade = BossRushFxLightFade.Attach(ghostAuraLight, GhostAuraBaseIntensity, GhostAuraFadeInSeconds,
                GhostAuraBreatheAmplitude, GhostAuraBreathePeriod);
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

        /// <summary>
        /// scale 是相对虚影终态的尺寸倍数（虚影期 0.6、凝实后 1）；solidity 0 → 1 从淡金虚影过渡到英雄箱的金色。
        /// 颜色只在 solidity 变化时重写（出现阶段只改尺寸）。
        /// </summary>
        private void ApplyGhostVisual(float scale, float solidity)
        {
            if (ghostTransform != null)
            {
                ghostTransform.localScale = ghostBaseScale * (GhostHeroScaleMultiplier * Mathf.Max(0.001f, scale));
            }

            if (Mathf.Abs(solidity - appliedSolidity) < 0.001f)
            {
                return;
            }

            appliedSolidity = solidity;
            VictoryRewardCrateTint.Apply(
                ghostRenderers,
                Color.Lerp(VictoryRewardCrateTint.PhantomTint, VictoryRewardCrateTint.HeroTint, solidity),
                Vector4.Lerp(VictoryRewardCrateTint.PhantomEmission, VictoryRewardCrateTint.HeroEmission, solidity));
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

        private void SpawnRealRewardAndDispose()
        {
            if (state == ShadowCrateState.Completed)
            {
                return;
            }

            state = ShadowCrateState.Completed;
            ModBehaviour.DevLog("[BossRush] 通关奖励箱虚影完成落地: landing=" + landingPosition);

            // 真箱外壳沿用虚影触地时的朝向并接着回弹；交接只在这一次同步生成里有效
            VictoryRewardCrateHeroVisual.BeginLandingHandoff(ghostTransform != null ? ghostTransform.rotation : Quaternion.identity);
            try
            {
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
            }
            finally
            {
                VictoryRewardCrateHeroVisual.EndLandingHandoff();
            }

            PlayLandingImpact();
            ReleaseGhostAuraLight();
            CleanupAndDestroy(true);
        }

        /// <summary>落地冲击：贴地金环 + 8 片碎片（不开灯）、一小团尘、轻微向下的震屏（官方手雷的 0.15 倍）。</summary>
        private void PlayLandingImpact()
        {
            try
            {
                NewWeaponFx.PlayBurst(landingPosition, RingColor, LandingRingRadius, RingLifeSeconds, LandingShardCount, false);
                BossRushFxKit.PlayBurst(landingPosition, BossRushFxKit.Dust(LandingDustColor, LandingDustCount));
                CameraShaker.Shake(Vector3.down * (0.4f * LandingShakeStrength), CameraShaker.CameraShakeTypes.explosion);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 通关奖励箱落地特效失败: " + e.Message);
            }
        }

        /// <summary>虚影的灯脱离虚影留在原地 0.3 s 淡出，与真箱灯的 0.4 s 淡入交叉，落地那一帧地面不闪。</summary>
        private void ReleaseGhostAuraLight()
        {
            if (ghostAuraLight == null)
            {
                return;
            }

            try
            {
                Transform lightTransform = ghostAuraLight.transform;
                lightTransform.SetParent(ghostTransform != null ? ghostTransform.parent : null, true);
                if (ghostAuraFade != null)
                {
                    ghostAuraFade.FadeOut(GhostAuraFadeOutSeconds, true);
                }
                else
                {
                    UnityEngine.Object.Destroy(lightTransform.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 通关奖励箱虚影灯淡出失败: " + e.Message);
            }

            ghostAuraLight = null;
            ghostAuraFade = null;
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
            ghostRenderers = null;
            ghostAuraLight = null;
            ghostAuraFade = null;

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

    /// <summary>
    /// Mode G 胜利奖励同步 strict materializer（任务 #7 加法分支，设计文档 §11/§13）。
    /// 只复用现有 ItemAssetsCollection.InstantiateSync 与 public Item/Inventory API；
    /// 每帧至多实例化一件，按固定 TypeID 快照逐件提交；
    /// strict 语义：单件失败只记失败并继续，绝不静默重试/替换，
    /// 全部完成后触发可观察完成回调并自毁。
    /// Legacy 路径不创建本组件，旧 void/Loader 入口逐字不变。
    /// </summary>
    public sealed class ModeGRewardStrictMaterializer : MonoBehaviour
    {
        private int[] typeIdSnapshot;
        private ItemStatsSystem.Inventory targetInventory;
        private Action<int, ItemStatsSystem.Item, bool> onItemCommitted;
        private Action<int, int, int> onMaterializationCompleted;
        private int nextIndex;
        private int succeededCount;
        private int failedCount;
        private bool finished;
        private bool materializing;
        private bool cancelRequested;

        public bool IsFinished
        {
            get { return finished; }
        }

        /// <summary>
        /// 初始化：接收固定 TypeID 快照（内部拷贝，后续外部修改不影响本事务）。
        /// </summary>
        public bool Initialize(
            int[] fixedTypeIds,
            ItemStatsSystem.Inventory inventory,
            Action<int, ItemStatsSystem.Item, bool> onItemCommittedCallback,
            Action<int, int, int> onCompletedCallback)
        {
            if (fixedTypeIds == null || fixedTypeIds.Length <= 0 || inventory == null)
            {
                return false;
            }

            typeIdSnapshot = (int[])fixedTypeIds.Clone();
            targetInventory = inventory;
            onItemCommitted = onItemCommittedCallback;
            onMaterializationCompleted = onCompletedCallback;
            nextIndex = 0;
            succeededCount = 0;
            failedCount = 0;
            finished = false;
            materializing = false;
            cancelRequested = false;
            return true;
        }

        /// <summary>
        /// 取消未完成的 materializer（幂等），立即自毁并完成回调。
        /// 调用方依靠完成回调对称释放隔离 lease 和胜利安全状态。
        /// </summary>
        public void CancelAndDestroy()
        {
            if (finished)
            {
                return;
            }

            // 背包/物品事件可同步重入取消。当前件必须先确定交付结果，
            // 再把尚未尝试的槽记失败，不能提前清空 Update 正在使用的快照。
            cancelRequested = true;
            if (materializing) return;

            int total = typeIdSnapshot != null ? typeIdSnapshot.Length : succeededCount + failedCount;
            int remaining = Math.Max(0, total - nextIndex);
            failedCount += remaining;
            Action<int, int, int> completionCallback = onMaterializationCompleted;
            finished = true;
            typeIdSnapshot = null;
            targetInventory = null;
            onItemCommitted = null;
            onMaterializationCompleted = null;

            try
            {
                if (completionCallback != null)
                    completionCallback(total, succeededCount, failedCount);
            }
            catch (Exception callbackEx)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 奖励取消完成回调异常: " + callbackEx.Message);
            }

            try
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.Destroy(gameObject);
                }
            }
            catch { }
        }

        private void Update()
        {
            if (finished || materializing || typeIdSnapshot == null)
            {
                return;
            }
            if (targetInventory == null)
            {
                // Inventory 是 Unity 对象，角色/背包销毁也必须完成回调以释放结算租约。
                CancelAndDestroy();
                return;
            }

            // 每帧至多一件 InstantiateSync，避免奖励尖峰
            materializing = true;
            int typeId = typeIdSnapshot[nextIndex];
            ItemStatsSystem.Item item = null;
            bool committed = false;

            try
            {
                item = ItemStatsSystem.ItemAssetsCollection.InstantiateSync(typeId);
                if (item != null)
                {
                    if (ModeGRewardTransaction.TryCommitItemToInventoryOrStorage(
                        item, targetInventory, "奖励 TypeID=" + typeId))
                    {
                        committed = true;
                        succeededCount++;
                    }
                }
            }
            catch (Exception materializeEx)
            {
                committed = false;
                ModBehaviour.DevLog("[ModeG] [WARNING] 奖励 strict materializer 单件实例化异常: typeId=" + typeId + ", " + materializeEx.Message);
            }

            if (!committed)
            {
                try { if (item != null) item.DestroyTree(); } catch { }
                item = null;
            }

            if (!committed) failedCount++;
            nextIndex++;

            try
            {
                if (onItemCommitted != null)
                {
                    onItemCommitted(typeId, item, committed);
                }
            }
            catch (Exception callbackEx)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 奖励单件完成回调异常: typeId=" + typeId + ", " + callbackEx.Message);
            }

            materializing = false;
            if (cancelRequested)
            {
                CancelAndDestroy();
                return;
            }
            if (nextIndex >= typeIdSnapshot.Length)
            {
                finished = true;

                int total = typeIdSnapshot.Length;
                int succeeded = succeededCount;
                int failed = failedCount;

                Action<int, int, int> completionCallback = onMaterializationCompleted;
                typeIdSnapshot = null;
                targetInventory = null;
                onItemCommitted = null;
                onMaterializationCompleted = null;

                try
                {
                    if (completionCallback != null)
                    {
                        completionCallback(total, succeeded, failed);
                    }
                }
                catch (Exception completionEx)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 奖励 materializer 完成回调异常: " + completionEx.Message);
                }

                try
                {
                    if (gameObject != null)
                    {
                        UnityEngine.Object.Destroy(gameObject);
                    }
                }
                catch { }
            }
        }

        private void OnDestroy()
        {
            if (!finished) CancelAndDestroy();
        }
    }

    internal static class VictoryRewardCrateHeroVisual
    {
        private const float HeroShellScaleMultiplier = 2f;
        // 常驻灯只照亮箱子周围一小片（旧版贴地 8 m / 5.5，约 16 m 宽的地面被洗平）；抬到箱子中部高度
        private const float AuraRange = 2.5f;
        private const float AuraIntensity = 1.8f;
        private const float AuraHeight = 0.9f;
        private const float AuraFadeInSeconds = 0.4f;
        private const float AuraBreatheAmplitude = 0.15f;
        private const float AuraBreathePeriod = 2.9f;

        // 虚影触地那一帧由控制器设置、AttachToLootbox 读完即清（控制器 finally 里也会清）：
        // 外壳沿用虚影的朝向，并从虚影触地时的压扁接着回弹
        private static bool landingHandoffActive;
        private static Quaternion landingHandoffRotation = Quaternion.identity;

        internal static void BeginLandingHandoff(Quaternion ghostRotation)
        {
            landingHandoffActive = true;
            landingHandoffRotation = ghostRotation;
        }

        internal static void EndLandingHandoff()
        {
            landingHandoffActive = false;
        }

        internal static void AttachToLootbox(InteractableLootbox lootbox, InteractableLootbox visualPrefab)
        {
            bool landed = landingHandoffActive;
            landingHandoffActive = false;
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
                shell.transform.localRotation = landed
                    ? Quaternion.Inverse(lootbox.transform.rotation) * landingHandoffRotation
                    : Quaternion.identity;
                shell.transform.localScale = Vector3.one * HeroShellScaleMultiplier;

                PrepareHeroShell(shell);
                if (landed)
                {
                    shell.AddComponent<VictoryRewardCrateLandingBounce>();
                }
                CreateAuraLight(lootbox);

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

            // 外壳是实心的金色箱子：保留官方材质与投影（不再伪造半透明，见 VictoryRewardShadowCrateController 文件头），
            // 只经属性块写 _Tint 与一丝自发光
            try
            {
                VictoryRewardCrateTint.Apply(
                    shell.GetComponentsInChildren<Renderer>(true),
                    VictoryRewardCrateTint.HeroTint,
                    VictoryRewardCrateTint.HeroEmission);
            }
            catch {}

            EnsureVisualChildrenVisible(shell);
        }

        /// <summary>
        /// 常驻灯不挂在箱子下面，由 VictoryRewardCrateAuraFollower 跟随（箱子可被搬运）：
        /// 箱子被打开或被销毁时灯还在，才能 0.3 s 淡出而不是跟着一帧消失。
        /// </summary>
        private static void CreateAuraLight(InteractableLootbox lootbox)
        {
            Transform target = lootbox != null ? lootbox.transform : null;
            if (target == null)
            {
                return;
            }

            Vector3 offset = new Vector3(0f, AuraHeight, 0f);
            GameObject auraLightObject = new GameObject("BossRush_VictoryRewardHeroAuraLight");
            auraLightObject.transform.SetParent(target.parent, false);
            auraLightObject.transform.position = target.position + offset;
            Light aura = auraLightObject.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.color = VictoryRewardCrateTint.AuraLightColor;
            aura.range = AuraRange;
            aura.intensity = 0f;
            aura.shadows = LightShadows.None;
            BossRushFxLightFade fade = BossRushFxLightFade.Attach(aura, AuraIntensity, AuraFadeInSeconds,
                AuraBreatheAmplitude, AuraBreathePeriod);
            auraLightObject.AddComponent<VictoryRewardCrateAuraFollower>().Bind(lootbox, fade, offset);
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
    }
}
