using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using TMPro;
using UnityEngine;

namespace BossRush
{
    internal static class MapSelectionEntryInjectionHelper
    {
        internal delegate void ConfigureEntryDelegate(MapSelectionEntry entry, BossRushMapConfig mapConfig, int entryIndex);
        internal delegate string GetDisplayNameDelegate(BossRushMapConfig mapConfig);
        internal delegate void EntryCreatedDelegate(MapSelectionEntry entry, GameObject entryObject, BossRushMapConfig mapConfig, int entryIndex, MapSelectionEntry template, Transform targetParent);

        internal sealed class TemplateContext
        {
            internal Transform ViewTransform;
            internal readonly List<MapSelectionEntry> TemplateCandidates = new List<MapSelectionEntry>();
            internal readonly Dictionary<string, MapSelectionEntry> TemplateBySceneId = new Dictionary<string, MapSelectionEntry>();
            internal readonly Dictionary<Transform, List<MapSelectionEntry>> ColumnGroups = new Dictionary<Transform, List<MapSelectionEntry>>();
            internal readonly List<Transform> ColumnOrder = new List<Transform>();
        }

        internal static int InjectEntries(
            MapSelectionView mapView,
            string entryNamePrefix,
            List<GameObject> createdEntries,
            List<GameObject> hiddenEntries,
            BossRushMapConfig[] mapConfigs,
            ConfigureEntryDelegate configureEntry,
            GetDisplayNameDelegate getDisplayName,
            Action<GameObject> setupCostDisplay,
            EntryCreatedDelegate onEntryCreated,
            out TemplateContext context)
        {
            context = null;
            if (mapView == null || createdEntries == null || hiddenEntries == null || mapConfigs == null || mapConfigs.Length == 0)
            {
                return 0;
            }

            if (!CollectTemplatesAndHideOriginals(mapView, entryNamePrefix, hiddenEntries, out context))
            {
                return 0;
            }

            Transform targetParent = context.ColumnOrder.Count > 0 ? context.ColumnOrder[0] : context.ViewTransform;
            MapSelectionEntry defaultTemplate = context.TemplateCandidates[0];

            for (int i = 0; i < mapConfigs.Length; i++)
            {
                BossRushMapConfig mapConfig = mapConfigs[i];
                if (mapConfig == null || string.IsNullOrEmpty(mapConfig.sceneID))
                {
                    continue;
                }

                MapSelectionEntry templateToUse = defaultTemplate;
                if (context.TemplateBySceneId.ContainsKey(mapConfig.sceneID))
                {
                    templateToUse = context.TemplateBySceneId[mapConfig.sceneID];
                }

                GameObject cloned = null;
                try
                {
                    cloned = UnityEngine.Object.Instantiate(templateToUse.gameObject, targetParent);
                    cloned.name = entryNamePrefix + i;
                    cloned.SetActive(false);

                    MapSelectionEntry uiEntry = cloned.GetComponent<MapSelectionEntry>();
                    if (uiEntry == null)
                    {
                        UnityEngine.Object.Destroy(cloned);
                        continue;
                    }

                    if (configureEntry != null)
                    {
                        configureEntry(uiEntry, mapConfig, i);
                    }

                    cloned.SetActive(true);

                    // Setup 内部会调用 Refresh()，访问 SceneInfoCollection.GetSceneInfo(sceneID).DisplayName
                    // 等会引发 NRE 的字段。把它独立包成 try，避免单个坏条目让整个注入抛出空消息异常。
                    try
                    {
                        uiEntry.Setup(mapView);
                    }
                    catch (Exception setupEx)
                    {
                        ModBehaviour.DevLog("[MapSelection] uiEntry.Setup 失败 (sceneID=" + mapConfig.sceneID + "): " + setupEx.GetType().Name + ": " + setupEx.Message + "\n" + setupEx.StackTrace);
                    }

                    if (setupCostDisplay != null)
                    {
                        setupCostDisplay(cloned);
                    }

                    if (getDisplayName != null)
                    {
                        SetEntryDisplayNameDirect(cloned, getDisplayName(mapConfig));
                    }

                    createdEntries.Add(cloned);

                    if (onEntryCreated != null)
                    {
                        onEntryCreated(uiEntry, cloned, mapConfig, i, templateToUse, targetParent);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[MapSelection] InjectEntries 单条目失败 (sceneID=" + mapConfig.sceneID + "): " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                    if (cloned != null)
                    {
                        try { UnityEngine.Object.Destroy(cloned); } catch { }
                    }
                }
            }

            MoveEntriesToFront(createdEntries);
            return createdEntries.Count;
        }

        internal static bool CollectTemplatesAndHideOriginals(
            MapSelectionView mapView,
            string injectedEntryNamePrefix,
            List<GameObject> hiddenEntries,
            out TemplateContext context)
        {
            context = new TemplateContext();
            if (mapView == null || hiddenEntries == null)
            {
                return false;
            }

            context.ViewTransform = mapView.transform;
            MapSelectionEntry[] existingEntries = context.ViewTransform.GetComponentsInChildren<MapSelectionEntry>(true);
            if (existingEntries == null || existingEntries.Length == 0)
            {
                return false;
            }

            foreach (MapSelectionEntry entry in existingEntries)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(injectedEntryNamePrefix) &&
                    entry.gameObject.name.StartsWith(injectedEntryNamePrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(entry.gameObject);
                    continue;
                }

                context.TemplateCandidates.Add(entry);

                string sid = entry.SceneID;
                if (!string.IsNullOrEmpty(sid) && !context.TemplateBySceneId.ContainsKey(sid))
                {
                    context.TemplateBySceneId[sid] = entry;
                }

                Transform parent = entry.transform.parent;
                if (parent != null)
                {
                    if (!context.ColumnGroups.ContainsKey(parent))
                    {
                        context.ColumnGroups[parent] = new List<MapSelectionEntry>();
                        context.ColumnOrder.Add(parent);
                    }
                    context.ColumnGroups[parent].Add(entry);
                }

                entry.gameObject.SetActive(false);
                hiddenEntries.Add(entry.gameObject);
            }

            return context.TemplateCandidates.Count > 0;
        }

