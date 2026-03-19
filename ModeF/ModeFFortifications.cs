using System;
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
        #region Mode F Fortifications

        private const float FORT_HP_FOLDABLE_COVER = 100f;
        private const float FORT_HP_REINFORCED_ROADBLOCK = 500f;
        private const float FORT_HP_BARBED_WIRE = 200f;

        /// <summary>遍历 ActiveFortifications 时的防御性快照缓存，避免遍历期间字典被修改</summary>
        private readonly List<ModeFFortificationMarker> modeFActiveFortificationSnapshot = new List<ModeFFortificationMarker>();
        private const float FORT_REPAIR_RANGE = 3f;
        private const float FORT_REPAIR_PERCENT = 0.25f;
        private const float FORT_HIGHLIGHT_DURATION = 1.25f;
        private const float FORT_PLACEMENT_PADDING = 0.35f;
        private const float FORT_WORLD_COLLISION_PADDING = 0.05f;
        private const int FORT_PLACEMENT_OVERLAP_BUFFER_SIZE = 24;

        private const string FORT_PREFAB_FOLDABLE_COVER = "BossRush_ModeF_FoldableCover_Model";
        private const string FORT_PREFAB_REINFORCED_ROADBLOCK = "BossRush_ModeF_ReinforcedRoadblock_Model";
        private const string FORT_PREFAB_BARBED_WIRE = "BossRush_ModeF_BarbedWire_Model";

        private static readonly FieldInfo healthDefaultMaxHealthField =
            typeof(Health).GetField("defaultMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo damageReceiverHealthField =
            typeof(DamageReceiver).GetField("health", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo damageReceiverOnlyExplosionField =
            typeof(DamageReceiver).GetField("onlyReceiveExplosion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo damageReceiverHealthProperty =
            typeof(DamageReceiver).GetProperty("health", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo damageReceiverOnlyExplosionProperty =
            typeof(DamageReceiver).GetProperty("onlyReceiveExplosion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Collider[] modeFFortificationPlacementOverlapBuffer =
            new Collider[FORT_PLACEMENT_OVERLAP_BUFFER_SIZE];

        private static int cachedModeFFortificationPlacementObstacleLayerMask = int.MinValue;

        // Health 反射字段缓存（修复其他mod Harmony patch导致的无敌问题）
        private static readonly FieldInfo healthHasCharacterField =
            typeof(Health).GetField("hasCharacter", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo healthCharacterCachedField =
            typeof(Health).GetField("characterCached", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo healthItemField =
            typeof(Health).GetField("item", BindingFlags.NonPublic | BindingFlags.Instance);

        // 预览放置系统字段
        private bool modeFPlacementActive = false;
        private FortificationType modeFPlacementType;
        private GameObject modeFPlacementPreview;
        private float modeFPlacementRotationY;
        private int modeFPlacementItemTypeId;
        private Material modeFPlacementPreviewMaterial;

        private Material modeFFortificationOutlineMaterial;
        private bool modeFHasActiveFortificationHighlight = false;

        public bool UseModeFFortificationItem(FortificationType type)
        {
            if (!modeFActive)
            {
                ShowMessage(L10n.T("该物品只能在 Mode F 中使用", "This item can only be used in Mode F"));
                return false;
            }

            if (modeFPlacementActive)
            {
                ShowMessage(L10n.T("已有工事正在部署中", "A fortification is already being placed"));
                return false;
            }

            return EnterFortPlacementMode(type);
        }

        public bool UseModeFRepairSpray()
        {
            if (!modeFActive)
            {
                ShowMessage(L10n.T("该物品只能在 Mode F 中使用", "This item can only be used in Mode F"));
                return false;
            }

            return TryRepairNearestFortification();
        }

        private int GetFortificationTypeItemTypeId(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover: return FoldableCoverPackConfig.TYPE_ID;
                case FortificationType.ReinforcedRoadblock: return ReinforcedRoadblockPackConfig.TYPE_ID;
                case FortificationType.BarbedWire: return BarbedWirePackConfig.TYPE_ID;
                default: return 0;
            }
        }

        private bool EnterFortPlacementMode(FortificationType type)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return false;

                Vector3 forward = player.transform.forward;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                Vector3 startPos = player.transform.position + forward.normalized * 2f;

                string prefabName;
                switch (type)
                {
                    case FortificationType.FoldableCover:
                        prefabName = FORT_PREFAB_FOLDABLE_COVER;
                        break;
                    case FortificationType.ReinforcedRoadblock:
                        prefabName = FORT_PREFAB_REINFORCED_ROADBLOCK;
                        break;
                    case FortificationType.BarbedWire:
                        prefabName = FORT_PREFAB_BARBED_WIRE;
                        break;
                    default:
                        return false;
                }

                GameObject preview = EntityModelFactory.Create(prefabName, startPos, Quaternion.LookRotation(forward));
                if (preview == null)
                {
                    preview = CreateFallbackModeFFortification(type, startPos, Quaternion.LookRotation(forward));
                }
                if (preview == null) return false;

                preview.name = "ModeF_FortPlacement_Preview";

                // 禁用所有 Collider 和 Rigidbody
                Collider[] cols = preview.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null) cols[i].enabled = false;
                }
                Rigidbody[] rbs = preview.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < rbs.Length; i++)
                {
                    if (rbs[i] != null) UnityEngine.Object.Destroy(rbs[i]);
                }

                // 应用半透明材质
                ApplyFortPlacementPreviewMaterial(preview, true);

                if (!preview.activeSelf) preview.SetActive(true);

                modeFPlacementActive = true;
                modeFPlacementType = type;
                modeFPlacementPreview = preview;
                modeFPlacementRotationY = player.transform.eulerAngles.y;
                modeFPlacementItemTypeId = GetFortificationTypeItemTypeId(type);

                ShowMessage(L10n.T(
                    "工事部署模式：左键确认 | 右键取消 | 滚轮旋转",
                    "Fortification placement: LMB confirm | RMB cancel | Scroll to rotate"));
                DevLog("[ModeF] 进入工事放置模式: " + type);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] EnterFortPlacementMode failed: " + e.Message);
                // 只清理预览对象，不退还物品（由 OnUse fallback 处理退还）
                try { if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;
                return false;
            }
        }

        internal void UpdateFortPlacementMode()
        {
            if (!modeFPlacementActive || modeFPlacementPreview == null) return;

            try
            {
                // 获取相机
                Camera cam = null;
                try
                {
                    if (GameCamera.Instance != null)
                    {
                        cam = GameCamera.Instance.renderCamera;
                    }
                }
                catch { }
                if (cam == null) cam = Camera.main;
                if (cam == null) return;

                // 滚轮旋转（15°步进）
                float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                {
                    if (scroll > 0f)
                        modeFPlacementRotationY += 15f;
                    else
                        modeFPlacementRotationY -= 15f;
                    if (modeFPlacementRotationY >= 360f) modeFPlacementRotationY -= 360f;
                    if (modeFPlacementRotationY < 0f) modeFPlacementRotationY += 360f;
                }

                // 中键旋转90°
                if (UnityEngine.Input.GetMouseButtonDown(2))
                {
                    modeFPlacementRotationY += 90f;
                    if (modeFPlacementRotationY >= 360f) modeFPlacementRotationY -= 360f;
                }

                // 鼠标射线检测地面
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask |
                                       Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                RaycastHit hit;
                bool hitSomething = Physics.Raycast(ray, out hit, 500f, groundMask, QueryTriggerInteraction.Ignore);
                if (!hitSomething)
                {
                    hitSomething = Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore);
                }

                if (hitSomething)
                {
                    modeFPlacementPreview.transform.position = hit.point;
                    modeFPlacementPreview.transform.rotation = Quaternion.Euler(0f, modeFPlacementRotationY, 0f);

                    // 颜色反馈：检查是否可放置
                    CharacterMainControl player = CharacterMainControl.Main;
                    string unused;
                    bool canPlace = player != null && CanPlaceModeFFortificationAtPreview(player, out unused);
                    ApplyFortPlacementPreviewMaterial(modeFPlacementPreview, canPlace);
                }

                // 左键确认
                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    ConfirmFortPlacement();
                }

                // 右键取消
                if (UnityEngine.Input.GetMouseButtonDown(1))
                {
                    CancelFortPlacement();
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] UpdateFortPlacementMode failed: " + e.Message);
            }
        }

        private bool CanPlaceModeFFortificationAtPreview(CharacterMainControl player, out string failureMessage)
        {
            failureMessage = null;
            if (modeFPlacementPreview == null) return false;

            // 复用或创建一次性 BoxCollider 用于检测（缓存在预览对象上避免每帧创建）
            BoxCollider previewCol = modeFPlacementPreview.GetComponent<BoxCollider>();
            if (previewCol == null)
            {
                Bounds defaultBounds = GetDefaultModeFFortificationBounds(modeFPlacementType);
                previewCol = modeFPlacementPreview.AddComponent<BoxCollider>();
                previewCol.center = defaultBounds.center;
                previewCol.size = defaultBounds.size;
                previewCol.enabled = false;
            }

            return CanPlaceModeFFortification(player, modeFPlacementPreview.transform, previewCol, modeFPlacementType, out failureMessage);
        }

        private void ConfirmFortPlacement()
        {
            if (!modeFPlacementActive || modeFPlacementPreview == null)
            {
                CancelFortPlacement();
                return;
            }

            int itemTypeId = modeFPlacementItemTypeId;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                string failureMessage = null;
                if (player == null || !CanPlaceModeFFortificationAtPreview(player, out failureMessage))
                {
                    if (!string.IsNullOrEmpty(failureMessage))
                    {
                        ShowModeFRewardBubble(failureMessage);
                    }
                    else
                    {
                        ShowModeFRewardBubble(L10n.T("无法在此处部署", "Cannot deploy here"));
                    }
                    return;
                }

                Vector3 position = modeFPlacementPreview.transform.position;
                Quaternion rotation = modeFPlacementPreview.transform.rotation;
                FortificationType type = modeFPlacementType;

                // 销毁预览，退出放置模式
                try { UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;

                // 放置实际工事
                bool placed = PlaceModeFortification(type, position, rotation);
                if (!placed)
                {
                    RefundModeFUtilityItem(itemTypeId,
                        L10n.T("部署失败", "Deployment failed"));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ConfirmFortPlacement failed: " + e.Message);
                // 清理残留状态
                try { if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;
                // 退还物品（使用进入时缓存的 itemTypeId）
                if (itemTypeId > 0)
                {
                    RefundModeFUtilityItem(itemTypeId,
                        L10n.T("部署失败", "Deployment failed"));
                }
            }
        }

        internal void CancelFortPlacement()
        {
            if (!modeFPlacementActive && modeFPlacementPreview == null) return;

            int itemTypeId = modeFPlacementItemTypeId;
            bool wasActive = modeFPlacementActive;

            try { if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
            modeFPlacementPreview = null;
            modeFPlacementActive = false;
            modeFPlacementItemTypeId = 0;

            if (wasActive && itemTypeId > 0)
            {
                RefundModeFUtilityItem(itemTypeId,
                    L10n.T("已取消部署", "Deployment cancelled"));
            }
        }

        private void ApplyFortPlacementPreviewMaterial(GameObject preview, bool canPlace)
        {
            if (preview == null) return;

            if (modeFPlacementPreviewMaterial == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                modeFPlacementPreviewMaterial = new Material(shader);
                if (modeFPlacementPreviewMaterial.HasProperty("_Mode"))
                {
                    modeFPlacementPreviewMaterial.SetFloat("_Mode", 3f); // Transparent
                    modeFPlacementPreviewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    modeFPlacementPreviewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    modeFPlacementPreviewMaterial.SetInt("_ZWrite", 0);
                    modeFPlacementPreviewMaterial.DisableKeyword("_ALPHATEST_ON");
                    modeFPlacementPreviewMaterial.EnableKeyword("_ALPHABLEND_ON");
                    modeFPlacementPreviewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    modeFPlacementPreviewMaterial.renderQueue = 3000;
                }
            }

            Color color = canPlace ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);
            if (modeFPlacementPreviewMaterial.HasProperty("_Color"))
            {
                modeFPlacementPreviewMaterial.SetColor("_Color", color);
            }

            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterial = modeFPlacementPreviewMaterial;
                }
            }
        }

        private bool PlaceModeFortification(FortificationType type, Vector3 position, Quaternion rotation)
        {
            GameObject fortObj = null;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    return false;
                }

                string prefabName;
                float maxHealth;
                switch (type)
                {
                    case FortificationType.FoldableCover:
                        prefabName = FORT_PREFAB_FOLDABLE_COVER;
                        maxHealth = FORT_HP_FOLDABLE_COVER;
                        break;
                    case FortificationType.ReinforcedRoadblock:
                        prefabName = FORT_PREFAB_REINFORCED_ROADBLOCK;
                        maxHealth = FORT_HP_REINFORCED_ROADBLOCK;
                        break;
                    case FortificationType.BarbedWire:
                        prefabName = FORT_PREFAB_BARBED_WIRE;
                        maxHealth = FORT_HP_BARBED_WIRE;
                        break;
                    default:
                        DevLog("[ModeF] [WARNING] Unknown fortification type: " + type);
                        return false;
                }

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

                if (fortObj.activeSelf)
                {
                    fortObj.SetActive(false);
                }

                fortObj.name = "ModeF_Fort_" + type + "_" + Time.frameCount;

                Health health = fortObj.GetComponent<Health>();
                if (health == null)
                {
                    health = fortObj.AddComponent<Health>();
                }

                EnsureModeFFortificationHealthRuntime(health, maxHealth);
                ConfigureModeFFortificationPhysics(fortObj);
                EnsureModeFFortificationDamageTarget(fortObj, type, health);
                EnsureModeFFortificationWallCollider(fortObj, type);

                if (healthDefaultMaxHealthField != null)
                {
                    try
                    {
                        healthDefaultMaxHealthField.SetValue(health, Mathf.RoundToInt(maxHealth));
                    }
                    catch { }
                }

                try { health.CurrentHealth = maxHealth; } catch { }
                health.showHealthBar = true;

                ModeFFortificationMarker marker = fortObj.GetComponent<ModeFFortificationMarker>();
                if (marker == null)
                {
                    marker = fortObj.AddComponent<ModeFFortificationMarker>();
                }

                marker.Type = type;
                marker.Owner = player;
                marker.OwnerCharacterId = player.GetInstanceID();
                marker.MaxHealth = maxHealth;
                marker.IsDestroyed = false;
                marker.BoundHealth = health;

                if (health.OnDeadEvent != null)
                {
                    health.OnDeadEvent.AddListener((damageInfo) => OnFortificationDestroyed(marker));
                }

                if (!fortObj.activeSelf)
                {
                    fortObj.SetActive(true);
                }

                try { health.RequestHealthBar(); } catch { }

                modeFState.ActiveFortifications[marker.FortificationId] = marker;

                string typeName = GetFortificationTypeName(type);
                DevLog("[ModeF] Fortification placed: " + typeName + " at " + position);
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

            modeFActiveFortificationSnapshot.Clear();
            foreach (var kvp in modeFState.ActiveFortifications)
            {
                modeFActiveFortificationSnapshot.Add(kvp.Value);
            }

            for (int fi = 0; fi < modeFActiveFortificationSnapshot.Count; fi++)
            {
                ModeFFortificationMarker marker = modeFActiveFortificationSnapshot[fi];
                if (marker == null || marker.IsDestroyed || marker.gameObject == null)
                {
                    continue;
                }

                float otherRadius = GetModeFFortificationFootprintRadius(
                    marker.transform,
                    marker.GetComponent<BoxCollider>(),
                    marker.Type);
                float minDistance = candidateRadius + otherRadius + FORT_PLACEMENT_PADDING;
                if ((marker.transform.position - candidateTransform.position).sqrMagnitude <
                    minDistance * minDistance)
                {
                    failureMessage = L10n.T(
                        "附近已有工事，无法重叠部署",
                        "Another fortification is too close. Deployment cancelled.");
                    modeFActiveFortificationSnapshot.Clear();
                    return false;
                }
            }
            modeFActiveFortificationSnapshot.Clear();

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
                : GetDefaultModeFFortificationBounds(type);
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
                : GetDefaultModeFFortificationBounds(type);
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

            if (hit.GetComponentInParent<ModeFFortificationMarker>() != null)
            {
                return true;
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

        private void EnsureModeFFortificationHealthRuntime(Health health, float maxHealth)
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
            health.team = Teams.all;
            health.healthBarHeight = 1.1f;
            health.CanDieIfNotRaidMap = false;

            // 使用反射读取 defaultMaxHealth，绕过被其他mod Harmony patch 的 MaxHealth 属性
            float currentMaxHealth = 0f;
            if (healthDefaultMaxHealthField != null)
            {
                try { currentMaxHealth = (int)healthDefaultMaxHealthField.GetValue(health); } catch { }
            }
            if (currentMaxHealth <= 0f)
            {
                if (healthDefaultMaxHealthField != null)
                {
                    try { healthDefaultMaxHealthField.SetValue(health, Mathf.RoundToInt(maxHealth)); } catch { }
                }
            }

            if (health.CurrentHealth <= 0f)
            {
                health.CurrentHealth = Mathf.Max(maxHealth, 1f);
            }

            // 修复其他mod Harmony patch导致的无敌问题：
            // 清除 hasCharacter/characterCached/item，确保 MaxHealth getter 走 defaultMaxHealth 分支
            try
            {
                if (healthHasCharacterField != null)
                    healthHasCharacterField.SetValue(health, false);
                if (healthCharacterCachedField != null)
                    healthCharacterCachedField.SetValue(health, null);
                if (healthItemField != null)
                    healthItemField.SetValue(health, null);
            }
            catch { }
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

        private void EnsureModeFFortificationDamageTarget(GameObject fortObj, FortificationType type, Health health)
        {
            if (fortObj == null || health == null)
            {
                return;
            }

            health.team = Teams.all;

            Collider damageCollider = EnsureModeFFortificationDamageCollider(fortObj, type);
            DamageReceiver receiver = fortObj.GetComponent<DamageReceiver>();
            if (receiver == null)
            {
                receiver = fortObj.AddComponent<DamageReceiver>();
            }

            try
            {
                if (damageReceiverHealthField != null)
                {
                    damageReceiverHealthField.SetValue(receiver, health);
                }
                else if (damageReceiverHealthProperty != null && damageReceiverHealthProperty.CanWrite)
                {
                    damageReceiverHealthProperty.SetValue(receiver, health, null);
                }
            }
            catch { }

            try
            {
                if (damageReceiverOnlyExplosionField != null)
                {
                    damageReceiverOnlyExplosionField.SetValue(receiver, false);
                }
                else if (damageReceiverOnlyExplosionProperty != null && damageReceiverOnlyExplosionProperty.CanWrite)
                {
                    damageReceiverOnlyExplosionProperty.SetValue(receiver, false, null);
                }
            }
            catch { }

            int damageReceiverLayer = GetModeFFortificationDamageReceiverLayer();
            if (damageReceiverLayer >= 0)
            {
                fortObj.layer = damageReceiverLayer;
                if (damageCollider != null)
                {
                    damageCollider.gameObject.layer = damageReceiverLayer;
                }
            }
        }

        private void EnsureModeFFortificationWallCollider(GameObject fortObj, FortificationType type)
        {
            if (fortObj == null) return;

            try
            {
                // 获取 damage collider 的尺寸作为参考
                BoxCollider rootCollider = fortObj.GetComponent<BoxCollider>();
                Bounds bounds = rootCollider != null
                    ? new Bounds(rootCollider.center, rootCollider.size)
                    : GetDefaultModeFFortificationBounds(type);

                GameObject wallChild = new GameObject("ModeF_WallCollider");
                wallChild.transform.SetParent(fortObj.transform, false);
                wallChild.layer = LayerMask.NameToLayer("Wall");

                BoxCollider wallCol = wallChild.AddComponent<BoxCollider>();
                wallCol.center = bounds.center;
                wallCol.size = bounds.size;
                wallCol.isTrigger = false;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFFortificationWallCollider failed: " + e.Message);
            }
        }

        private Collider EnsureModeFFortificationDamageCollider(GameObject fortObj, FortificationType type)
        {
            if (fortObj == null)
            {
                return null;
            }

            Collider[] existingColliders = fortObj.GetComponentsInChildren<Collider>(true);
            Bounds localBounds;
            if (!TryGetModeFFortificationLocalBounds(fortObj.transform, existingColliders, out localBounds))
            {
                localBounds = GetDefaultModeFFortificationBounds(type);
            }

            for (int i = 0; i < existingColliders.Length; i++)
            {
                if (existingColliders[i] == null)
                {
                    continue;
                }

                existingColliders[i].enabled = false;
            }

            BoxCollider rootCollider = fortObj.GetComponent<BoxCollider>();
            if (rootCollider == null)
            {
                rootCollider = fortObj.AddComponent<BoxCollider>();
            }

            rootCollider.enabled = true;
            rootCollider.isTrigger = false;
            rootCollider.center = localBounds.center;
            rootCollider.size = new Vector3(
                Mathf.Max(localBounds.size.x, 0.2f),
                Mathf.Max(localBounds.size.y, 0.2f),
                Mathf.Max(localBounds.size.z, 0.2f));
            return rootCollider;
        }

        private bool TryGetModeFFortificationLocalBounds(Transform root, Collider[] existingColliders, out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (root == null)
            {
                return false;
            }

            bool initialized = false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                EncapsulateModeFFortificationWorldBounds(root, renderer.bounds, ref localBounds, ref initialized);
            }

            if (!initialized && existingColliders != null)
            {
                for (int i = 0; i < existingColliders.Length; i++)
                {
                    Collider collider = existingColliders[i];
                    if (collider == null)
                    {
                        continue;
                    }

                    EncapsulateModeFFortificationWorldBounds(root, collider.bounds, ref localBounds, ref initialized);
                }
            }

            return initialized;
        }

        private void EncapsulateModeFFortificationWorldBounds(Transform root, Bounds worldBounds, ref Bounds localBounds, ref bool initialized)
        {
            Vector3 extents = worldBounds.extents;
            Vector3 center = worldBounds.center;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldPoint = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 localPoint = root.InverseTransformPoint(worldPoint);
                        if (!initialized)
                        {
                            localBounds = new Bounds(localPoint, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        private Bounds GetDefaultModeFFortificationBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.3f, 0.35f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(2.6f, 1.35f, 0.75f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.95f, 0.45f));
                default:
                    return new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            }
        }

        private int GetModeFFortificationDamageReceiverLayer()
        {
            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask;
            }
            catch
            {
                return -1;
            }

            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnFortificationDestroyed(ModeFFortificationMarker marker)
        {
            try
            {
                if (marker == null)
                {
                    return;
                }

                marker.IsDestroyed = true;
                modeFState.ActiveFortifications.Remove(marker.FortificationId);

                string typeName = GetFortificationTypeName(marker.Type);
                DevLog("[ModeF] Fortification destroyed: " + typeName);
                if (marker.gameObject != null)
                {
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }
            catch { }
        }

        internal bool CanUseModeFRepairSpray()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            ModeFFortificationMarker nearest = FindNearestOwnedModeFFortification(player, FORT_REPAIR_RANGE, true);
            if (nearest != null)
            {
                HighlightModeFFortification(nearest, true);
                return true;
            }

            return false;
        }

        private ModeFFortificationMarker FindNearestOwnedModeFFortification(CharacterMainControl player, float maxDistance, bool requireDamaged = false)
        {
            try
            {
                if (player == null)
                {
                    return null;
                }

                Vector3 playerPos = player.transform.position;
                int playerId = player.GetInstanceID();
                ModeFFortificationMarker nearest = null;
                float nearestDistSqr = float.MaxValue;
                float maxDistanceSqr = maxDistance * maxDistance;

                modeFActiveFortificationSnapshot.Clear();
                foreach (var kvp in modeFState.ActiveFortifications)
                {
                    modeFActiveFortificationSnapshot.Add(kvp.Value);
                }

                for (int fi = 0; fi < modeFActiveFortificationSnapshot.Count; fi++)
                {
                    ModeFFortificationMarker marker = modeFActiveFortificationSnapshot[fi];
                    if (marker == null || marker.IsDestroyed || marker.BoundHealth == null || marker.BoundHealth.IsDead)
                    {
                        continue;
                    }

                    if (marker.OwnerCharacterId != playerId)
                    {
                        continue;
                    }

                    if (requireDamaged && marker.BoundHealth.CurrentHealth >= marker.MaxHealth - 0.01f)
                    {
                        continue;
                    }

                    float distSqr = (marker.transform.position - playerPos).sqrMagnitude;
                    if (distSqr <= maxDistanceSqr && distSqr < nearestDistSqr)
                    {
                        nearestDistSqr = distSqr;
                        nearest = marker;
                    }
                }
                modeFActiveFortificationSnapshot.Clear();

                return nearest;
            }
            catch
            {
                return null;
            }
        }

        private bool TryRepairNearestFortification()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                ModeFFortificationMarker nearest = FindNearestOwnedModeFFortification(player, FORT_REPAIR_RANGE, true);

                if (nearest == null)
                {
                    ShowModeFRewardBubble(L10n.T("附近没有可维修的工事", "No repairable fortification nearby"));
                    return false;
                }

                HighlightModeFFortification(nearest, true);

                Health health = nearest.BoundHealth;
                float healAmount = nearest.MaxHealth * FORT_REPAIR_PERCENT;
                float newHealth = Mathf.Min(health.CurrentHealth + healAmount, nearest.MaxHealth);
                health.CurrentHealth = newHealth;

                string typeName = GetFortificationTypeName(nearest.Type);
                DevLog("[ModeF] Fortification repaired: " + typeName + " +" + healAmount.ToString("F0") + " HP");
                ShowModeFRewardBubble(typeName + L10n.T("已维修", " repaired"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] TryRepairNearestFortification failed: " + e.Message);
                return false;
            }
        }

        private GameObject CreateFallbackModeFFortification(FortificationType type, Vector3 position, Quaternion rotation)
        {
            try
            {
                GameObject root = new GameObject("ModeF_Fallback_" + type);
                root.transform.position = position;
                root.transform.rotation = rotation;

                switch (type)
                {
                    case FortificationType.FoldableCover:
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 0.2f), new Color(0.28f, 0.33f, 0.38f));
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(-0.65f, 0.15f, 0f), new Vector3(0.12f, 0.3f, 0.12f), new Color(0.18f, 0.18f, 0.18f));
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0.65f, 0.15f, 0f), new Vector3(0.12f, 0.3f, 0.12f), new Color(0.18f, 0.18f, 0.18f));
                        break;
                    case FortificationType.ReinforcedRoadblock:
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 1.1f, 0.55f), new Color(0.36f, 0.27f, 0.18f));
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 1.18f, 0f), new Vector3(1.5f, 0.18f, 0.2f), new Color(0.62f, 0.54f, 0.22f));
                        break;
                    case FortificationType.BarbedWire:
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.1f, 0f), new Vector3(2.2f, 0.12f, 0.12f), new Color(0.25f, 0.25f, 0.25f));
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, -0.08f), new Vector3(2.2f, 0.05f, 0.05f), new Color(0.78f, 0.78f, 0.78f));
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.85f, 0.08f), new Vector3(2.2f, 0.05f, 0.05f), new Color(0.78f, 0.78f, 0.78f));
                        break;
                    default:
                        UnityEngine.Object.Destroy(root);
                        return null;
                }

                return root;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] CreateFallbackModeFFortification failed: " + e.Message);
                return null;
            }
        }

        /// <summary>M2: 缓存 MaterialPropertyBlock，避免每次 CreatePrimitivePart 泄漏 Material</summary>
        private static readonly MaterialPropertyBlock fortMaterialPropertyBlock = new MaterialPropertyBlock();

        private void CreatePrimitivePart(Transform parent, PrimitiveType primitiveType, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                fortMaterialPropertyBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(fortMaterialPropertyBlock);
            }
        }

        private void HighlightModeFFortification(ModeFFortificationMarker marker, bool enabled)
        {
            if (marker == null || marker.gameObject == null)
            {
                return;
            }

            EnsureModeFFortificationHighlight(marker);
            if (enabled)
            {
                marker.HighlightUntilTime = Mathf.Max(marker.HighlightUntilTime, Time.unscaledTime + FORT_HIGHLIGHT_DURATION);
                modeFHasActiveFortificationHighlight = true;
                if (marker.HighlightRoot != null)
                {
                    marker.HighlightRoot.SetActive(true);
                }

                return;
            }

            marker.HighlightUntilTime = 0f;
            if (marker.HighlightRoot != null && marker.HighlightRoot.activeSelf)
            {
                marker.HighlightRoot.SetActive(false);
            }
        }

        private void UpdateModeFFortificationHighlights()
        {
            if (!modeFActive)
            {
                return;
            }

            if (!modeFHasActiveFortificationHighlight)
            {
                return;
            }

            bool hasActiveHighlight = false;
            float now = Time.unscaledTime;
            modeFActiveFortificationSnapshot.Clear();
            foreach (var kvp in modeFState.ActiveFortifications)
            {
                modeFActiveFortificationSnapshot.Add(kvp.Value);
            }

            for (int fi = 0; fi < modeFActiveFortificationSnapshot.Count; fi++)
            {
                ModeFFortificationMarker marker = modeFActiveFortificationSnapshot[fi];
                if (marker == null || marker.HighlightRoot == null)
                {
                    continue;
                }

                bool shouldShow = !marker.IsDestroyed && now < marker.HighlightUntilTime;
                if (marker.HighlightRoot.activeSelf != shouldShow)
                {
                    marker.HighlightRoot.SetActive(shouldShow);
                }

                if (shouldShow)
                {
                    hasActiveHighlight = true;
                }
            }
            modeFActiveFortificationSnapshot.Clear();

            modeFHasActiveFortificationHighlight = hasActiveHighlight;
        }

        private void EnsureModeFFortificationHighlight(ModeFFortificationMarker marker)
        {
            if (marker == null || marker.gameObject == null || marker.HighlightRoot != null)
            {
                return;
            }

            GameObject outlineRoot = new GameObject("ModeF_FortificationHighlight");
            outlineRoot.transform.SetParent(marker.transform, false);
            outlineRoot.SetActive(false);

            Material outlineMaterial = GetModeFFortificationOutlineMaterial();
            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || renderer.transform.IsChildOf(outlineRoot.transform))
                    {
                        continue;
                    }

                    MeshFilter sourceMeshFilter = renderer.GetComponent<MeshFilter>();
                    MeshRenderer sourceMeshRenderer = renderer as MeshRenderer;
                    if (sourceMeshFilter == null || sourceMeshRenderer == null || sourceMeshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    GameObject outlinePart = new GameObject(renderer.gameObject.name + "_Outline");
                    outlinePart.layer = renderer.gameObject.layer;
                    outlinePart.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    outlinePart.transform.localScale = renderer.transform.lossyScale * 1.04f;
                    outlinePart.transform.SetParent(outlineRoot.transform, true);

                    MeshFilter outlineFilter = outlinePart.AddComponent<MeshFilter>();
                    outlineFilter.sharedMesh = sourceMeshFilter.sharedMesh;

                    MeshRenderer outlineRenderer = outlinePart.AddComponent<MeshRenderer>();
                    Material[] outlineMaterials = new Material[sourceMeshRenderer.sharedMaterials.Length];
                    for (int j = 0; j < outlineMaterials.Length; j++)
                    {
                        outlineMaterials[j] = outlineMaterial;
                    }
                    outlineRenderer.sharedMaterials = outlineMaterials;
                }
                catch { }
            }

            marker.HighlightRoot = outlineRoot;
        }

        private Material GetModeFFortificationOutlineMaterial()
        {
            if (modeFFortificationOutlineMaterial != null)
            {
                return modeFFortificationOutlineMaterial;
            }

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            modeFFortificationOutlineMaterial = new Material(shader);
            if (modeFFortificationOutlineMaterial.HasProperty("_Color"))
            {
                modeFFortificationOutlineMaterial.SetColor("_Color", Color.white);
            }
            if (modeFFortificationOutlineMaterial.HasProperty("_BaseColor"))
            {
                modeFFortificationOutlineMaterial.SetColor("_BaseColor", Color.white);
            }

            return modeFFortificationOutlineMaterial;
        }

        private void GrantModeFKillRewards(int killCount)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    return;
                }

                GiveModeFItem(FoldableCoverPackConfig.TYPE_ID, L10n.T("折叠掩体包", "Foldable Cover Pack"));

                if (killCount % 3 == 0)
                {
                    GiveModeFItem(EmergencyRepairSprayConfig.TYPE_ID, L10n.T("应急维修喷剂", "Emergency Repair Spray"));
                }

                if (killCount % 10 == 0)
                {
                    GiveModeFItem(ReinforcedRoadblockPackConfig.TYPE_ID, L10n.T("加固路障包", "Reinforced Roadblock Pack"));
                }

                if (killCount % 20 == 0)
                {
                    GiveModeFItem(BarbedWirePackConfig.TYPE_ID, L10n.T("阻滞铁丝网包", "Barbed Wire Pack"));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] GrantModeFKillRewards failed: " + e.Message);
            }
        }

        private bool TryGiveItemToPlayerOrDrop(int typeId, string displayName, bool showRewardBubble = true)
        {
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    DevLog("[ModeF] [WARNING] 无法实例化物品: typeId=" + typeId);
                    return false;
                }

                bool sent = false;
                try
                {
                    sent = ItemUtilities.SendToPlayerCharacterInventory(item, false);
                }
                catch { }

                if (!sent)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        item.Drop(player.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                        sent = true;
                    }
                }

                if (!sent)
                {
                    try { item.DestroyTree(); } catch { }
                    return false;
                }

                if (sent && showRewardBubble)
                {
                    ShowModeFRewardBubble(displayName + L10n.T("已到账", " received"));
                }
                return sent;
            }
            catch
            {
                try
                {
                    if (item != null)
                    {
                        item.DestroyTree();
                    }
                }
                catch { }
                return false;
            }
        }

        private void GiveModeFItem(int typeId, string displayName)
        {
            TryGiveItemToPlayerOrDrop(typeId, displayName, true);
        }

        private string GetModeFUtilityItemDisplayName(int typeId)
        {
            switch (typeId)
            {
                case FoldableCoverPackConfig.TYPE_ID:
                    return L10n.T("折叠掩体包", "Foldable Cover Pack");
                case ReinforcedRoadblockPackConfig.TYPE_ID:
                    return L10n.T("加固路障包", "Reinforced Roadblock Pack");
                case BarbedWirePackConfig.TYPE_ID:
                    return L10n.T("阻滞铁丝网包", "Barbed Wire Pack");
                case EmergencyRepairSprayConfig.TYPE_ID:
                    return L10n.T("应急维修喷剂", "Emergency Repair Spray");
                default:
                    return null;
            }
        }

        internal void RefundModeFUtilityItem(int typeId, string reason)
        {
            string displayName = GetModeFUtilityItemDisplayName(typeId);
            if (string.IsNullOrEmpty(displayName))
            {
                return;
            }

            bool refunded = TryGiveItemToPlayerOrDrop(typeId, displayName, false);
            if (refunded)
            {
                ShowMessage(reason + L10n.T("，物品已返还。", ", item refunded."));
            }
            else
            {
                ShowMessage(reason + L10n.T("，但返还失败，请查看日志。", ", but refund failed. Check the log."));
            }
        }

        private void CleanupAllModeFortifications()
        {
            try
            {
                // 防御性退还：如果清理时仍在放置模式，退还物品
                int pendingItemTypeId = modeFPlacementItemTypeId;
                bool wasPlacing = modeFPlacementActive;

                // 清理放置预览
                try { if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;

                if (wasPlacing && pendingItemTypeId > 0)
                {
                    try { RefundModeFUtilityItem(pendingItemTypeId, L10n.T("模式结束", "Mode ended")); } catch { }
                }

                foreach (var kvp in modeFState.ActiveFortifications)
                {
                    try
                    {
                        if (kvp.Value != null && kvp.Value.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(kvp.Value.gameObject);
                        }
                    }
                    catch { }
                }

                modeFState.ActiveFortifications.Clear();
                modeFHasActiveFortificationHighlight = false;
                DevLog("[ModeF] All fortifications have been cleaned up.");
            }
            catch { }
        }

        private string GetFortificationTypeName(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return L10n.T("折叠掩体", "Foldable Cover");
                case FortificationType.ReinforcedRoadblock:
                    return L10n.T("加固路障", "Reinforced Roadblock");
                case FortificationType.BarbedWire:
                    return L10n.T("阻滞铁丝网", "Barbed Wire");
                default:
                    return type.ToString();
            }
        }

        #endregion
    }

    public class ModeFItemUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：部署 Mode F 战术物品", "Use: Deploy Mode F tactical utility")
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null)
            {
                return false;
            }

            if (!inst.IsModeFActive)
            {
                NotificationText.Push(L10n.T(
                    "该物品只能在血猎追击模式中使用！",
                    "This item can only be used in Bloodhunt mode!"
                ));
                return false;
            }

            if (item != null && item.TypeID == EmergencyRepairSprayConfig.TYPE_ID && !inst.CanUseModeFRepairSpray())
            {
                NotificationText.Push(L10n.T(
                    "附近没有可维修的己方工事。",
                    "No repairable fortification nearby."
                ));
                return false;
            }

            return true;
        }

        protected override void OnUse(Item item, object user)
        {
            string failureReason = null;
            try
            {
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst == null || item == null)
                {
                    return;
                }

                bool succeeded = true;
                switch (item.TypeID)
                {
                    case FoldableCoverPackConfig.TYPE_ID:
                        failureReason = L10n.T("部署失败", "Deployment failed");
                        succeeded = inst.UseModeFFortificationItem(FortificationType.FoldableCover);
                        break;
                    case ReinforcedRoadblockPackConfig.TYPE_ID:
                        failureReason = L10n.T("部署失败", "Deployment failed");
                        succeeded = inst.UseModeFFortificationItem(FortificationType.ReinforcedRoadblock);
                        break;
                    case BarbedWirePackConfig.TYPE_ID:
                        failureReason = L10n.T("部署失败", "Deployment failed");
                        succeeded = inst.UseModeFFortificationItem(FortificationType.BarbedWire);
                        break;
                    case EmergencyRepairSprayConfig.TYPE_ID:
                        failureReason = L10n.T("维修失败", "Repair failed");
                        succeeded = inst.UseModeFRepairSpray();
                        break;
                    default:
                        failureReason = null;
                        break;
                }

                if (!succeeded && !string.IsNullOrEmpty(failureReason))
                {
                    inst.RefundModeFUtilityItem(item.TypeID, failureReason);
                    failureReason = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] ModeFItemUsage.OnUse failed: " + e.Message);
                try
                {
                    ModBehaviour inst = ModBehaviour.Instance;
                    if (inst != null && item != null && !string.IsNullOrEmpty(failureReason))
                    {
                        inst.RefundModeFUtilityItem(item.TypeID, failureReason);
                    }
                }
                catch { }
            }
        }
    }

    internal static class ModeFItemUsageHelper
    {
        internal static void AttachToItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                UsageUtilities usageUtilities = item.GetComponent<UsageUtilities>();
                if (usageUtilities == null)
                {
                    usageUtilities = item.gameObject.AddComponent<UsageUtilities>();
                }
                SetMaster(usageUtilities, item);

                ModeFItemUsage usage = item.GetComponent<ModeFItemUsage>();
                if (usage == null)
                {
                    usage = item.gameObject.AddComponent<ModeFItemUsage>();
                }
                SetMaster(usage, item);

                if (usageUtilities.behaviors == null)
                {
                    usageUtilities.behaviors = new System.Collections.Generic.List<UsageBehavior>();
                }
                if (!usageUtilities.behaviors.Contains(usage))
                {
                    usageUtilities.behaviors.Add(usage);
                }

                SetItemUsageUtilities(item, usageUtilities);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] AttachToItem failed: " + e.Message);
            }
        }

        private static void SetMaster(Component component, Item item)
        {
            if (component == null || item == null)
            {
                return;
            }

            Type currentType = component.GetType();
            while (currentType != null)
            {
                FieldInfo masterField = currentType.GetField("master", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (masterField != null)
                {
                    masterField.SetValue(component, item);
                    return;
                }

                currentType = currentType.BaseType;
            }
        }

        private static void SetItemUsageUtilities(Item item, UsageUtilities usageUtilities)
        {
            try
            {
                FieldInfo field = typeof(Item).GetField("usageUtilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(item, usageUtilities);
                }
            }
            catch { }
        }
    }
}
