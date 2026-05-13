using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool PlaceModeFortification(FortificationType type, Vector3 position, Quaternion rotation, GameObject existingPreview = null)
        {
            GameObject fortObj = existingPreview;
            bool reusedPreview = fortObj != null;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    return false;
                }

                FortDef def = FortDef.Get(type);
                if (def == null)
                {
                    DevLog("[ModeF] [WARNING] Unknown fortification type: " + type);
                    return false;
                }
                string prefabName = def.PrefabName;
                float maxHealth   = def.MaxHp;

                if (!reusedPreview)
                {
                    fortObj = EntityModelFactory.Create(prefabName, position, rotation);
                    if (fortObj == null)
                    {
                        DevLog("[ModeF] [WARNING] Failed to create fortification model, falling back to runtime geometry: " + prefabName);
                        fortObj = CreateFallbackModeFFortification(type, position, rotation);
                        if (fortObj == null)
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    RestoreFortPlacementPreviewMaterials();
                }

                EnsureModeFFortificationRenderersVisible(fortObj);

                if (!reusedPreview && fortObj.activeSelf)
                {
                    fortObj.SetActive(false);
                }

                fortObj.name = "ModeF_Fort_" + type + "_" + Time.frameCount;

                if (fortObj.activeSelf)
                {
                    fortObj.SetActive(false);
                }

                Health health = fortObj.GetComponent<Health>();
                if (health == null)
                {
                    health = fortObj.AddComponent<Health>();
                }

                Teams fortTeam = GetModeFFortificationOwnerTeam(player);
                EnsureModeFFortificationHealthRuntime(health, maxHealth, fortTeam);
                ConfigureModeFFortificationPhysics(fortObj);
                EnsureModeFFortificationDamageTarget(fortObj, type, health, fortTeam);
                EnsureModeFFortificationWallCollider(fortObj, type, health);
                EnsureModeFFortificationCharacterBlocker(fortObj);
                EnsureModeFFortificationHalfObstacleRegistration(fortObj, type);
                try { health.CurrentHealth = maxHealth; } catch { }

                ModeFFortificationMarker marker = fortObj.GetComponent<ModeFFortificationMarker>();
                if (marker == null)
                {
                    marker = fortObj.AddComponent<ModeFFortificationMarker>();
                }

                marker.Type = type;
                marker.Owner = player;
                marker.OwnerCharacterId = player.GetInstanceID();
                marker.MaxHealth = maxHealth;
                marker.LastKnownHealth = maxHealth;
                marker.IsDestroyed = false;
                marker.BoundHealth = health;

                // 精确移除旧 listener（仅移除我们上一次注册的那个），避免对象复用时回调重复注册；
                // 不使用 RemoveAllListeners 以免误删游戏内部组件（DamageReceiver/HurtVisual/HealthBar）自动注册的监听器。
                if (health.OnDeadEvent != null)
                {
                    if (marker.RegisteredDeadListener != null)
                    {
                        health.OnDeadEvent.RemoveListener(marker.RegisteredDeadListener);
                    }
                    UnityAction<DamageInfo> deadListener = (damageInfo) => OnFortificationDestroyed(marker);
                    health.OnDeadEvent.AddListener(deadListener);
                    marker.RegisteredDeadListener = deadListener;
                }
                if (health.OnHurtEvent != null)
                {
                    if (marker.RegisteredHurtListener != null)
                    {
                        health.OnHurtEvent.RemoveListener(marker.RegisteredHurtListener);
                    }
                    UnityAction<DamageInfo> hurtListener = (damageInfo) => OnFortificationHurt(marker, damageInfo);
                    health.OnHurtEvent.AddListener(hurtListener);
                    marker.RegisteredHurtListener = hurtListener;
                }

                if (!fortObj.activeSelf)
                {
                    fortObj.SetActive(true);
                }

                // SetActive 触发 Awake/Start，DamageReceiver 在子对象上，
                // GetComponent<Health>() 找不到根对象的 Health，导致 health == null。
                // 因此在激活后重新绑定一次。
                ReapplyModeFFortificationDamageReceiverHealth(fortObj, health);

                // 显示血条
                try
                {
                    health.showHealthBar = true;
                    health.RequestHealthBar();
                }
                catch { }

                EnsureModeFFortificationRenderersVisible(fortObj);
                fortObj.transform.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();

                modeFState.ActiveFortifications[marker.FortificationId] = marker;
                if (IsZombieModeActive && zombieModeRunState.RunId > 0)
                {
                    RegisterZombieModeRunOnlyObject(
                        zombieModeRunState.RunId,
                        ZombieModeRunOnlyObjectKind.Fortification,
                        fortObj,
                        marker,
                        delegate
                        {
                            if (marker != null &&
                                modeFState != null &&
                                modeFState.ActiveFortifications != null)
                            {
                                modeFState.ActiveFortifications.Remove(marker.FortificationId);
                            }
                        });
                }

                string typeName = GetFortificationTypeName(type);
                DevLog("[ModeF] [PLACE] " + typeName
                    + " | reusedPreview=" + reusedPreview
                    + " | pos=" + position
                    + " | renderers=" + fortObj.GetComponentsInChildren<Renderer>(true).Length
                    + " | hp=" + marker.LastKnownHealth + "/" + marker.MaxHealth);
                if ((FortDef.Get(type)?.IsHalfObstacle ?? false))
                {
                    DevLog("[ModeF] [HALF] " + typeName + GetModeFFortificationColliderDebugText(fortObj));
                }
                ClearFortPlacementPreviewMaterialCache();
                ShowModeFRewardBubble(typeName + L10n.T("已部署", " deployed"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] PlaceModeFortification failed: " + e.Message + "\n" + e.StackTrace);
                try
                {
                    if (fortObj != null)
                    {
                        UnityEngine.Object.Destroy(fortObj);
                    }
                }
                catch { }
                ClearFortPlacementPreviewMaterialCache();
                return false;
            }
        }

        private bool CanPlaceModeFFortification(
            CharacterMainControl owner,
            Transform candidateTransform,
            BoxCollider candidateCollider,
            FortificationType type,
            out string failureMessage)
        {
            failureMessage = null;
            if (candidateTransform == null)
            {
                return false;
            }

            float candidateRadius = GetModeFFortificationFootprintRadius(candidateTransform, candidateCollider, type);
            if (owner != null && owner.transform != null)
            {
                float minPlayerDistance = candidateRadius + 0.65f;
                if ((candidateTransform.position - owner.transform.position).sqrMagnitude <
                    minPlayerDistance * minPlayerDistance)
                {
                    failureMessage = L10n.T(
                        "部署位置过近，请向前移动后重试",
                        "Deployment is too close to you. Move forward and try again.");
                    return false;
                }
            }

            if (IsModeFFortificationPlacementBlocked(owner, candidateTransform, candidateCollider, type))
            {
                failureMessage = L10n.T(
                    "部署位置被场景物体占用，请更换位置",
                    "The deployment space is blocked by world geometry. Try another spot.");
                return false;
            }

            return true;
        }

        private float GetModeFFortificationFootprintRadius(
            Transform fortTransform,
            BoxCollider rootCollider,
            FortificationType type)
        {
            Bounds localBounds = rootCollider != null
                ? new Bounds(rootCollider.center, rootCollider.size)
                : GetModeFFortificationDefaultBounds(type);
            Vector3 scale = fortTransform != null ? fortTransform.lossyScale : Vector3.one;
            float extentX = Mathf.Abs(localBounds.extents.x * scale.x);
            float extentZ = Mathf.Abs(localBounds.extents.z * scale.z);
            return Mathf.Max(0.5f, Mathf.Max(extentX, extentZ));
        }

        private bool IsModeFFortificationPlacementBlocked(
            CharacterMainControl owner,
            Transform candidateTransform,
            BoxCollider candidateCollider,
            FortificationType type)
        {
            if (candidateTransform == null)
            {
                return true;
            }

            Bounds localBounds = candidateCollider != null
                ? new Bounds(candidateCollider.center, candidateCollider.size)
                : GetModeFFortificationDefaultBounds(type);
            Vector3 center = candidateTransform.TransformPoint(localBounds.center);
            Vector3 halfExtents = GetModeFFortificationPlacementHalfExtents(candidateTransform, localBounds);
            int layerMask = GetModeFFortificationPlacementObstacleLayerMask();
            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                modeFFortificationPlacementOverlapBuffer,
                candidateTransform.rotation,
                layerMask);
            bool overlapBufferExceeded = hitCount >= modeFFortificationPlacementOverlapBuffer.Length;
            int scanCount = Mathf.Min(hitCount, modeFFortificationPlacementOverlapBuffer.Length);

            for (int i = 0; i < scanCount; i++)
            {
                Collider hit = modeFFortificationPlacementOverlapBuffer[i];
                modeFFortificationPlacementOverlapBuffer[i] = null;

                if (ShouldIgnoreModeFFortificationPlacementHit(hit, owner, candidateTransform))
                {
                    continue;
                }

                return true;
            }

            if (overlapBufferExceeded)
            {
                DevLog("[ModeF] [WARNING] Fortification placement overlap buffer exceeded, blocking placement defensively");
                return true;
            }

            return false;
        }

        private Vector3 GetModeFFortificationPlacementHalfExtents(Transform fortTransform, Bounds localBounds)
        {
            Vector3 scale = fortTransform != null ? fortTransform.lossyScale : Vector3.one;
            return new Vector3(
                Mathf.Max(0.12f, Mathf.Abs(localBounds.extents.x * scale.x) + FORT_WORLD_COLLISION_PADDING),
                Mathf.Max(0.12f, Mathf.Abs(localBounds.extents.y * scale.y) - FORT_WORLD_COLLISION_PADDING),
                Mathf.Max(0.12f, Mathf.Abs(localBounds.extents.z * scale.z) + FORT_WORLD_COLLISION_PADDING));
        }

        private bool ShouldIgnoreModeFFortificationPlacementHit(
            Collider hit,
            CharacterMainControl owner,
            Transform candidateTransform)
        {
            if (hit == null || !hit.enabled || hit.isTrigger)
            {
                return true;
            }

            Transform hitTransform = hit.transform;
            if (hitTransform == null)
            {
                return true;
            }

            if (candidateTransform != null && hitTransform.IsChildOf(candidateTransform))
            {
                return true;
            }

            if (owner != null)
            {
                if (hitTransform.IsChildOf(owner.transform))
                {
                    return true;
                }

                CharacterMainControl hitCharacter = hit.GetComponentInParent<CharacterMainControl>();
                if (hitCharacter != null && hitCharacter == owner)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetModeFFortificationPlacementObstacleLayerMask()
        {
            if (cachedModeFFortificationPlacementObstacleLayerMask != int.MinValue)
            {
                return cachedModeFFortificationPlacementObstacleLayerMask;
            }

            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask |
                       Duckov.Utilities.GameplayDataSettings.Layers.fowBlockLayers;
            }
            catch { }

            if (mask == 0)
            {
                mask = LayerMask.GetMask("Default", "Wall", "Obstacle");
            }

            if (mask == 0)
            {
                mask = ~0;
            }

            cachedModeFFortificationPlacementObstacleLayerMask = mask;
            return cachedModeFFortificationPlacementObstacleLayerMask;
        }

        // ★低端机优化：放置预览鼠标射线层掩码（缓存，避免每帧重算）。
        // 主 mask = ground + wall：覆盖绝大多数有效放置面；fallback = ~0 兜底（见下方函数注释）。
        private static int cachedModeFFortificationPlacementRaycastMask = int.MinValue;

        private int GetModeFFortificationPlacementRaycastMask()
        {
            if (cachedModeFFortificationPlacementRaycastMask != int.MinValue)
            {
                return cachedModeFFortificationPlacementRaycastMask;
            }

            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask |
                       Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
            }
            catch { }

            if (mask == 0)
            {
                mask = LayerMask.GetMask("Default", "Ground", "Wall", "Obstacle");
            }

            cachedModeFFortificationPlacementRaycastMask = mask;
            return cachedModeFFortificationPlacementRaycastMask;
        }

        // Fallback mask = ~0（全层）。
        // ★ 行为兼容性保证：与 pre-optimization 版本 100% 等价，避免"鼠标悬停在
        //   Default/Character/Pickup/TransparentFX 等层时 preview 不跟随"的回归。
        // ★ 性能仍然受益：主 mask（ground+wall）在 99% 帧里命中，fallback 只在极少数
        //   "鼠标在虚空/其他层"场景触发；每帧成本 = 1 次窄 Raycast + 最多 1 次 ~0 Raycast，
        //   与原始代码完全一致（只是把内联 ~0 抽到了辅助函数里）。
        private int GetModeFFortificationPlacementRaycastFallbackMask()
        {
            return ~0;
        }

        // ★低端机优化：维修悬停 Raycast 层掩码（缓存）。
        // 只需检测工事碰撞体所在的层（damageReceiver + wall），不需要扫描全场。
        private static int cachedModeFFortificationRepairHoverMask = int.MinValue;

        private int GetModeFFortificationRepairHoverMask()
        {
            if (cachedModeFFortificationRepairHoverMask != int.MinValue)
            {
                return cachedModeFFortificationRepairHoverMask;
            }

            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask |
                       Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
            }
            catch { }

            if (mask == 0)
            {
                mask = LayerMask.GetMask("DamageReceiver", "Wall", "Obstacle");
            }

            cachedModeFFortificationRepairHoverMask = mask;
            return cachedModeFFortificationRepairHoverMask;
        }

        private Teams GetModeFFortificationOwnerTeam(CharacterMainControl owner)
        {
            // 工事归属仍由 OwnerCharacterId 追踪；受击 team 使用 middle，
            // 这样玩家和敌人的子弹都能正常命中工事，同时 AI 不会把工事当成主动敌对目标。
            return Teams.middle;
        }


        private void EnsureModeFFortificationHealthRuntime(Health health, float maxHealth, Teams ownerTeam)
        {
            if (health == null)
            {
                return;
            }

            EnsureModeFHealthUnityEvent(health, "OnHealthChange", typeof(UnityEvent<Health>));
            EnsureModeFHealthUnityEvent(health, "OnMaxHealthChange", typeof(UnityEvent<Health>));
            EnsureModeFHealthUnityEvent(health, "OnDeadEvent", typeof(UnityEvent<DamageInfo>));
            EnsureModeFHealthUnityEvent(health, "OnHurtEvent", typeof(UnityEvent<DamageInfo>));

            health.autoInit = false;
            health.hasSoul = false;
            health.team = ownerTeam;
            health.healthBarHeight = 1.1f;
            health.CanDieIfNotRaidMap = true;
            EnsureModeFFortificationVulnerable(health);

            if (healthDefaultMaxHealthField != null)
            {
                try { healthDefaultMaxHealthField.SetValue(health, Mathf.RoundToInt(maxHealth)); } catch { }
            }

            if (health.CurrentHealth <= 0f)
            {
                health.CurrentHealth = Mathf.Max(maxHealth, 1f);
            }
        }

        private static void EnsureModeFHealthUnityEvent(Health health, string memberName, Type fallbackEventType)
        {
            if (health == null || string.IsNullOrEmpty(memberName))
            {
                return;
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo field = typeof(Health).GetField(memberName, Flags);
            if (field != null)
            {
                if (field.GetValue(health) == null)
                {
                    object eventInstance = CreateModeFUnityEventInstance(field.FieldType, fallbackEventType);
                    if (eventInstance != null)
                    {
                        field.SetValue(health, eventInstance);
                    }
                }
                return;
            }

            PropertyInfo property = typeof(Health).GetProperty(memberName, Flags);
            if (property != null && property.CanRead && property.CanWrite && property.GetValue(health, null) == null)
            {
                object eventInstance = CreateModeFUnityEventInstance(property.PropertyType, fallbackEventType);
                if (eventInstance != null)
                {
                    property.SetValue(health, eventInstance, null);
                }
            }
        }

        private static object CreateModeFUnityEventInstance(Type memberType, Type fallbackEventType)
        {
            if (memberType == null || !typeof(UnityEventBase).IsAssignableFrom(memberType))
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(memberType);
            }
            catch { }

            if (fallbackEventType != null && memberType.IsAssignableFrom(fallbackEventType))
            {
                try
                {
                    return Activator.CreateInstance(fallbackEventType);
                }
                catch { }
            }

            return null;
        }

        private static void EnsureModeFFortificationVulnerable(Health health)
        {
            if (health == null)
            {
                return;
            }

            try { health.SetInvincible(false); } catch { }
        }

        private static void ConfigureModeFFortificationPhysics(GameObject fortObj)
        {
            if (fortObj == null)
            {
                return;
            }

            Rigidbody body = fortObj.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = fortObj.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        private static void EnsureModeFFortificationRenderersVisible(GameObject fortObj)
        {
            if (fortObj == null)
            {
                return;
            }

            Renderer[] renderers = fortObj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = true;
                }
            }
        }

        private void EnsureModeFFortificationDamageTarget(GameObject fortObj, FortificationType type, Health health, Teams ownerTeam)
        {
            if (fortObj == null || health == null)
            {
                return;
            }

            health.team = ownerTeam;

            Collider damageCollider = EnsureModeFFortificationDamageCollider(fortObj, type);
            if (damageCollider == null)
            {
                return;
            }

            // 销毁根对象上可能残留的 DamageReceiver，避免与子对象的 DamageReceiver 冲突导致双重扣血。
            DamageReceiver rootReceiver = fortObj.GetComponent<DamageReceiver>();
            if (rootReceiver != null)
            {
                rootReceiver.enabled = false;
                UnityEngine.Object.Destroy(rootReceiver);
            }

            DamageReceiver receiver = damageCollider.GetComponent<DamageReceiver>();
            if (receiver == null)
            {
                receiver = damageCollider.gameObject.AddComponent<DamageReceiver>();
            }
            ConfigureModeFFortificationDamageReceiver(receiver, type, health);

            int damageReceiverLayer = FortLayers.DamageReceiver;
            if (damageReceiverLayer >= 0)
            {
                damageCollider.gameObject.layer = damageReceiverLayer;
            }
        }

        /// <summary>
        /// 配置单个 DamageReceiver 的公共属性和反射字段。
        /// </summary>
        private static void ConfigureModeFFortificationDamageReceiver(DamageReceiver receiver, FortificationType type, Health health)
        {
            if (receiver == null) return;

            receiver.useSimpleHealth = false;
            // 故意不标记为 halfObsticle：
            // Projectile.cs / Accessory_Lazer.cs / TecLazer.cs 在 ignoreHalfObsticle=true
            // （高暴击、追踪弹、玩家在掩体旁）时会对 isHalfObsticle=true 的 DamageReceiver 走 goto 跳过，
            // 跳过后子弹继续遍历同帧 hits，命中同一工事的 Wall 层 wallObj 进入障碍物分支直接死亡，
            // 最终表现为"有些子弹打工事不扣血"。工事"半障碍物"的穿越/掩体语义由独立的
            // ModeFHalfObstacleTrigger 子对象负责，DamageReceiver 自身无需承担该标记。
            receiver.isHalfObsticle = false;
            // type 参数暂未使用，保留签名便于将来按工事类型差异化配置（如不同伤害修正、反射字段）
            _ = type;

            if (receiver.OnHurtEvent == null)
            {
                receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
            }
            if (receiver.OnDeadEvent == null)
            {
                receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
            }

            try
            {
                if (damageReceiverHealthField != null)
                    damageReceiverHealthField.SetValue(receiver, health);
                else if (damageReceiverHealthProperty != null && damageReceiverHealthProperty.CanWrite)
                    damageReceiverHealthProperty.SetValue(receiver, health, null);
            }
            catch { }

            try
            {
                if (damageReceiverOnlyExplosionField != null)
                    damageReceiverOnlyExplosionField.SetValue(receiver, false);
                else if (damageReceiverOnlyExplosionProperty != null && damageReceiverOnlyExplosionProperty.CanWrite)
                    damageReceiverOnlyExplosionProperty.SetValue(receiver, false, null);
            }
            catch { }
        }

        /// <summary>
        /// SetActive(true) 后重新绑定所有子 DamageReceiver 的 health 引用。
        /// DamageReceiver.Start() 不会覆盖 health，但作为防御性措施，
        /// 确保所有 DamageReceiver（包括预制体自带的）都指向正确的 Health 实例。
        /// </summary>
        private static void ReapplyModeFFortificationDamageReceiverHealth(GameObject fortObj, Health health)
        {
            if (fortObj == null || health == null) return;
            try
            {
                DamageReceiver[] receivers = fortObj.GetComponentsInChildren<DamageReceiver>(true);
                for (int i = 0; i < receivers.Length; i++)
                {
                    DamageReceiver r = receivers[i];
                    if (r == null) continue;
                    try
                    {
                        if (damageReceiverHealthField != null)
                            damageReceiverHealthField.SetValue(r, health);
                        else if (damageReceiverHealthProperty != null && damageReceiverHealthProperty.CanWrite)
                            damageReceiverHealthProperty.SetValue(r, health, null);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void EnsureModeFFortificationWallCollider(GameObject fortObj, FortificationType type, Health health)
        {
            if (fortObj == null)
            {
                return;
            }

            try
            {
                Transform wallTransform = fortObj.transform.Find("ModeF_WallCollider");
                GameObject wallObj = wallTransform != null
                    ? wallTransform.gameObject
                    : new GameObject("ModeF_WallCollider");
                wallObj.transform.SetParent(fortObj.transform, false);
                wallObj.transform.localPosition = Vector3.zero;
                wallObj.transform.localRotation = Quaternion.identity;
                wallObj.transform.localScale = Vector3.one;

                // WallCollider 始终使用 Wall 层，这样子弹射线检测才能命中并被阻挡。
                // HalfObstacle 的注册由单独的 ModeF_HalfObstacleTrigger 子对象负责。
                int wallLayer = FortLayers.Wall;
                if (wallLayer >= 0)
                {
                    wallObj.layer = wallLayer;
                }

                Collider[] childColliders = wallObj.GetComponents<Collider>();
                BoxCollider wallCollider = null;
                for (int i = 0; i < childColliders.Length; i++)
                {
                    BoxCollider existingBox = childColliders[i] as BoxCollider;
                    if (existingBox != null && wallCollider == null)
                    {
                        wallCollider = existingBox;
                        continue;
                    }

                    childColliders[i].enabled = false;
                }

                if (wallCollider == null)
                {
                    wallCollider = wallObj.AddComponent<BoxCollider>();
                }

                // ★低端机优化：Wall collider 现在同时承担"挡子弹"+"挡角色移动"两种职责，
                // bounds 取 Wall 和 Blocker-shrunk 的并集，并 clamp 到 Damage bounds 内部。
                // 这样每个工事只需 1 个 Wall GameObject + BoxCollider，
                // 替代原来 Wall + CharacterBlocker 两个 collider，减少物理查询成本。
                // Blocker GameObject 由 EnsureModeFFortificationCharacterBlocker 负责清理销毁。
                Bounds wallBounds = GetModeFFortificationLocalBounds(
                    fortObj.transform,
                    (FortDef.Get(type)?.WallColliderBounds ?? GetModeFFortificationDefaultBounds(type)));
                Bounds blockerBounds = GetModeFFortificationLocalBounds(
                    fortObj.transform,
                    (FortDef.Get(type)?.CharacterBlockerBounds ?? GetModeFFortificationDefaultBounds(type)));
                Bounds damageBoundsForMerge = GetModeFFortificationLocalBounds(
                    fortObj.transform,
                    (FortDef.Get(type)?.DamageColliderBounds ?? GetModeFFortificationDefaultBounds(type)));
                blockerBounds = ShrinkBlockerBoundsInsideDamage(blockerBounds, damageBoundsForMerge);
                Bounds bounds = UnionBoundsClampedToContainer(wallBounds, blockerBounds, damageBoundsForMerge);
                wallCollider.enabled = true;
                wallCollider.isTrigger = false;
                wallCollider.center = bounds.center;
                wallCollider.size = bounds.size;

                // Wall 子对象故意不挂 DamageReceiver：
                // 实测游戏的 damageReceiverLayerMask 同时包含 Wall 层，OverlapSphere 会同时命中
                // damageCollider 和 wallObj 两个 collider。若 Wall 上也有 DamageReceiver，
                // 近战武器 / 凤凰戟爆燃（按 receiver.InstanceID 去重）会对同一工事扣两次血。
                // damageCollider 子对象上的唯一 DamageReceiver 已由 EnsureModeFFortificationDamageTarget
                // 负责挂载和配置，近战武器命中 Wall 时 GetComponent<DamageReceiver>() 返回 null
                // 自动跳过，爆炸按 Health 去重不受影响，行为正确。
                // 兜底清理：池化复用时若旧版本曾经在 Wall 上添加过 DamageReceiver，这里销毁避免双扣血残留。
                DamageReceiver staleWallReceiver = wallObj.GetComponent<DamageReceiver>();
                if (staleWallReceiver != null)
                {
                    staleWallReceiver.enabled = false;
                    UnityEngine.Object.Destroy(staleWallReceiver);
                }
                _ = health;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFFortificationWallCollider failed: " + e.Message);
            }
        }

        /// <summary>
        /// ★低端机优化：原本为每个工事创建独立 ModeF_CharacterBlocker 子对象的逻辑已合并到 WallCollider。
        /// 此函数现在只做清理：销毁已存在的 ModeF_CharacterBlocker 子对象（兼容池化复用和存档升级）。
        /// </summary>
        private void EnsureModeFFortificationCharacterBlocker(GameObject fortObj)
        {
            if (fortObj == null)
            {
                return;
            }

            try
            {
                Transform blockerTransform = fortObj.transform.Find("ModeF_CharacterBlocker");
                if (blockerTransform != null)
                {
                    UnityEngine.Object.Destroy(blockerTransform.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFFortificationCharacterBlocker cleanup failed: " + e.Message);
            }
        }

        /// <summary>
        /// 把 a 和 b 的 bounds 合并为并集，并 clamp 到 container 内部（确保每轴不超出）。
        /// 用于把 Wall+Blocker 合并到单个 Wall collider 同时保持在 Damage bounds 内。
        /// </summary>
        private static Bounds UnionBoundsClampedToContainer(Bounds a, Bounds b, Bounds container)
        {
            Bounds union = a;
            union.Encapsulate(b);

            Vector3 containerMin = container.center - container.extents;
            Vector3 containerMax = container.center + container.extents;
            Vector3 unionMin = union.center - union.extents;
            Vector3 unionMax = union.center + union.extents;

            Vector3 clampedMin = new Vector3(
                Mathf.Max(unionMin.x, containerMin.x),
                Mathf.Max(unionMin.y, containerMin.y),
                Mathf.Max(unionMin.z, containerMin.z));
            Vector3 clampedMax = new Vector3(
                Mathf.Min(unionMax.x, containerMax.x),
                Mathf.Min(unionMax.y, containerMax.y),
                Mathf.Min(unionMax.z, containerMax.z));

            Vector3 size = clampedMax - clampedMin;
            size.x = Mathf.Max(0.05f, size.x);
            size.y = Mathf.Max(0.05f, size.y);
            size.z = Mathf.Max(0.05f, size.z);
            Vector3 center = (clampedMin + clampedMax) * 0.5f;
            return new Bounds(center, size);
        }

        /// <summary>
        /// 把 Blocker bounds 收缩到 Damage bounds 内部，并留出一点 margin。
        /// 这样 Physics.SphereCastAll 距离排序时 DamageReceiver 永远在最外侧先被命中，
        /// 避免 Wall 层的 Blocker 先触发 Projectile 障碍物分支（"有烟雾无伤害数字"）。
        /// </summary>
        private static Bounds ShrinkBlockerBoundsInsideDamage(Bounds blocker, Bounds damage)
        {
            const float shrinkMargin = 0.02f;
            const float minAxisSize = 0.05f;

            Vector3 maxAllowedSize = new Vector3(
                Mathf.Max(minAxisSize, damage.size.x - shrinkMargin),
                Mathf.Max(minAxisSize, damage.size.y - shrinkMargin),
                Mathf.Max(minAxisSize, damage.size.z - shrinkMargin));

            Vector3 newSize = new Vector3(
                Mathf.Min(blocker.size.x, maxAllowedSize.x),
                Mathf.Min(blocker.size.y, maxAllowedSize.y),
                Mathf.Min(blocker.size.z, maxAllowedSize.z));

            Vector3 halfBlocker = newSize * 0.5f;
            Vector3 halfDamage = damage.size * 0.5f;
            Vector3 minCenter = damage.center - halfDamage + halfBlocker;
            Vector3 maxCenter = damage.center + halfDamage - halfBlocker;

            Vector3 newCenter = new Vector3(
                Mathf.Clamp(blocker.center.x, minCenter.x, maxCenter.x),
                Mathf.Clamp(blocker.center.y, minCenter.y, maxCenter.y),
                Mathf.Clamp(blocker.center.z, minCenter.z, maxCenter.z));

            return new Bounds(newCenter, newSize);
        }

        private Collider EnsureModeFFortificationDamageCollider(GameObject fortObj, FortificationType type)
        {
            if (fortObj == null)
            {
                return null;
            }

            Collider[] existingColliders = fortObj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existingColliders.Length; i++)
            {
                if (existingColliders[i] == null)
                {
                    continue;
                }

                existingColliders[i].enabled = false;
            }

            Transform damageTransform = fortObj.transform.Find("ModeF_DamageReceiver");
            GameObject damageObj = damageTransform != null
                ? damageTransform.gameObject
                : new GameObject("ModeF_DamageReceiver");
            damageObj.transform.SetParent(fortObj.transform, false);
            damageObj.transform.localPosition = Vector3.zero;
            damageObj.transform.localRotation = Quaternion.identity;
            damageObj.transform.localScale = Vector3.one;

            int damageReceiverLayer = FortLayers.DamageReceiver;
            if (damageReceiverLayer >= 0)
            {
                damageObj.layer = damageReceiverLayer;
            }

            Collider[] damageColliders = damageObj.GetComponents<Collider>();
            BoxCollider damageCollider = null;
            for (int i = 0; i < damageColliders.Length; i++)
            {
                BoxCollider existingBox = damageColliders[i] as BoxCollider;
                if (existingBox != null && damageCollider == null)
                {
                    damageCollider = existingBox;
                    continue;
                }

                damageColliders[i].enabled = false;
            }

            if (damageCollider == null)
            {
                damageCollider = damageObj.AddComponent<BoxCollider>();
            }

            Bounds localBounds = GetModeFFortificationLocalBounds(
                fortObj.transform,
                (FortDef.Get(type)?.DamageColliderBounds ?? GetModeFFortificationDefaultBounds(type)));
            damageCollider.enabled = true;
            damageCollider.isTrigger = false;
            damageCollider.center = localBounds.center;
            damageCollider.size = localBounds.size;
            return damageCollider;
        }





        private void EnsureModeFFortificationHalfObstacleRegistration(GameObject fortObj, FortificationType type)
        {
            if (fortObj == null)
            {
                return;
            }

            Transform triggerTransform = fortObj.transform.Find("ModeF_HalfObstacleTrigger");
            ModeFHalfObstacleTrigger triggerComponent = triggerTransform != null
                ? triggerTransform.GetComponent<ModeFHalfObstacleTrigger>()
                : null;

            if (!(FortDef.Get(type)?.IsHalfObstacle ?? false))
            {
                if (triggerTransform != null)
                {
                    UnityEngine.Object.Destroy(triggerTransform.gameObject);
                }
                return;
            }

            Transform damageTransform = fortObj.transform.Find("ModeF_DamageReceiver");
            GameObject damagePart = damageTransform != null ? damageTransform.gameObject : null;
            if (damagePart == null)
            {
                return;
            }

            // 同时注册 Wall 子对象为 halfObstacle part：
            // 玩家站在工事旁时，ItemAgent_Gun 会把 GetNearByHalfObsticles() 加入子弹的 damagedObjects，
            // 使子弹跳过这些 collider 不做任何处理（不扣血、不死亡）。
            // 若此处只注册 damageCollider，玩家附近工事的 wallObj（Wall 层）不在 damagedObjects 中，
            // 子弹按距离遍历同帧 hits 时在 damageCollider 被跳过后仍会命中 wallObj，
            // 进入 Projectile.cs 的 else 障碍物分支：dead=true + 生成 BulletHitObsticleFx 烟雾但不扣血，
            // 表现为"打中硬体烟雾但没出现伤害数字"。因此必须把 wallObj 一起注册。
            Transform wallPartTransform = fortObj.transform.Find("ModeF_WallCollider");
            GameObject wallPart = wallPartTransform != null ? wallPartTransform.gameObject : null;
            // ★低端机优化：CharacterBlocker 已合并进 WallCollider，不再单独注册。

            GameObject triggerObj = triggerTransform != null
                ? triggerTransform.gameObject
                : new GameObject("ModeF_HalfObstacleTrigger");
            triggerObj.transform.SetParent(fortObj.transform, false);
            triggerObj.transform.localPosition = Vector3.zero;
            triggerObj.transform.localRotation = Quaternion.identity;
            triggerObj.transform.localScale = Vector3.one;

            BoxCollider triggerCollider = triggerObj.GetComponent<BoxCollider>();
            if (triggerCollider == null)
            {
                triggerCollider = triggerObj.AddComponent<BoxCollider>();
            }

            Bounds triggerBounds = GetModeFFortificationLocalBounds(
                fortObj.transform,
                (FortDef.Get(type)?.HalfObstacleTriggerBounds ?? new Bounds(Vector3.zero, Vector3.zero)));
            triggerCollider.enabled = true;
            triggerCollider.isTrigger = true;
            triggerCollider.center = triggerBounds.center;
            triggerCollider.size = triggerBounds.size;

            if (triggerComponent == null)
            {
                triggerComponent = triggerObj.AddComponent<ModeFHalfObstacleTrigger>();
            }

            // SetRegisteredParts 会过滤 null，wallPart 为 null（预制体无 Wall 子对象）时安全降级。
            triggerComponent.SetRegisteredParts(damagePart, wallPart);
        }

        private Bounds GetModeFFortificationDefaultBounds(FortificationType type)
        {
            FortDef def = FortDef.Get(type);
            return def != null ? def.DefaultBounds : new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
        }

        private Bounds GetModeFFortificationLocalBounds(Transform fortTransform, Bounds desiredWorldBounds)
        {
            Vector3 scale = fortTransform != null ? fortTransform.lossyScale : Vector3.one;
            scale.x = Mathf.Abs(scale.x) < 0.001f ? 1f : Mathf.Abs(scale.x);
            scale.y = Mathf.Abs(scale.y) < 0.001f ? 1f : Mathf.Abs(scale.y);
            scale.z = Mathf.Abs(scale.z) < 0.001f ? 1f : Mathf.Abs(scale.z);

            return new Bounds(
                new Vector3(
                    desiredWorldBounds.center.x / scale.x,
                    desiredWorldBounds.center.y / scale.y,
                    desiredWorldBounds.center.z / scale.z),
                new Vector3(
                    desiredWorldBounds.size.x / scale.x,
                    desiredWorldBounds.size.y / scale.y,
                    desiredWorldBounds.size.z / scale.z));
        }

        private string GetModeFFortificationColliderDebugText(GameObject fortObj)
        {
            if (fortObj == null)
            {
                return string.Empty;
            }

            Vector3 scale = fortObj.transform.lossyScale;
            BoxCollider damageCollider = null;
            BoxCollider wallCollider = null;
            BoxCollider triggerCollider = null;

            Transform damageTransform = fortObj.transform.Find("ModeF_DamageReceiver");
            if (damageTransform != null)
            {
                damageCollider = damageTransform.GetComponent<BoxCollider>();
            }

            // Wall collider 现在同时承担挡子弹+挡角色职责，CharacterBlocker 已合并。
            Transform wallTransform = fortObj.transform.Find("ModeF_WallCollider");
            if (wallTransform != null)
            {
                wallCollider = wallTransform.GetComponent<BoxCollider>();
            }

            Transform triggerTransform = fortObj.transform.Find("ModeF_HalfObstacleTrigger");
            if (triggerTransform != null)
            {
                triggerCollider = triggerTransform.GetComponent<BoxCollider>();
            }

            return " | scale=" + scale
                + " | damage=" + GetModeFFortificationBoxColliderWorldBoundsText(damageCollider)
                + " | wall=" + GetModeFFortificationBoxColliderWorldBoundsText(wallCollider)
                + " | halfTrigger=" + GetModeFFortificationBoxColliderWorldBoundsText(triggerCollider);
        }

        private string GetModeFFortificationBoxColliderWorldBoundsText(BoxCollider collider)
        {
            if (collider == null)
            {
                return "none";
            }

            Vector3 scale = collider.transform.lossyScale;
            scale.x = Mathf.Abs(scale.x);
            scale.y = Mathf.Abs(scale.y);
            scale.z = Mathf.Abs(scale.z);

            Vector3 worldCenter = Vector3.Scale(collider.center, scale);
            Vector3 worldSize = Vector3.Scale(collider.size, scale);
            return "center=" + worldCenter + ", size=" + worldSize;
        }


    }
}