        internal static void RestoreHiddenEntries(List<GameObject> hiddenEntries)
        {
            if (hiddenEntries == null)
            {
                return;
            }

            foreach (GameObject entry in hiddenEntries)
            {
                if (entry != null)
                {
                    entry.SetActive(true);
                }
            }
            hiddenEntries.Clear();
        }

        internal static void DestroyCreatedEntries(List<GameObject> createdEntries)
        {
            if (createdEntries == null)
            {
                return;
            }

            foreach (GameObject entry in createdEntries)
            {
                if (entry != null)
                {
                    UnityEngine.Object.Destroy(entry);
                }
            }
            createdEntries.Clear();
        }

        internal static void MoveEntriesToFront(List<GameObject> entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i] != null)
                {
                    entries[i].transform.SetAsFirstSibling();
                }
            }
        }

        /// <summary>
        /// 延迟三连刷：帧结束、+0.1 s、+0.3 s 三次回调，确保 BossRush/Zombie 注入条目的显示名不被
        /// 原版 UI Refresh() 覆盖。两个模式的 helper 都用同一份实现，避免分叉。
        /// </summary>
        internal static System.Collections.IEnumerator DelayedRefreshDisplayNames(System.Action refresh)
        {
            if (refresh == null)
            {
                yield break;
            }
            yield return new UnityEngine.WaitForEndOfFrame();
            try { refresh(); } catch (System.Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] DelayedRefresh frame-end failed: " + e.Message);
            }
            yield return new UnityEngine.WaitForSeconds(0.1f);
            try { refresh(); } catch (System.Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] DelayedRefresh +0.1s failed: " + e.Message);
            }
            yield return new UnityEngine.WaitForSeconds(0.2f);
            try { refresh(); } catch (System.Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] DelayedRefresh +0.3s failed: " + e.Message);
            }
        }

        internal static void SetMapSelectionEntryField(MapSelectionEntry entry, string fieldName, object value)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                FieldInfo field = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(MapSelectionEntry),
                    fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(entry, value);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] SetMapSelectionEntryField failed: " + fieldName + ", " + e.Message);
            }
        }

        internal static void ClearCostDisplayItems(CostDisplay costDisplay)
        {
            if (costDisplay == null)
            {
                return;
            }

            try
            {
                FieldInfo itemsContainerField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(CostDisplay),
                    "itemsContainer",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemsContainerField == null)
                {
                    return;
                }

                GameObject itemsContainer = itemsContainerField.GetValue(costDisplay) as GameObject;
                if (itemsContainer == null)
                {
                    return;
                }

                FieldInfo templateField = BossRush.Common.Utils.ReflectionCache.GetField(
                    typeof(CostDisplay),
                    "itemAmountTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                ItemAmountDisplay template = templateField != null ? templateField.GetValue(costDisplay) as ItemAmountDisplay : null;
                GameObject templateGO = template != null ? template.gameObject : null;

                List<GameObject> toDestroy = new List<GameObject>();
                for (int i = 0; i < itemsContainer.transform.childCount; i++)
                {
                    Transform child = itemsContainer.transform.GetChild(i);
                    if (templateGO != null && child.gameObject == templateGO)
                    {
                        continue;
                    }
                    toDestroy.Add(child.gameObject);
                }

                foreach (GameObject go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] ClearCostDisplayItems failed: " + e.Message);
            }
        }

        internal static void SetEntryDisplayNameDirect(GameObject clonedObject, string displayName)
        {
            if (clonedObject == null)
            {
                return;
            }

            try
            {
                TextMeshProUGUI[] textComps = clonedObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                bool found = false;
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    if (tmp != null && tmp.gameObject.name == "Text_MapName")
                    {
                        tmp.text = displayName;
                        found = true;
                    }
                }

                if (found)
                {
                    return;
                }

                foreach (TextMeshProUGUI tmp in textComps)
                {
                    if (tmp == null)
                    {
                        continue;
                    }

                    string objName = tmp.gameObject.name.ToLower();
                    if (objName.Contains("name") || objName.Contains("title") || objName.Contains("display"))
                    {
                        tmp.text = displayName;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[MapSelection] SetEntryDisplayNameDirect failed: " + e.Message);
            }
        }
    }
}
