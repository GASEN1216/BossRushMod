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
        /// Prepares grouped interaction internals before InteractableBase.Awake touches them.
        /// The game's GetInteractableList() crashes when interactableGroup is true but the private list is still null.
        /// </summary>
        public static List<InteractableBase> PrepareGroupedInteractionOwner(InteractableBase owner, string logPrefix)
        {
            if (owner == null)
            {
                ModBehaviour.DevLog(logPrefix + " [ERROR] Failed to prepare grouped interaction: owner is null");
                return null;
            }

            owner.interactableGroup = true;
            return GetOrCreateGroupList(owner, logPrefix);
        }

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
                var field = BossRushEagerReflectionCache.InteractableBase_OtherInterablesInGroup;
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

            Transform existingChild = parent.Find(childName);
            GameObject childObj = existingChild != null ? existingChild.gameObject : new GameObject(childName);
            bool restoreActive = childObj.activeSelf;
            if (restoreActive)
            {
                childObj.SetActive(false);
            }

            try
            {
                childObj.transform.SetParent(parent, false);
                childObj.transform.localPosition = Vector3.zero;
                childObj.transform.localRotation = Quaternion.identity;
                childObj.transform.localScale = Vector3.one;

                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    childObj.layer = interactableLayer;
                }

                BoxCollider boxCollider = childObj.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = childObj.AddComponent<BoxCollider>();
                }

                boxCollider.isTrigger = true;
                boxCollider.size = new Vector3(0.1f, 0.1f, 0.1f);
                boxCollider.enabled = false;

                T component = childObj.GetComponent<T>();
                if (component == null)
                {
                    component = childObj.AddComponent<T>();
                }

                // 官方语义：交互组只有宿主显示交互气泡，组员的气泡在宿主 Awake 里关掉、偏移对齐宿主。
                // 运行时后加的组员赶不上宿主 Awake，不在这里补就会每个选项各挂一个气泡，全堆在 NPC 身上。
                // 偏移先继承、再让 setup 覆盖；气泡在 setup 之后关，子类 setup 不会把它重新打开。
                InteractableBase owner = FindGroupOwner(parent, groupList);
                if (owner != null)
                {
                    component.interactMarkerOffset = owner.interactMarkerOffset;
                }

                setup?.Invoke(component);
                component.MarkerActive = false;

                if (!groupList.Contains(component))
                {
                    groupList.Add(component);
                }

                return component;
            }
            finally
            {
                if (restoreActive)
                {
                    childObj.SetActive(true);
                }
            }
        }

        /// <summary>parent 上持有这份组员清单的宿主交互体；找不到返回 null。</summary>
        private static InteractableBase FindGroupOwner(Transform parent, List<InteractableBase> groupList)
        {
            var field = BossRushEagerReflectionCache.InteractableBase_OtherInterablesInGroup;
            if (parent == null || groupList == null || field == null)
            {
                return null;
            }

            try
            {
                foreach (InteractableBase candidate in parent.GetComponents<InteractableBase>())
                {
                    if (candidate != null && ReferenceEquals(field.GetValue(candidate), groupList))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // 反射读组员清单失败：不继承偏移，照常挂组员
            }

            return null;
        }

        /// <summary>
        /// Gets or creates a standalone world-space interactable anchor without touching grouped interaction internals.
        /// </summary>
        public static T GetOrCreateStandaloneInteractable<T>(
            Transform parent,
            string childName,
            Vector3 localPosition,
            Action<T> setup = null)
            where T : InteractableBase
        {
            if (parent == null)
            {
                return null;
            }

            Transform child = parent.Find(childName);
            GameObject childObj = child != null ? child.gameObject : new GameObject(childName);
            bool createdChild = child == null;
            bool restoreActive = childObj.activeSelf;
            bool setupSucceeded = false;
            if (restoreActive)
            {
                childObj.SetActive(false);
            }

            try
            {
                childObj.transform.SetParent(parent, false);
                childObj.transform.localPosition = localPosition;
                childObj.transform.localRotation = Quaternion.identity;
                childObj.transform.localScale = Vector3.one;

                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    childObj.layer = interactableLayer;
                }

                SphereCollider sphereCollider = childObj.GetComponent<SphereCollider>();
                if (sphereCollider == null)
                {
                    sphereCollider = childObj.AddComponent<SphereCollider>();
                }

                sphereCollider.isTrigger = false;
                sphereCollider.radius = 0.22f;
                sphereCollider.center = Vector3.zero;

                T component = childObj.GetComponent<T>();
                if (component == null)
                {
                    component = childObj.AddComponent<T>();
                }

                if (GetOrCreateGroupList(
                        component,
                        "[NPCInteractionGroup/Standalone]") == null)
                {
                    throw new InvalidOperationException(
                        "failed to initialize InteractableBase group list");
                }

                setup?.Invoke(component);
                setupSucceeded = true;
                return component;
            }
            finally
            {
                if (!setupSucceeded && createdChild && childObj != null)
                {
                    UnityEngine.Object.Destroy(childObj);
                }
                else if (restoreActive && childObj != null)
                {
                    childObj.SetActive(true);
                }
            }
        }
    }
}
