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
    // -------------------------------------------------------------------------
    // FortDef: 每种工事的所有静态参数，集中于此，消除散落的 switch/case
    // -------------------------------------------------------------------------
    internal sealed class FortDef
    {
        internal readonly FortificationType Type;
        internal readonly string PrefabName;
        internal readonly float MaxHp;
        internal readonly int ItemTypeId;
        internal readonly bool IsHalfObstacle;
        internal readonly Bounds DamageColliderBounds;
        internal readonly Bounds WallColliderBounds;
        internal readonly Bounds CharacterBlockerBounds;
        internal readonly Bounds DefaultBounds;
        internal readonly Bounds HalfObstacleTriggerBounds;

        private FortDef(
            FortificationType type, string prefab, float hp, int itemTypeId, bool halfObstacle,
            Bounds damage, Bounds wall, Bounds blocker, Bounds def, Bounds halfTrigger)
        {
            Type = type; PrefabName = prefab; MaxHp = hp; ItemTypeId = itemTypeId;
            IsHalfObstacle = halfObstacle;
            DamageColliderBounds = damage; WallColliderBounds = wall;
            CharacterBlockerBounds = blocker; DefaultBounds = def;
            HalfObstacleTriggerBounds = halfTrigger;
        }

        private static readonly Dictionary<FortificationType, FortDef> _table =
            new Dictionary<FortificationType, FortDef>
            {
                {
                    FortificationType.FoldableCover,
                    new FortDef(FortificationType.FoldableCover,
                        "BossRush_ModeF_FoldableCover_Model", 250f, FoldableCoverPackConfig.TYPE_ID, true,
                        new Bounds(new Vector3(0f, 0.54f, 0f),     new Vector3(1.66f, 1.08f, 0.30f)),
                        new Bounds(new Vector3(0f, 0.45f, 0f),     new Vector3(1.56f, 0.90f, 0.16f)),
                        new Bounds(new Vector3(0f, 0.52f, 0f),     new Vector3(1.70f, 1.12f, 0.30f)),
                        new Bounds(new Vector3(0f, 0.54f, 0f),     new Vector3(1.76f, 1.08f, 0.35f)),
                        new Bounds(new Vector3(0f, 0.95f, -0.82f), new Vector3(2.05f, 1.90f, 1.25f)))
                },
                {
                    FortificationType.ReinforcedRoadblock,
                    new FortDef(FortificationType.ReinforcedRoadblock,
                        "BossRush_ModeF_ReinforcedRoadblock_Model", 500f, ReinforcedRoadblockPackConfig.TYPE_ID, true,
                        new Bounds(new Vector3(0f, 0.64f, 0f),     new Vector3(2.48f, 1.28f, 0.64f)),
                        new Bounds(new Vector3(0f, 0.62f, 0f),     new Vector3(2.32f, 1.18f, 0.50f)),
                        new Bounds(new Vector3(0f, 0.66f, 0f),     new Vector3(2.50f, 1.30f, 0.70f)),
                        new Bounds(new Vector3(0f, 0.6f,  0f),     new Vector3(2.60f, 1.35f, 0.75f)),
                        new Bounds(new Vector3(0f, 1.02f, -0.96f), new Vector3(2.95f, 2.05f, 1.45f)))
                },
                {
                    FortificationType.BarbedWire,
                    new FortDef(FortificationType.BarbedWire,
                        "BossRush_ModeF_BarbedWire_Model", 200f, BarbedWirePackConfig.TYPE_ID, true,
                        new Bounds(new Vector3(0f, 0.44f, 0f),     new Vector3(2.60f, 0.94f, 0.40f)),
                        new Bounds(new Vector3(0f, 0.42f, 0f),     new Vector3(2.50f, 0.82f, 0.28f)),
                        new Bounds(new Vector3(0f, 0.46f, 0f),     new Vector3(2.56f, 0.96f, 0.42f)),
                        new Bounds(new Vector3(0f, 0.45f, 0f),     new Vector3(2.70f, 0.98f, 0.50f)),
                        new Bounds(new Vector3(0f, 0.78f, -0.76f), new Vector3(2.95f, 1.60f, 1.30f)))
                },
            };

        internal static FortDef Get(FortificationType type)
        {
            FortDef d; return _table.TryGetValue(type, out d) ? d : null;
        }
    }

    // -------------------------------------------------------------------------
    // FortLayers: layer ID 只解析一次，之后直接读属性
    // -------------------------------------------------------------------------
    internal static class FortLayers
    {
        private static int _wall           = int.MinValue;
        private static int _halfObstacle   = int.MinValue;
        private static int _damageReceiver = int.MinValue;

        internal static int Wall
        {
            get
            {
                if (_wall != int.MinValue) return _wall;
                _wall = LayerMask.NameToLayer("Wall");
                if (_wall >= 0) return _wall;
                int mask = 0;
                try { mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask; } catch { }
                _wall = FirstBit(mask);
                if (_wall < 0) _wall = LayerMask.NameToLayer("Obstacle");
                if (_wall < 0) _wall = 0;
                return _wall;
            }
        }

        internal static int HalfObstacle
        {
            get
            {
                if (_halfObstacle != int.MinValue) return _halfObstacle;
                _halfObstacle = LayerMask.NameToLayer("HalfObsticle");
                if (_halfObstacle >= 0) return _halfObstacle;
                int mask = 0;
                try { mask = Duckov.Utilities.GameplayDataSettings.Layers.halfObsticleLayer; } catch { return _halfObstacle = -1; }
                return _halfObstacle = FirstBit(mask);
            }
        }

        internal static int DamageReceiver
        {
            get
            {
                if (_damageReceiver != int.MinValue) return _damageReceiver;
                _damageReceiver = LayerMask.NameToLayer("DamageReceiver");
                if (_damageReceiver >= 0) return _damageReceiver;
                int mask = 0;
                try { mask = Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask; } catch { return _damageReceiver = -1; }
                return _damageReceiver = FirstBit(mask);
            }
        }

        private static int FirstBit(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F Fortifications

        private readonly List<ModeFFortificationMarker> modeFActiveFortificationSnapshot = new List<ModeFFortificationMarker>();
        private const float FORT_REPAIR_RANGE            = 3f;
        private const float FORT_REPAIR_PERCENT          = 0.25f;
        private const float FORT_HIGHLIGHT_DURATION      = 1.25f;
        // ★低端机优化：Highlight 完全关闭后保留 outline 对象的最长时间（秒）。
        // 超时后销毁 outline，避免每个工事永远保留 MeshFilter+MeshRenderer 副本；
        // 下次 Highlight 时按需重建。阈值设置较大以避免维修选择抖动时反复创建销毁。
        private const float FORT_HIGHLIGHT_OUTLINE_DESTROY_DELAY = 10f;
        private const float FORT_DAMAGE_LOG_INTERVAL     = 0.25f;
        private const float FORT_WORLD_COLLISION_PADDING = 0.05f;
        private const int   FORT_PLACEMENT_OVERLAP_BUFFER_SIZE = 24;
        private const int   MODEF_UTILITY_REWARD_UNTHROTTLED_THRESHOLD = 6;
        private const int   MODEF_UTILITY_REWARD_MAX_DELIVERIES_PER_FLUSH = 6;
        private const int   MODEF_UTILITY_REWARD_MAX_DELIVERIES_PER_TYPE_PER_FLUSH = 3;
        // ★低端机防御：活动工事硬上限。超过后拒绝放置新工事，防止玩家无上限部署拖垮物理/渲染。
        private const int   MODEF_MAX_ACTIVE_FORTIFICATIONS = 24;

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
        private bool? modeFPlacementLastCanPlace = null;
        private readonly List<Renderer> modeFPlacementPreviewRendererCache = new List<Renderer>();
        private readonly List<Material[]> modeFPlacementPreviewOriginalMaterials = new List<Material[]>();
        private bool modeFRepairSelectionActive = false;
        private int modeFRepairSelectionItemTypeId;
        private ModeFFortificationMarker modeFRepairSelectionTarget;

        // 工事道具奖励队列
        private readonly Dictionary<int, int> modeFPendingUtilityRewardCounts    = new Dictionary<int, int>();
        private readonly List<int>            modeFPendingUtilityRewardTypeScratch = new List<int>();

        private Material modeFFortificationOutlineMaterial;
        private bool modeFHasActiveFortificationHighlight = false;

        private bool CanUseModeFortificationUtilities()
        {
            return modeFActive || IsZombieModeActive;
        }

        public bool UseModeFFortificationItem(FortificationType type)
        {
            if (!CanUseModeFortificationUtilities())
            {
                ShowMessage(L10n.T(
                    "该物品只能在 Mode F 或丧尸模式中使用",
                    "This item can only be used in Mode F or Zombie Mode"));
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

            // ★低端机防御：活动工事数达到硬上限时拒绝放置，避免低端机被无限部署拖垮。
            if (modeFState != null
                && modeFState.ActiveFortifications != null
                && modeFState.ActiveFortifications.Count >= MODEF_MAX_ACTIVE_FORTIFICATIONS)
            {
                ShowMessage(L10n.T(
                    "工事数量已达上限 (" + MODEF_MAX_ACTIVE_FORTIFICATIONS + ")",
                    "Fortification limit reached (" + MODEF_MAX_ACTIVE_FORTIFICATIONS + ")"));
                return false;
            }

            return EnterFortPlacementMode(type);
        }

        public bool UseModeFRepairSpray()
        {
            if (!CanUseModeFortificationUtilities())
            {
                ShowMessage(L10n.T(
                    "该物品只能在 Mode F 或丧尸模式中使用",
                    "This item can only be used in Mode F or Zombie Mode"));
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
            FortDef def = FortDef.Get(type);
            return def != null ? def.ItemTypeId : 0;
        }

        private bool EnterFortPlacementMode(FortificationType type)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return false;

                FortDef def = FortDef.Get(type);
                if (def == null) return false;

                Vector3 forward = player.transform.forward;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                Vector3 startPos = player.transform.position + forward.normalized * 2f;

                GameObject preview = EntityModelFactory.Create(def.PrefabName, startPos, Quaternion.LookRotation(forward));
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
                // ★低端机优化：用有限层掩码，禁止 ~0 全层 fallback（复杂关卡每帧扫描数万 collider）。
                //   主 mask = ground + wall；fallback mask = 主 mask + damageReceiver + fowBlock（覆盖敌人身体、雾障等）。
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);
                int placementRaycastMask = GetModeFFortificationPlacementRaycastMask();
                int placementRaycastFallbackMask = GetModeFFortificationPlacementRaycastFallbackMask();
                RaycastHit hit;
                bool hitSomething = Physics.Raycast(ray, out hit, 500f, placementRaycastMask, QueryTriggerInteraction.Ignore);
                if (!hitSomething && placementRaycastFallbackMask != placementRaycastMask)
                {
                    hitSomething = Physics.Raycast(ray, out hit, 500f, placementRaycastFallbackMask, QueryTriggerInteraction.Ignore);
                }

                if (hitSomething)
                {
                    modeFPlacementPreview.transform.position = hit.point;
                    modeFPlacementPreview.transform.rotation = Quaternion.Euler(0f, modeFPlacementRotationY, 0f);

                    // 颜色反馈：仅在可放置状态变化时更新材质
                    CharacterMainControl player = CharacterMainControl.Main;
                    string unavailableReason;
                    bool canPlace = player != null && CanPlaceModeFFortificationAtPreview(player, out unavailableReason);
                    if (modeFPlacementLastCanPlace == null || modeFPlacementLastCanPlace.Value != canPlace)
                    {
                        ApplyFortPlacementPreviewMaterial(modeFPlacementPreview, canPlace);
                        modeFPlacementLastCanPlace = canPlace;
                    }
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

            // ★低端机优化：维修选择悬停 Raycast 从 ~0 全层收敛到 damageReceiver + wall
            // （工事的 DamageReceiver 和 Wall/Blocker collider 所在层），其他层没必要检测。
            Ray ray = cam.ScreenPointToRay(mousePos);
            RaycastHit hit;
            int repairHoverMask = GetModeFFortificationRepairHoverMask();
            if (Physics.Raycast(ray, out hit, 500f, repairHoverMask, QueryTriggerInteraction.Ignore))
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
                GetModeFFortificationDefaultBounds(modeFPlacementType));
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
                modeFPlacementLastCanPlace = null;

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
                modeFPlacementLastCanPlace = null;
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
            modeFPlacementLastCanPlace = null;
            ClearFortPlacementPreviewMaterialCache();

            if (wasActive && itemTypeId > 0)
            {
                RefundModeFUtilityItem(itemTypeId,
                    L10n.T("已取消部署", "Deployment cancelled"));
            }
        }

        internal void CleanupZombieModeFortificationInteractionState()
        {
            try
            {
                CancelFortPlacement();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 清理丧尸模式工事放置状态失败: " + e.Message);
            }

            try
            {
                CancelModeFRepairSelection(L10n.T("模式结束", "Mode ended"), true);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 清理丧尸模式工事维修状态失败: " + e.Message);
            }

            ClearModeFFortificationHighlights();
        }

        private void ClearModeFFortificationHighlights()
        {
            try
            {
                modeFActiveFortificationSnapshot.Clear();
                if (modeFState != null && modeFState.ActiveFortifications != null)
                {
                    foreach (var kvp in modeFState.ActiveFortifications)
                    {
                        modeFActiveFortificationSnapshot.Add(kvp.Value);
                    }
                }

                for (int fi = 0; fi < modeFActiveFortificationSnapshot.Count; fi++)
                {
                    ModeFFortificationMarker marker = modeFActiveFortificationSnapshot[fi];
                    if (marker == null)
                    {
                        continue;
                    }

                    marker.HighlightUntilTime = 0f;
                    if (marker.HighlightRoot != null)
                    {
                        try { UnityEngine.Object.Destroy(marker.HighlightRoot); } catch { }
                        marker.HighlightRoot = null;
                    }
                }

                modeFActiveFortificationSnapshot.Clear();
                modeFHasActiveFortificationHighlight = false;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 清理工事高亮失败: " + e.Message);
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

            // 使用进入放置模式时缓存好的 Renderer 列表，避免每帧 GetComponentsInChildren 分配
            int rendererCount = modeFPlacementPreviewRendererCache.Count;
            for (int i = 0; i < rendererCount; i++)
            {
                Renderer renderer = modeFPlacementPreviewRendererCache[i];
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

        #endregion
    }
}
