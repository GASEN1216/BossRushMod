using System;
using System.Reflection;
using UnityEngine;
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
        private const float FORT_REPAIR_RANGE = 3f;
        private const float FORT_REPAIR_PERCENT = 0.25f;

        private const string FORT_PREFAB_FOLDABLE_COVER = "BossRush_ModeF_FoldableCover_Model";
        private const string FORT_PREFAB_REINFORCED_ROADBLOCK = "BossRush_ModeF_ReinforcedRoadblock_Model";
        private const string FORT_PREFAB_BARBED_WIRE = "BossRush_ModeF_BarbedWire_Model";

        private static readonly FieldInfo healthDefaultMaxHealthField =
            typeof(Health).GetField("defaultMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

        private Material modeFFortificationOutlineMaterial;

        public void UseModeFFortificationItem(FortificationType type)
        {
            if (!modeFActive)
            {
                ShowMessage(L10n.T("该物品只能在 Mode F 中使用", "This item can only be used in Mode F"));
                return;
            }

            PlaceModeFortification(type);
        }

        public void UseModeFRepairSpray()
        {
            if (!modeFActive)
            {
                ShowMessage(L10n.T("该物品只能在 Mode F 中使用", "This item can only be used in Mode F"));
                return;
            }

            TryRepairNearestFortification();
        }

        private void PlaceModeFortification(FortificationType type)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    return;
                }

                Vector3 forward = player.transform.forward;
                if (forward.sqrMagnitude < 0.01f)
                {
                    forward = Vector3.forward;
                }

                Vector3 position = player.transform.position + forward.normalized * 2f;

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
                        return;
                }

                GameObject fortObj = EntityModelFactory.Create(prefabName, position, Quaternion.LookRotation(forward));
                if (fortObj == null)
                {
                    DevLog("[ModeF] [WARNING] Failed to create fortification model, falling back to runtime geometry: " + prefabName);
                    fortObj = CreateFallbackModeFFortification(type, position, Quaternion.LookRotation(forward));
                    if (fortObj == null)
                    {
                        return;
                    }
                }

                fortObj.name = "ModeF_Fort_" + type + "_" + Time.frameCount;

                Health health = fortObj.GetComponent<Health>();
                if (health == null)
                {
                    health = fortObj.AddComponent<Health>();
                }

                if (healthDefaultMaxHealthField != null)
                {
                    try
                    {
                        healthDefaultMaxHealthField.SetValue(health, Mathf.RoundToInt(maxHealth));
                    }
                    catch { }
                }

                try { health.SetHealth(maxHealth); } catch { health.CurrentHealth = maxHealth; }
                health.showHealthBar = true;
                health.RequestHealthBar();

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

                health.OnDeadEvent.AddListener((damageInfo) => OnFortificationDestroyed(marker));

                modeFState.ActiveFortifications[marker.FortificationId] = marker;

                string typeName = GetFortificationTypeName(type);
                DevLog("[ModeF] Fortification placed: " + typeName + " at " + position);
                ShowModeFRewardBubble(typeName + L10n.T("已部署", " deployed"));
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] PlaceModeFortification failed: " + e.Message);
            }
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
            return FindNearestOwnedModeFFortification(player, FORT_REPAIR_RANGE) != null;
        }

        private ModeFFortificationMarker FindNearestOwnedModeFFortification(CharacterMainControl player, float maxDistance)
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
                float nearestDist = float.MaxValue;

                foreach (var kvp in modeFState.ActiveFortifications)
                {
                    ModeFFortificationMarker marker = kvp.Value;
                    if (marker == null || marker.IsDestroyed || marker.BoundHealth == null || marker.BoundHealth.IsDead)
                    {
                        continue;
                    }

                    if (marker.OwnerCharacterId != playerId)
                    {
                        continue;
                    }

                    float dist = Vector3.Distance(playerPos, marker.transform.position);
                    if (dist <= maxDistance && dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = marker;
                    }
                }

                return nearest;
            }
            catch
            {
                return null;
            }
        }

        private void TryRepairNearestFortification()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                ModeFFortificationMarker nearest = FindNearestOwnedModeFFortification(player, FORT_REPAIR_RANGE);

                if (nearest == null)
                {
                    ShowModeFRewardBubble(L10n.T("附近没有可维修的工事", "No repairable fortification nearby"));
                    return;
                }

                HighlightModeFFortification(nearest, true);

                Health health = nearest.BoundHealth;
                float healAmount = nearest.MaxHealth * FORT_REPAIR_PERCENT;
                try
                {
                    health.SetHealth(Mathf.Min(health.CurrentHealth + healAmount, nearest.MaxHealth));
                }
                catch
                {
                    health.CurrentHealth = Mathf.Min(health.CurrentHealth + healAmount, nearest.MaxHealth);
                }

                string typeName = GetFortificationTypeName(nearest.Type);
                DevLog("[ModeF] Fortification repaired: " + typeName + " +" + healAmount.ToString("F0") + " HP");
                ShowModeFRewardBubble(typeName + L10n.T("已维修", " repaired"));
                HighlightModeFFortification(nearest, false);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] TryRepairNearestFortification failed: " + e.Message);
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

        private void CreatePrimitivePart(Transform parent, PrimitiveType primitiveType, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null && renderer.material.HasProperty("_Color"))
            {
                renderer.material.color = color;
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
                marker.HighlightUntilTime = Mathf.Max(marker.HighlightUntilTime, Time.unscaledTime + 0.75f);
                if (marker.HighlightRoot != null)
                {
                    marker.HighlightRoot.SetActive(true);
                }
            }
            return;

            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    Material[] materials = renderers[i].materials;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        if (materials[j] != null && materials[j].HasProperty("_Color"))
                        {
                            materials[j].SetColor("_Color", enabled ? Color.white : Color.gray);
                        }
                    }
                }
                catch { }
            }
        }

        private void UpdateModeFFortificationHighlights()
        {
            if (!modeFActive)
            {
                return;
            }

            foreach (var kvp in modeFState.ActiveFortifications)
            {
                ModeFFortificationMarker marker = kvp.Value;
                if (marker == null || marker.HighlightRoot == null)
                {
                    continue;
                }

                bool shouldShow = !marker.IsDestroyed && Time.unscaledTime < marker.HighlightUntilTime;
                if (marker.HighlightRoot.activeSelf != shouldShow)
                {
                    marker.HighlightRoot.SetActive(shouldShow);
                }
            }
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

        private void GiveModeFItem(int typeId, string displayName)
        {
            try
            {
                Item item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    return;
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
                    }
                }

                ShowModeFRewardBubble(displayName + L10n.T("已到账", " received"));
            }
            catch { }
        }

        private void CleanupAllModeFortifications()
        {
            try
            {
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
            try
            {
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst == null || item == null)
                {
                    return;
                }

                switch (item.TypeID)
                {
                    case FoldableCoverPackConfig.TYPE_ID:
                        inst.UseModeFFortificationItem(FortificationType.FoldableCover);
                        break;
                    case ReinforcedRoadblockPackConfig.TYPE_ID:
                        inst.UseModeFFortificationItem(FortificationType.ReinforcedRoadblock);
                        break;
                    case BarbedWirePackConfig.TYPE_ID:
                        inst.UseModeFFortificationItem(FortificationType.BarbedWire);
                        break;
                    case EmergencyRepairSprayConfig.TYPE_ID:
                        inst.UseModeFRepairSpray();
                        break;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] ModeFItemUsage.OnUse failed: " + e.Message);
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
