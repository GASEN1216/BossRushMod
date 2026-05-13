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
                DevLog("[ModeF] [DESTROY] " + typeName + " | lastHp=" + marker.LastKnownHealth + "/" + marker.MaxHealth);
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
            if (!IsDevLoggingEnabled)
            {
                return;
            }

            if (Time.unscaledTime < marker.NextDamageLogTime)
            {
                return;
            }

            marker.NextDamageLogTime = Time.unscaledTime + FORT_DAMAGE_LOG_INTERVAL;
            float finalDamage = damageInfo.finalDamage;
            DevLog("[ModeF] [HURT] " + GetFortificationTypeName(marker.Type)
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
            if (!CanUseModeFortificationUtilities())
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
                else if (marker.HighlightUntilTime > 0f)
                {
                    if (now - marker.HighlightUntilTime > FORT_HIGHLIGHT_OUTLINE_DESTROY_DELAY)
                    {
                        // ★低端机优化：Highlight 关闭超过阈值后销毁 outline 对象，下次再 Highlight 时按需重建。
                        // 避免每个工事永远保留一份 MeshFilter+MeshRenderer 副本，累积 draw call 和 GPU 内存。
                        try { UnityEngine.Object.Destroy(marker.HighlightRoot); }
                        catch (Exception e) { DevLog("[ModeF] [WARNING] Highlight outline 销毁失败: " + e.Message); }
                        marker.HighlightRoot = null;
                        marker.HighlightUntilTime = 0f;
                    }
                    else
                    {
                        // 延迟销毁窗口期内，仍需保持 Update 循环活跃；否则入口的
                        // `if (!modeFHasActiveFortificationHighlight) return;` 会使本函数提前退出，
                        // 导致 outline 永远无法到达销毁分支（低端机优化失效）。
                        hasActiveHighlight = true;
                    }
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

                GiveModeFItem(
                    FoldableCoverPackConfig.TYPE_ID,
                    L10n.T("折叠掩体包", "Foldable Cover Pack"));

                if (killCount % 3 == 0)
                {
                    GiveModeFItem(
                        EmergencyRepairSprayConfig.TYPE_ID,
                        L10n.T("应急维修喷剂", "Emergency Repair Spray"));
                }

                if (killCount % 10 == 0)
                {
                    GiveModeFItem(
                        ReinforcedRoadblockPackConfig.TYPE_ID,
                        L10n.T("加固路障包", "Reinforced Roadblock Pack"));
                }

                if (killCount % 20 == 0)
                {
                    GiveModeFItem(
                        BarbedWirePackConfig.TYPE_ID,
                        L10n.T("阻滞铁丝网包", "Barbed Wire Pack"));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] GrantModeFKillRewards failed: " + e.Message);
            }
        }

        private bool TryGiveItemToPlayerOrDrop(int typeId, string displayName, bool showRewardBubble = true, bool allowWorldDrop = true)
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
                    if (allowWorldDrop)
                    {
                        CharacterMainControl player = CharacterMainControl.Main;
                        if (player != null)
                        {
                            item.Drop(player.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                            sent = true;
                        }
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


        private int GetModeFPendingUtilityRewardCount(int typeId)
        {
            int count;
            modeFPendingUtilityRewardCounts.TryGetValue(typeId, out count);
            return count;
        }

        // 每帧（节流后）尝试把积压奖励发放到玩家背包/丢地上
        private void UpdateModeFPendingUtilityRewards()
        {
            if (!modeFActive || modeFPendingUtilityRewardCounts.Count <= 0)
            {
                return;
            }

            FlushModeFPendingUtilityRewards(false);
        }

        // allowWorldDrop=false 时若背包满则留到下次；true 时强制丢地
        private void FlushModeFPendingUtilityRewards(bool allowWorldDrop)
        {
            if (modeFPendingUtilityRewardCounts.Count <= 0) return;

            modeFPendingUtilityRewardTypeScratch.Clear();
            int totalPendingCount = 0;
            foreach (var kvp in modeFPendingUtilityRewardCounts)
            {
                if (kvp.Value > 0)
                {
                    modeFPendingUtilityRewardTypeScratch.Add(kvp.Key);
                    totalPendingCount += kvp.Value;
                }
            }

            bool throttleDeliveries =
                !allowWorldDrop &&
                totalPendingCount > MODEF_UTILITY_REWARD_UNTHROTTLED_THRESHOLD;
            int remainingBudget = throttleDeliveries
                ? MODEF_UTILITY_REWARD_MAX_DELIVERIES_PER_FLUSH
                : int.MaxValue;

            for (int i = 0; i < modeFPendingUtilityRewardTypeScratch.Count; i++)
            {
                if (remainingBudget <= 0)
                {
                    break;
                }

                int typeId = modeFPendingUtilityRewardTypeScratch[i];
                int remaining = GetModeFPendingUtilityRewardCount(typeId);
                string displayName = GetModeFUtilityItemDisplayName(typeId);
                if (remaining <= 0 || string.IsNullOrEmpty(displayName))
                {
                    modeFPendingUtilityRewardCounts.Remove(typeId);
                    continue;
                }

                int deliveredThisType = 0;
                int perTypeBudget = throttleDeliveries
                    ? MODEF_UTILITY_REWARD_MAX_DELIVERIES_PER_TYPE_PER_FLUSH
                    : int.MaxValue;
                while (remaining > 0 && deliveredThisType < perTypeBudget && remainingBudget > 0)
                {
                    if (!TryGiveItemToPlayerOrDrop(typeId, displayName, true, allowWorldDrop))
                    {
                        break;
                    }
                    remaining--;
                    deliveredThisType++;
                    if (remainingBudget != int.MaxValue)
                    {
                        remainingBudget--;
                    }
                }

                if (remaining > 0)
                    modeFPendingUtilityRewardCounts[typeId] = remaining;
                else
                    modeFPendingUtilityRewardCounts.Remove(typeId);
            }
        }

        // 撤离时同步写入 PlayerStorage（不受背包格限制）
        private int DeliverModeFPendingUtilityRewardsToStorage()
        {
            if (modeFPendingUtilityRewardCounts.Count <= 0) return 0;

            int delivered = 0;
            modeFPendingUtilityRewardTypeScratch.Clear();
            foreach (var kvp in modeFPendingUtilityRewardCounts)
            {
                if (kvp.Value > 0) modeFPendingUtilityRewardTypeScratch.Add(kvp.Key);
            }

            for (int i = 0; i < modeFPendingUtilityRewardTypeScratch.Count; i++)
            {
                int typeId = modeFPendingUtilityRewardTypeScratch[i];
                int remaining = GetModeFPendingUtilityRewardCount(typeId);
                if (remaining <= 0)
                {
                    modeFPendingUtilityRewardCounts.Remove(typeId);
                    continue;
                }

                bool anyBuffered = false;
                while (remaining > 0)
                {
                    Item item = null;
                    bool stored = false;
                    try
                    {
                        item = ItemAssetsCollection.InstantiateSync(typeId);
                        if (item == null) break;
                        try { PlayerStorage.Push(item, true); stored = true; }
                        catch (Exception pushEx)
                        {
                            DevLog("[ModeF] [WARNING] 工事奖励写入寄存失败，尝试缓冲: " + pushEx.Message);
                        }
                        if (!stored)
                        {
                            // PlayerStorage 已满，回退到 PlayerStorageBuffer
                            try
                            {
                                ItemStatsSystem.Data.ItemTreeData bufferedData = ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                                PlayerStorageBuffer.Buffer.Add(bufferedData);
                                item.DestroyTree();
                                delivered++;
                                remaining--;
                                anyBuffered = true;
                                DevLog("[ModeF] [WARNING] PlayerStorage.Push 失败，工事奖励已回退写入寄存缓冲");
                            }
                            catch (Exception bufferEx)
                            {
                                DevLog("[ModeF] [ERROR] 工事奖励写入寄存缓冲失败: " + bufferEx.Message);
                                if (item != null)
                                {
                                    SafeRuntime.Run("ModeF fortification reward destroy after buffer failure", item.DestroyTree);
                                }
                                break;
                            }
                            continue;
                        }
                        delivered++;
                        remaining--;
                    }
                    catch (Exception itemEx)
                    {
                        DevLog("[ModeF] [WARNING] 工事奖励发放异常: " + itemEx.Message);
                        if (item != null)
                        {
                            SafeRuntime.Run("ModeF fortification reward destroy after grant failure", item.DestroyTree);
                        }
                        break;
                    }
                }
                if (anyBuffered)
                {
                    SafeRuntime.Run("ModeF fortification reward buffer save", PlayerStorageBuffer.SaveBuffer);
                }

                if (remaining > 0)
                    modeFPendingUtilityRewardCounts[typeId] = remaining;
                else
                    modeFPendingUtilityRewardCounts.Remove(typeId);
            }

            return delivered;
        }

    }
}
