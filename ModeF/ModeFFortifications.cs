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
        #region Mode F Fortifications

        private const float FORT_HP_FOLDABLE_COVER = 2500f;
        private const float FORT_HP_REINFORCED_ROADBLOCK = 5000f;
        private const float FORT_HP_BARBED_WIRE = 2000f;

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

        // 预览放置系统字段
        private bool modeFPlacementActive = false;
        private FortificationType modeFPlacementType;
        private GameObject modeFPlacementPreview;
        private float modeFPlacementRotationY;
        private int modeFPlacementItemTypeId;
        private Material modeFPlacementPreviewMaterial;
        private readonly List<Renderer> modeFPlacementPreviewRendererCache = new List<Renderer>();
        private readonly List<Material[]> modeFPlacementPreviewOriginalMaterials = new List<Material[]>();
        private bool modeFRepairSelectionActive = false;
        private int modeFRepairSelectionItemTypeId;
        private ModeFFortificationMarker modeFRepairSelectionTarget;

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

            if (modeFRepairSelectionActive)
            {
                ShowMessage(L10n.T("请先结束当前维修选择", "Finish the current repair selection first"));
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

            if (modeFPlacementActive)
            {
                ShowMessage(L10n.T("已有工事正在部署中", "A fortification is already being placed"));
                return false;
            }

            if (modeFRepairSelectionActive)
            {
                ShowMessage(L10n.T("已在维修选择模式中", "Repair selection is already active"));
                return false;
            }

            return EnterModeFRepairSelection();
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
                CacheFortPlacementPreviewMaterials(preview);

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
                    "工事部署模式：左键确认 | 右键取消 | 滚轮旋转 | 中键旋转90度",
                    "Fortification placement: LMB confirm | RMB cancel | Scroll rotate | MMB rotate 90 deg"));
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

        internal void UpdateModeFRepairSelection()
        {
            if (!modeFRepairSelectionActive)
            {
                return;
            }

            try
            {
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

                ModeFFortificationMarker nextTarget = FindModeFRepairSelectionTarget(cam, UnityEngine.Input.mousePosition);
                if (modeFRepairSelectionTarget != nextTarget)
                {
                    if (modeFRepairSelectionTarget != null)
                    {
                        HighlightModeFFortification(modeFRepairSelectionTarget, false);
                    }

                    modeFRepairSelectionTarget = nextTarget;
                }

                if (modeFRepairSelectionTarget != null)
                {
                    HighlightModeFFortification(modeFRepairSelectionTarget, true);
                }

                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    ConfirmModeFRepairSelection();
                }

                if (UnityEngine.Input.GetMouseButtonDown(1))
                {
                    CancelModeFRepairSelection(L10n.T("已取消维修", "Repair cancelled"));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] UpdateModeFRepairSelection failed: " + e.Message);
            }
        }

        private bool EnterModeFRepairSelection()
        {
            try
            {
                if (!CanUseModeFRepairSpray(false))
                {
                    ShowModeFRewardBubble(L10n.T("附近没有可维修的工事", "No repairable fortification nearby"));
                    return false;
                }

                modeFRepairSelectionActive = true;
                modeFRepairSelectionItemTypeId = EmergencyRepairSprayConfig.TYPE_ID;
                modeFRepairSelectionTarget = null;
                ShowMessage(L10n.T(
                    "工事维修模式：靠近鼠标的受损工事会高亮 | 左键确认 | 右键取消",
                    "Fortification repair: damaged fortification nearest to cursor is highlighted | LMB confirm | RMB cancel"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] EnterModeFRepairSelection failed: " + e.Message);
                modeFRepairSelectionActive = false;
                modeFRepairSelectionItemTypeId = 0;
                modeFRepairSelectionTarget = null;
                return false;
            }
        }

        private void ConfirmModeFRepairSelection()
        {
            ModeFFortificationMarker target = modeFRepairSelectionTarget;
            if (target == null)
            {
                ShowModeFRewardBubble(L10n.T("附近没有可维修的工事", "No repairable fortification nearby"));
                return;
            }

            try
            {
                if (!TryRepairFortification(target))
                {
                    return;
                }

                CancelModeFRepairSelection(null, false);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ConfirmModeFRepairSelection failed: " + e.Message);
                CancelModeFRepairSelection(L10n.T("维修失败", "Repair failed"), true);
            }
        }

        private void CancelModeFRepairSelection(string reason, bool refundItem = true)
        {
            if (modeFRepairSelectionTarget != null)
            {
                HighlightModeFFortification(modeFRepairSelectionTarget, false);
            }

            int itemTypeId = modeFRepairSelectionItemTypeId;
            bool wasActive = modeFRepairSelectionActive;
            modeFRepairSelectionTarget = null;
            modeFRepairSelectionActive = false;
            modeFRepairSelectionItemTypeId = 0;

            if (refundItem && wasActive && itemTypeId > 0)
            {
                RefundModeFUtilityItem(itemTypeId, string.IsNullOrEmpty(reason) ? L10n.T("已取消维修", "Repair cancelled") : reason);
            }
        }

        private ModeFFortificationMarker FindModeFRepairSelectionTarget(Camera cam, Vector3 mousePos)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || cam == null)
            {
                return null;
            }

            Ray ray = cam.ScreenPointToRay(mousePos);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            {
                ModeFFortificationMarker hoveredMarker = hit.collider != null
                    ? hit.collider.GetComponentInParent<ModeFFortificationMarker>()
                    : null;
                if (IsValidModeFRepairTarget(hoveredMarker, player, FORT_REPAIR_RANGE))
                {
                    return hoveredMarker;
                }
            }

            Vector2 mouseScreen = new Vector2(mousePos.x, mousePos.y);
            ModeFFortificationMarker bestTarget = null;
            float bestDistance = float.MaxValue;
            float maxDistanceSqr = FORT_REPAIR_RANGE * FORT_REPAIR_RANGE;

            modeFActiveFortificationSnapshot.Clear();
            foreach (var kvp in modeFState.ActiveFortifications)
            {
                modeFActiveFortificationSnapshot.Add(kvp.Value);
            }

            for (int fi = 0; fi < modeFActiveFortificationSnapshot.Count; fi++)
            {
                ModeFFortificationMarker marker = modeFActiveFortificationSnapshot[fi];
                if (!IsValidModeFRepairTarget(marker, player, FORT_REPAIR_RANGE))
                {
                    continue;
                }

                if ((marker.transform.position - player.transform.position).sqrMagnitude > maxDistanceSqr)
                {
                    continue;
                }

                Vector3 worldPoint = marker.transform.position + Vector3.up * 0.8f;
                Vector3 screenPoint = cam.WorldToScreenPoint(worldPoint);
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                float distance = Vector2.SqrMagnitude(new Vector2(screenPoint.x, screenPoint.y) - mouseScreen);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = marker;
                }
            }

            modeFActiveFortificationSnapshot.Clear();
            return bestTarget;
        }

        private bool CanPlaceModeFFortificationAtPreview(CharacterMainControl player, out string failureMessage)
        {
            failureMessage = null;
            if (modeFPlacementPreview == null) return false;

            // 复用或创建一次性 BoxCollider 用于检测（缓存在预览对象上避免每帧创建）
            BoxCollider previewCol = modeFPlacementPreview.GetComponent<BoxCollider>();
            Bounds localBounds = GetModeFFortificationLocalBounds(
                modeFPlacementPreview.transform,
                GetDefaultModeFFortificationBounds(modeFPlacementType));
            if (previewCol == null)
            {
                previewCol = modeFPlacementPreview.AddComponent<BoxCollider>();
                previewCol.enabled = false;
            }
            previewCol.center = localBounds.center;
            previewCol.size = localBounds.size;

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
                GameObject previewObject = modeFPlacementPreview;

                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;

                // 直接将当前预览对象转为真实工事，避免重新实例化后只剩影子
                bool placed = PlaceModeFortification(type, position, rotation, previewObject);
                if (!placed)
                {
                    try { if (previewObject != null) UnityEngine.Object.Destroy(previewObject); } catch { }
                    ClearFortPlacementPreviewMaterialCache();
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
                ClearFortPlacementPreviewMaterialCache();
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
            ClearFortPlacementPreviewMaterialCache();

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
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Standard");
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
                Renderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                    Material[] materials = renderer.sharedMaterials;
                    if (materials == null || materials.Length == 0)
                    {
                        renderer.sharedMaterial = modeFPlacementPreviewMaterial;
                        continue;
                    }

                    for (int mi = 0; mi < materials.Length; mi++)
                    {
                        materials[mi] = modeFPlacementPreviewMaterial;
                    }
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private void CacheFortPlacementPreviewMaterials(GameObject preview)
        {
            ClearFortPlacementPreviewMaterialCache();
            if (preview == null)
            {
                return;
            }

            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] sharedMaterials = renderer.sharedMaterials;
                Material[] snapshot = new Material[sharedMaterials.Length];
                Array.Copy(sharedMaterials, snapshot, sharedMaterials.Length);
                modeFPlacementPreviewRendererCache.Add(renderer);
                modeFPlacementPreviewOriginalMaterials.Add(snapshot);
            }
        }

        private void RestoreFortPlacementPreviewMaterials()
        {
            int count = Mathf.Min(modeFPlacementPreviewRendererCache.Count, modeFPlacementPreviewOriginalMaterials.Count);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = modeFPlacementPreviewRendererCache[i];
                Material[] originalMaterials = modeFPlacementPreviewOriginalMaterials[i];
                if (renderer == null || originalMaterials == null)
                {
                    continue;
                }

                renderer.sharedMaterials = originalMaterials;
                renderer.enabled = true;
            }
        }

        private void ClearFortPlacementPreviewMaterialCache()
        {
            modeFPlacementPreviewRendererCache.Clear();
            modeFPlacementPreviewOriginalMaterials.Clear();
        }

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
                EnsureModeFFortificationWallCollider(fortObj, type);
                EnsureModeFFortificationCharacterBlocker(fortObj, type);
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

                if (health.OnDeadEvent != null)
                {
                    health.OnDeadEvent.AddListener((damageInfo) => OnFortificationDestroyed(marker));
                }
                if (health.OnHurtEvent != null)
                {
                    health.OnHurtEvent.AddListener((damageInfo) => OnFortificationHurt(marker, damageInfo));
                }

                if (!fortObj.activeSelf)
                {
                    fortObj.SetActive(true);
                }
                EnsureModeFFortificationRenderersVisible(fortObj);
                fortObj.transform.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();

                modeFState.ActiveFortifications[marker.FortificationId] = marker;

                string typeName = GetFortificationTypeName(type);
                Debug.Log("[ModeF] [PLACE] " + typeName
                    + " | reusedPreview=" + reusedPreview
                    + " | pos=" + position
                    + " | renderers=" + fortObj.GetComponentsInChildren<Renderer>(true).Length
                    + " | hp=" + marker.LastKnownHealth + "/" + marker.MaxHealth);
                if (IsModeFFortificationHalfObstacle(type))
                {
                    Debug.Log("[ModeF] [HALF] " + typeName + GetModeFFortificationColliderDebugText(fortObj));
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

        private Teams GetModeFFortificationOwnerTeam(CharacterMainControl owner)
        {
            // 工事归属仍由 OwnerCharacterId 追踪；受击 team 使用 middle，
            // 这样玩家和敌人的子弹都能正常命中工事，同时 AI 不会把工事当成主动敌对目标。
            return Teams.middle;
        }

        private bool IsModeFFortificationHalfObstacle(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                case FortificationType.ReinforcedRoadblock:
                case FortificationType.BarbedWire:
                    return true;
                default:
                    return false;
            }
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

            receiver.useSimpleHealth = false;
            receiver.isHalfObsticle = IsModeFFortificationHalfObstacle(type);
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
                damageCollider.gameObject.layer = damageReceiverLayer;
            }
        }

        private void EnsureModeFFortificationWallCollider(GameObject fortObj, FortificationType type)
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

                int wallLayer = GetModeFFortificationWallLayer();
                if (IsModeFFortificationHalfObstacle(type))
                {
                    int halfObstacleLayer = GetModeFFortificationHalfObstacleLayer();
                    if (halfObstacleLayer >= 0)
                    {
                        wallLayer = halfObstacleLayer;
                    }
                }
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

                Bounds bounds = GetModeFFortificationLocalBounds(
                    fortObj.transform,
                    GetModeFFortificationWallColliderBounds(type));
                wallCollider.enabled = true;
                wallCollider.isTrigger = false;
                wallCollider.center = bounds.center;
                wallCollider.size = bounds.size;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFFortificationWallCollider failed: " + e.Message);
            }
        }

        private void EnsureModeFFortificationCharacterBlocker(GameObject fortObj, FortificationType type)
        {
            if (fortObj == null)
            {
                return;
            }

            try
            {
                Transform blockerTransform = fortObj.transform.Find("ModeF_CharacterBlocker");
                GameObject blockerObj = blockerTransform != null
                    ? blockerTransform.gameObject
                    : new GameObject("ModeF_CharacterBlocker");
                blockerObj.transform.SetParent(fortObj.transform, false);
                blockerObj.transform.localPosition = Vector3.zero;
                blockerObj.transform.localRotation = Quaternion.identity;
                blockerObj.transform.localScale = Vector3.one;

                int wallLayer = GetModeFFortificationWallLayer();
                if (wallLayer >= 0)
                {
                    blockerObj.layer = wallLayer;
                }

                Collider[] childColliders = blockerObj.GetComponents<Collider>();
                BoxCollider blockerCollider = null;
                for (int i = 0; i < childColliders.Length; i++)
                {
                    BoxCollider existingBox = childColliders[i] as BoxCollider;
                    if (existingBox != null && blockerCollider == null)
                    {
                        blockerCollider = existingBox;
                        continue;
                    }

                    childColliders[i].enabled = false;
                }

                if (blockerCollider == null)
                {
                    blockerCollider = blockerObj.AddComponent<BoxCollider>();
                }

                Bounds bounds = GetModeFFortificationLocalBounds(
                    fortObj.transform,
                    GetModeFFortificationCharacterBlockerBounds(type));
                blockerCollider.enabled = true;
                blockerCollider.isTrigger = true;
                blockerCollider.center = bounds.center;
                blockerCollider.size = bounds.size;

                ModeFFortificationCharacterBlocker blocker = blockerObj.GetComponent<ModeFFortificationCharacterBlocker>();
                if (blocker == null)
                {
                    blocker = blockerObj.AddComponent<ModeFFortificationCharacterBlocker>();
                }

                blocker.Bind(blockerCollider);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFFortificationCharacterBlocker failed: " + e.Message);
            }
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

            int damageReceiverLayer = GetModeFFortificationDamageReceiverLayer();
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
                GetModeFFortificationDamageColliderBounds(type));
            damageCollider.enabled = true;
            damageCollider.isTrigger = false;
            damageCollider.center = localBounds.center;
            damageCollider.size = localBounds.size;
            return damageCollider;
        }

        private Bounds GetModeFFortificationDamageColliderBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.54f, 0f), new Vector3(1.66f, 1.08f, 0.30f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 0.64f, 0f), new Vector3(2.48f, 1.28f, 0.64f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.42f, 0f), new Vector3(2.24f, 0.90f, 0.30f));
                default:
                    return GetDefaultModeFFortificationBounds(type);
            }
        }

        private Bounds GetModeFFortificationWallColliderBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(1.56f, 0.90f, 0.16f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 0.62f, 0f), new Vector3(2.32f, 1.18f, 0.50f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.40f, 0f), new Vector3(2.14f, 0.78f, 0.18f));
                default:
                    return GetModeFFortificationDamageColliderBounds(type);
            }
        }

        private Bounds GetModeFFortificationCharacterBlockerBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.52f, 0f), new Vector3(1.70f, 1.12f, 0.30f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 0.66f, 0f), new Vector3(2.50f, 1.30f, 0.70f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.44f, 0f), new Vector3(2.20f, 0.92f, 0.32f));
                default:
                    return GetModeFFortificationWallColliderBounds(type);
            }
        }

        private Bounds GetDefaultModeFFortificationBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.54f, 0f), new Vector3(1.76f, 1.08f, 0.35f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(2.6f, 1.35f, 0.75f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.95f, 0.45f));
                default:
                    return new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            }
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

            if (!IsModeFFortificationHalfObstacle(type))
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
                GetModeFFortificationHalfObstacleTriggerBounds(type));
            triggerCollider.enabled = true;
            triggerCollider.isTrigger = true;
            triggerCollider.center = triggerBounds.center;
            triggerCollider.size = triggerBounds.size;

            if (triggerComponent == null)
            {
                triggerComponent = triggerObj.AddComponent<ModeFHalfObstacleTrigger>();
            }

            triggerComponent.SetRegisteredParts(damagePart);
        }

        private Bounds GetModeFFortificationHalfObstacleTriggerBounds(FortificationType type)
        {
            switch (type)
            {
                case FortificationType.FoldableCover:
                    return new Bounds(new Vector3(0f, 0.95f, -0.82f), new Vector3(2.05f, 1.90f, 1.25f));
                case FortificationType.ReinforcedRoadblock:
                    return new Bounds(new Vector3(0f, 1.02f, -0.96f), new Vector3(2.95f, 2.05f, 1.45f));
                case FortificationType.BarbedWire:
                    return new Bounds(new Vector3(0f, 0.78f, -0.76f), new Vector3(2.70f, 1.60f, 1.20f));
                default:
                    return new Bounds(Vector3.zero, Vector3.zero);
            }
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
            BoxCollider blockerCollider = null;
            BoxCollider triggerCollider = null;

            Transform damageTransform = fortObj.transform.Find("ModeF_DamageReceiver");
            if (damageTransform != null)
            {
                damageCollider = damageTransform.GetComponent<BoxCollider>();
            }

            Transform wallTransform = fortObj.transform.Find("ModeF_WallCollider");
            if (wallTransform != null)
            {
                wallCollider = wallTransform.GetComponent<BoxCollider>();
            }

            Transform blockerTransform = fortObj.transform.Find("ModeF_CharacterBlocker");
            if (blockerTransform != null)
            {
                blockerCollider = blockerTransform.GetComponent<BoxCollider>();
            }

            Transform triggerTransform = fortObj.transform.Find("ModeF_HalfObstacleTrigger");
            if (triggerTransform != null)
            {
                triggerCollider = triggerTransform.GetComponent<BoxCollider>();
            }

            return " | scale=" + scale
                + " | damage=" + GetModeFFortificationBoxColliderWorldBoundsText(damageCollider)
                + " | wall=" + GetModeFFortificationBoxColliderWorldBoundsText(wallCollider)
                + " | block=" + GetModeFFortificationBoxColliderWorldBoundsText(blockerCollider)
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

        private int GetModeFFortificationWallLayer()
        {
            int namedLayer = LayerMask.NameToLayer("Wall");
            if (namedLayer >= 0)
            {
                return namedLayer;
            }

            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
            }
            catch { }

            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    return i;
                }
            }

            int obstacleLayer = LayerMask.NameToLayer("Obstacle");
            if (obstacleLayer >= 0)
            {
                return obstacleLayer;
            }

            return 0;
        }

        private int GetModeFFortificationHalfObstacleLayer()
        {
            int namedLayer = LayerMask.NameToLayer("HalfObsticle");
            if (namedLayer >= 0)
            {
                return namedLayer;
            }

            int mask = 0;
            try
            {
                mask = Duckov.Utilities.GameplayDataSettings.Layers.halfObsticleLayer;
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

        private int GetModeFFortificationDamageReceiverLayer()
        {
            int namedLayer = LayerMask.NameToLayer("DamageReceiver");
            if (namedLayer >= 0)
            {
                return namedLayer;
            }

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
                if (marker == null || marker.IsDestroyed)
                {
                    return;
                }

                marker.IsDestroyed = true;
                modeFState.ActiveFortifications.Remove(marker.FortificationId);

                string typeName = GetFortificationTypeName(marker.Type);
                Debug.Log("[ModeF] [DESTROY] " + typeName + " | lastHp=" + marker.LastKnownHealth + "/" + marker.MaxHealth);
                if (marker.gameObject != null)
                {
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }
            catch { }
        }

        private void OnFortificationHurt(ModeFFortificationMarker marker, DamageInfo damageInfo)
        {
            if (marker == null || marker.IsDestroyed || marker.BoundHealth == null)
            {
                return;
            }

            marker.LastKnownHealth = Mathf.Max(0f, marker.BoundHealth.CurrentHealth);
            float finalDamage = damageInfo.finalDamage;
            Debug.Log("[ModeF] [HURT] " + GetFortificationTypeName(marker.Type)
                + " | damage=" + finalDamage.ToString("F1")
                + " | hp=" + marker.LastKnownHealth.ToString("F1") + "/" + marker.MaxHealth.ToString("F1"));
        }

        internal bool CanUseModeFRepairSpray(bool highlightNearest = true)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            ModeFFortificationMarker nearest = FindNearestOwnedModeFFortification(player, FORT_REPAIR_RANGE, true);
            if (nearest != null)
            {
                if (highlightNearest)
                {
                    HighlightModeFFortification(nearest, true);
                }
                return true;
            }

            return false;
        }

        private bool IsValidModeFRepairTarget(ModeFFortificationMarker marker, CharacterMainControl player, float maxDistance)
        {
            if (marker == null || player == null || marker.IsDestroyed || marker.BoundHealth == null || marker.BoundHealth.IsDead)
            {
                return false;
            }

            if (marker.OwnerCharacterId != player.GetInstanceID())
            {
                return false;
            }

            if (marker.BoundHealth.CurrentHealth >= marker.MaxHealth - 0.01f)
            {
                return false;
            }

            return (marker.transform.position - player.transform.position).sqrMagnitude <= maxDistance * maxDistance;
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

        private bool TryRepairFortification(ModeFFortificationMarker target)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (!IsValidModeFRepairTarget(target, player, FORT_REPAIR_RANGE))
                {
                    ShowModeFRewardBubble(L10n.T("附近没有可维修的工事", "No repairable fortification nearby"));
                    return false;
                }

                HighlightModeFFortification(target, true);

                Health health = target.BoundHealth;
                float healAmount = target.MaxHealth * FORT_REPAIR_PERCENT;
                float newHealth = Mathf.Min(health.CurrentHealth + healAmount, target.MaxHealth);
                health.CurrentHealth = newHealth;
                target.LastKnownHealth = newHealth;

                string typeName = GetFortificationTypeName(target.Type);
                DevLog("[ModeF] Fortification repaired: " + typeName + " +" + healAmount.ToString("F0") + " HP");
                ShowModeFRewardBubble(typeName + L10n.T("已维修", " repaired"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] TryRepairFortification failed: " + e.Message);
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
                        CreatePrimitivePart(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.54f, 0f), new Vector3(1.6f, 1.08f, 0.2f), new Color(0.28f, 0.33f, 0.38f));
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
                int pendingRepairItemTypeId = modeFRepairSelectionItemTypeId;
                bool wasRepairSelecting = modeFRepairSelectionActive;

                // 清理放置预览
                try { if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview); } catch { }
                modeFPlacementPreview = null;
                modeFPlacementActive = false;
                modeFPlacementItemTypeId = 0;
                if (modeFRepairSelectionTarget != null)
                {
                    HighlightModeFFortification(modeFRepairSelectionTarget, false);
                }
                modeFRepairSelectionTarget = null;
                modeFRepairSelectionActive = false;
                modeFRepairSelectionItemTypeId = 0;

                if (wasPlacing && pendingItemTypeId > 0)
                {
                    try { RefundModeFUtilityItem(pendingItemTypeId, L10n.T("模式结束", "Mode ended")); } catch { }
                }
                if (wasRepairSelecting && pendingRepairItemTypeId > 0)
                {
                    try { RefundModeFUtilityItem(pendingRepairItemTypeId, L10n.T("模式结束", "Mode ended")); } catch { }
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
                Item item = GetBoundItem();
                return new DisplaySettingsData
                {
                    display = true,
                    description = GetUsageDescription(item)
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

        private Item GetBoundItem()
        {
            Item item = GetComponent<Item>();
            if (item != null)
            {
                return item;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type currentType = GetType();
            while (currentType != null)
            {
                FieldInfo masterField = currentType.GetField("master", flags);
                if (masterField != null)
                {
                    return masterField.GetValue(this) as Item;
                }

                currentType = currentType.BaseType;
            }

            return null;
        }

        private static string GetUsageDescription(Item item)
        {
            int typeId = item != null ? item.TypeID : 0;
            switch (typeId)
            {
                case FoldableCoverPackConfig.TYPE_ID:
                    return L10n.T("使用：部署折叠掩体", "Use: Deploy Foldable Cover");
                case ReinforcedRoadblockPackConfig.TYPE_ID:
                    return L10n.T("使用：部署加固路障", "Use: Deploy Reinforced Roadblock");
                case BarbedWirePackConfig.TYPE_ID:
                    return L10n.T("使用：部署阻滞铁丝网", "Use: Deploy Barbed Wire");
                case EmergencyRepairSprayConfig.TYPE_ID:
                    return L10n.T("使用：修复已部署的防御工事", "Use: Repair deployed fortifications");
                default:
                    return L10n.T("使用：部署 Mode F 战术物品", "Use: Deploy Mode F tactical utility");
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

    public class ModeFHalfObstacleTrigger : MonoBehaviour
    {
        private readonly List<GameObject> registeredParts = new List<GameObject>();
        private readonly Dictionary<CharacterMainControl, int> overlapCounts = new Dictionary<CharacterMainControl, int>();

        public void SetRegisteredParts(params GameObject[] parts)
        {
            RemoveRegisteredPartsFromTrackedCharacters();
            registeredParts.Clear();

            if (parts == null)
            {
                return;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                GameObject part = parts[i];
                if (part != null && !registeredParts.Contains(part))
                {
                    registeredParts.Add(part);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            CharacterMainControl character = GetCharacter(other);
            if (!ShouldTrackCharacter(character) || registeredParts.Count <= 0)
            {
                return;
            }

            int overlapCount = 0;
            overlapCounts.TryGetValue(character, out overlapCount);
            overlapCount++;
            overlapCounts[character] = overlapCount;

            if (overlapCount == 1)
            {
                character.AddnearByHalfObsticles(registeredParts);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            CharacterMainControl character = GetCharacter(other);
            if (!ShouldTrackCharacter(character) || registeredParts.Count <= 0)
            {
                return;
            }

            int overlapCount = 0;
            if (!overlapCounts.TryGetValue(character, out overlapCount))
            {
                return;
            }

            overlapCount--;
            if (overlapCount > 0)
            {
                overlapCounts[character] = overlapCount;
                return;
            }

            overlapCounts.Remove(character);
            character.RemoveNearByHalfObsticles(registeredParts);
        }

        private void OnDisable()
        {
            RemoveRegisteredPartsFromTrackedCharacters();
        }

        private void OnDestroy()
        {
            RemoveRegisteredPartsFromTrackedCharacters();
        }

        private CharacterMainControl GetCharacter(Collider other)
        {
            if (other == null)
            {
                return null;
            }

            CharacterMainControl character = other.GetComponent<CharacterMainControl>();
            if (character != null)
            {
                return character;
            }

            return other.GetComponentInParent<CharacterMainControl>();
        }

        private static bool ShouldTrackCharacter(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                return character.IsMainCharacter || character == CharacterMainControl.Main;
            }
            catch
            {
                return false;
            }
        }

        private void RemoveRegisteredPartsFromTrackedCharacters()
        {
            foreach (var kvp in overlapCounts)
            {
                CharacterMainControl character = kvp.Key;
                if (character != null && registeredParts.Count > 0)
                {
                    character.RemoveNearByHalfObsticles(registeredParts);
                }
            }

            overlapCounts.Clear();
        }
    }

    public class ModeFFortificationCharacterBlocker : MonoBehaviour
    {
        private const float PushOutPadding = 0.05f;
        private const float PushOutCooldown = 0.08f;
        private BoxCollider blockerCollider;
        private float nextPushAllowedTime = 0f;

        public void Bind(BoxCollider collider)
        {
            blockerCollider = collider;
        }

        private void Awake()
        {
            if (blockerCollider == null)
            {
                blockerCollider = GetComponent<BoxCollider>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryPushCharacterOut(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryPushCharacterOut(other);
        }

        private void TryPushCharacterOut(Collider other)
        {
            if (blockerCollider == null || other == null)
            {
                return;
            }

            CharacterMainControl character = GetCharacter(other);
            if (!ShouldPushCharacter(character))
            {
                return;
            }

            if (Time.unscaledTime < nextPushAllowedTime)
            {
                return;
            }

            Vector3 direction;
            float distance;
            if (!Physics.ComputePenetration(
                blockerCollider,
                blockerCollider.transform.position,
                blockerCollider.transform.rotation,
                other,
                other.transform.position,
                other.transform.rotation,
                out direction,
                out distance))
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                Vector3 fallback = character.transform.position - blockerCollider.bounds.center;
                fallback.y = 0f;
                direction = fallback.sqrMagnitude > 0.0001f ? fallback.normalized : blockerCollider.transform.forward;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 currentPos = character.transform.position;
            Vector3 targetPos = currentPos + direction * (distance + PushOutPadding);
            targetPos.y = currentPos.y;
            character.SetPosition(targetPos);
            nextPushAllowedTime = Time.unscaledTime + PushOutCooldown;
        }

        private CharacterMainControl GetCharacter(Collider other)
        {
            CharacterMainControl character = other.GetComponent<CharacterMainControl>();
            if (character != null)
            {
                return character;
            }

            return other.GetComponentInParent<CharacterMainControl>();
        }

        private static bool ShouldPushCharacter(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                return character.IsMainCharacter || character == CharacterMainControl.Main;
            }
            catch
            {
                return false;
            }
        }
    }
}
