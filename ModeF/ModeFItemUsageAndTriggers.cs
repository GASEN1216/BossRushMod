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

            if (!(inst.IsModeFActive || inst.IsZombieModeActive))
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

    /// <summary>
    /// 已弃用：旧版用 trigger+SetPosition 推挤角色，导致低端机严重卡顿。
    /// 现在 CharacterBlocker 改为非 trigger 物理碰撞体，由引擎自然阻挡。
    /// 保留空壳以兼容已存在的序列化实例，EnsureModeFFortificationCharacterBlocker 会自动清理。
    /// </summary>
    public class ModeFFortificationCharacterBlocker : MonoBehaviour
    {
        public void Bind(BoxCollider collider) { }
    }
}
