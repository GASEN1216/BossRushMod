// ============================================================================
// NPCInteractionGroupHelper.cs - interaction group helper
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush.Utils
{
    /// <summary>
    /// Builds grouped NPC interactions and sub-interactions.
    /// </summary>
    public static class NPCInteractionGroupHelper
    {
        /// <summary>
        /// Gets or creates the internal grouped interaction list.
        /// </summary>
        public static List<InteractableBase> GetOrCreateGroupList(InteractableBase owner, string logPrefix)
        {
            if (owner == null)
            {
                ModBehaviour.DevLog(logPrefix + " [ERROR] Failed to build interaction group: owner is null");
                return null;
            }

            try
            {
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    ModBehaviour.DevLog(logPrefix + " [ERROR] Failed to resolve otherInterablesInGroup field");
                    return null;
                }

                var list = field.GetValue(owner) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(owner, list);
                }

                return list;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [ERROR] Failed to get interaction group list: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Adds a child interactable into the interaction group.
        /// </summary>
        public static T AddSubInteractable<T>(
            Transform parent,
            string childName,
            List<InteractableBase> groupList,
            Action<T> setup = null)
            where T : InteractableBase
        {
            if (parent == null || groupList == null)
            {
                return null;
            }

            GameObject childObj = new GameObject(childName);
            childObj.transform.SetParent(parent, false);
            childObj.transform.localPosition = Vector3.zero;

            T component = childObj.AddComponent<T>();
            setup?.Invoke(component);

            groupList.Add(component);
            return component;
        }
    }
}
