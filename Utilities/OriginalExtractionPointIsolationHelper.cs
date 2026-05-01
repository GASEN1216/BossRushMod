using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class OriginalExtractionPointIsolationHelper
    {
        private static readonly BindingFlags InstanceBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] ExitCreatorAreaMemberNames =
        {
            "Areas",
            "areas",
            "CountDownAreas",
            "countDownAreas",
            "Exits",
            "exits"
        };

        internal static int Disable(
            IList<GameObject> disabledObjects,
            IDictionary<int, bool> activeStateByObjectId,
            Func<CountDownArea, bool> shouldSkipArea,
            string[] excludedNamePrefixes,
            Action<CountDownArea, GameObject, bool> onDisabled,
            out bool usedExitCreatorSnapshot,
            out int[] disabledObjectIds)
        {
            disabledObjectIds = new int[0];
            usedExitCreatorSnapshot = false;
            if (disabledObjects == null || activeStateByObjectId == null)
            {
                return 0;
            }

            disabledObjects.Clear();
            activeStateByObjectId.Clear();

            HashSet<int> seenIds = new HashSet<int>();
            List<CountDownArea> originalAreas = new List<CountDownArea>();
            try
            {
                usedExitCreatorSnapshot = TryCollectFromExitCreator(activeStateByObjectId, seenIds, originalAreas);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ExtractionIsolation] [WARNING] 从 ExitCreator 收集原始撤离点失败: " + e.Message);
                usedExitCreatorSnapshot = originalAreas.Count > 0;
            }
            if (!usedExitCreatorSnapshot)
            {
                CollectFromFallbackSceneScan(activeStateByObjectId, seenIds, originalAreas, shouldSkipArea, excludedNamePrefixes);
            }

            if (originalAreas.Count <= 0)
            {
                return 0;
            }

            int disabledActiveCount = 0;
            List<int> disabledIds = new List<int>();
            for (int i = 0; i < originalAreas.Count; i++)
            {
                try
                {
                    CountDownArea area = originalAreas[i];
                    if (area == null || area.gameObject == null)
                    {
                        continue;
                    }

                    if (shouldSkipArea != null && shouldSkipArea(area))
                    {
                        continue;
                    }

                    GameObject disabledObject = area.gameObject;
                    int objectId = disabledObject.GetInstanceID();
                    bool wasActive;
                    if (!activeStateByObjectId.TryGetValue(objectId, out wasActive))
                    {
                        wasActive = disabledObject.activeSelf;
                        activeStateByObjectId[objectId] = wasActive;
                    }

                    disabledObject.SetActive(false);
                    if (!disabledObjects.Contains(disabledObject))
                    {
                        disabledObjects.Add(disabledObject);
                    }
                    disabledIds.Add(objectId);
                    if (wasActive)
                    {
                        disabledActiveCount++;
                    }

                    if (onDisabled != null)
                    {
                        onDisabled(area, disabledObject, wasActive);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ExtractionIsolation] [WARNING] 禁用原始撤离点失败: " + e.Message);
                }
            }

            disabledObjectIds = disabledIds.ToArray();
            return disabledActiveCount;
        }

        internal static int Restore(
            IList<GameObject> disabledObjects,
            IDictionary<int, bool> activeStateByObjectId)
        {
            if (disabledObjects == null || activeStateByObjectId == null)
            {
                return 0;
            }

            int restored = 0;
            for (int i = disabledObjects.Count - 1; i >= 0; i--)
            {
                GameObject extractionObject = disabledObjects[i];
                if (extractionObject == null)
                {
                    continue;
                }

                try
                {
                    bool shouldBeActive;
                    if (!activeStateByObjectId.TryGetValue(extractionObject.GetInstanceID(), out shouldBeActive))
                    {
                        shouldBeActive = true;
                    }

                    extractionObject.SetActive(shouldBeActive);
                    if (shouldBeActive)
                    {
                        restored++;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ExtractionIsolation] [WARNING] 恢复原始撤离点失败: " + e.Message);
                }
            }

            disabledObjects.Clear();
            activeStateByObjectId.Clear();
            return restored;
        }

        private static void AddCandidate(
            CountDownArea area,
            IDictionary<int, bool> activeStateByObjectId,
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            if (area == null || area.gameObject == null || seenIds == null || results == null)
            {
                return;
            }

            if (!seenIds.Add(area.GetInstanceID()))
            {
                return;
            }

            if (activeStateByObjectId != null)
            {
                activeStateByObjectId[area.gameObject.GetInstanceID()] = area.gameObject.activeSelf;
            }
            results.Add(area);
        }

        private static void CollectFromObject(
            object source,
            IDictionary<int, bool> activeStateByObjectId,
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            if (source == null)
            {
                return;
            }

            CountDownArea countDownArea = source as CountDownArea;
            if (countDownArea != null)
            {
                AddCandidate(countDownArea, activeStateByObjectId, seenIds, results);
                return;
            }

            GameObject gameObject = source as GameObject;
            if (gameObject != null)
            {
                AddCandidate(gameObject.GetComponent<CountDownArea>(), activeStateByObjectId, seenIds, results);
                return;
            }

            Component component = source as Component;
            if (component != null)
            {
                AddCandidate(component.GetComponent<CountDownArea>(), activeStateByObjectId, seenIds, results);
                return;
            }

            System.Collections.IEnumerable enumerable = source as System.Collections.IEnumerable;
            if (enumerable == null || source is string)
            {
                return;
            }

            foreach (object item in enumerable)
            {
                CollectFromObject(item, activeStateByObjectId, seenIds, results);
            }
        }

        private static bool TryCollectFromExitCreator(
            IDictionary<int, bool> activeStateByObjectId,
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            LevelManager levelManager = LevelManager.Instance;
            if (levelManager == null)
            {
                levelManager = UnityEngine.Object.FindObjectOfType<LevelManager>();
            }

            if (levelManager == null)
            {
                return false;
            }

            object exitCreator = levelManager.ExitCreator;
            if (exitCreator == null && LevelManager.Instance != null)
            {
                exitCreator = LevelManager.Instance.ExitCreator;
            }

            if (exitCreator == null)
            {
                return false;
            }

            Component exitCreatorComponent = exitCreator as Component;
            if (exitCreatorComponent != null)
            {
                CountDownArea[] childAreas = exitCreatorComponent.GetComponentsInChildren<CountDownArea>(true);
                for (int i = 0; i < childAreas.Length; i++)
                {
                    AddCandidate(childAreas[i], activeStateByObjectId, seenIds, results);
                }
            }

            Type exitCreatorType = exitCreator.GetType();
            for (int i = 0; i < ExitCreatorAreaMemberNames.Length; i++)
            {
                string memberName = ExitCreatorAreaMemberNames[i];

                PropertyInfo property = exitCreatorType.GetProperty(memberName, InstanceBindingFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    CollectFromObject(property.GetValue(exitCreator, null), activeStateByObjectId, seenIds, results);
                }

                FieldInfo field = exitCreatorType.GetField(memberName, InstanceBindingFlags);
                if (field != null)
                {
                    CollectFromObject(field.GetValue(exitCreator), activeStateByObjectId, seenIds, results);
                }
            }

            return results.Count > 0;
        }

        private static void CollectFromFallbackSceneScan(
            IDictionary<int, bool> activeStateByObjectId,
            HashSet<int> seenIds,
            List<CountDownArea> results,
            Func<CountDownArea, bool> shouldSkipArea,
            string[] excludedNamePrefixes)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            CountDownArea[] areas = UnityEngine.Object.FindObjectsOfType<CountDownArea>(true);
            for (int i = 0; i < areas.Length; i++)
            {
                CountDownArea area = areas[i];
                if (!IsLikelyOriginalExtractionArea(area, activeStateByObjectId, shouldSkipArea, excludedNamePrefixes))
                {
                    continue;
                }

                Scene areaScene = area.gameObject.scene;
                if (!areaScene.IsValid() || areaScene != activeScene)
                {
                    continue;
                }

                AddCandidate(area, activeStateByObjectId, seenIds, results);
            }
        }

        private static bool IsLikelyOriginalExtractionArea(
            CountDownArea area,
            IDictionary<int, bool> activeStateByObjectId,
            Func<CountDownArea, bool> shouldSkipArea,
            string[] excludedNamePrefixes)
        {
            if (area == null || area.gameObject == null)
            {
                return false;
            }

            if (shouldSkipArea != null && shouldSkipArea(area))
            {
                return false;
            }

            string objectName = area.gameObject.name;
            if (excludedNamePrefixes != null)
            {
                for (int i = 0; i < excludedNamePrefixes.Length; i++)
                {
                    string prefix = excludedNamePrefixes[i];
                    if (!string.IsNullOrEmpty(prefix) && objectName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            if (activeStateByObjectId != null && activeStateByObjectId.ContainsKey(area.gameObject.GetInstanceID()))
            {
                return true;
            }

            if (area.onCountDownSucceed == null)
            {
                return false;
            }

            int persistentCount = area.onCountDownSucceed.GetPersistentEventCount();
            for (int i = 0; i < persistentCount; i++)
            {
                string methodName = area.onCountDownSucceed.GetPersistentMethodName(i);
                UnityEngine.Object target = area.onCountDownSucceed.GetPersistentTarget(i);
                string targetTypeName = target != null ? target.GetType().Name : string.Empty;

                if (string.Equals(methodName, "NotifyEvacuated", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "LevelManagerProxy", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadBaseScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadMainMenu", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "SceneLoaderProxy", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
