using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    internal sealed class ModeExtractionPointRequest
    {
        public string ObjectName = "BossRush_ExtractionPoint";
        public Vector3 Position;
        public float CountdownSeconds = 15f;
        public float FallbackTriggerRadius = 3f;
        public string LogPrefix = "[Extraction]";
        public bool DisableSmokeEffects = true;
        public Func<CountDownArea, bool> IsCurrentArea;
        public UnityAction OnSucceed;
        public Action<Vector3> OnFallbackNotify;
        public Action OnFallbackLoadBase;
    }

    internal sealed class ModeExtractionPointResult
    {
        public GameObject GameObject;
        public CountDownArea CountDownArea;
        public bool CreatedFromPrefab;
        public bool ShouldNotifyGameEvacuation;
        public bool ShouldLoadBaseScene;
    }

    internal static class ModeExtractionPointFactory
    {
        private static readonly FieldInfo RequiredExtractionTimeField =
            BossRush.Common.Utils.ReflectionCache.GetField(typeof(CountDownArea), "requiredExtrationTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CountDownTimeField =
            BossRush.Common.Utils.ReflectionCache.GetField(typeof(CountDownArea), "countDownTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        internal static ModeExtractionPointResult CreateExtractionPoint(ModeExtractionPointRequest request)
        {
            if (request == null)
            {
                return null;
            }

            bool createdFromPrefab;
            GameObject exitObj = TryCreateFromOfficialPrefab(request, out createdFromPrefab);
            if (exitObj == null)
            {
                exitObj = new GameObject(request.ObjectName);
                exitObj.transform.position = request.Position;
                exitObj.SetActive(true);
            }

            exitObj.name = request.ObjectName;
            PrepareExtractionPrefab(exitObj, request);

            CountDownArea countDown = exitObj.GetComponentInChildren<CountDownArea>(true);
            if (countDown == null)
            {
                countDown = exitObj.AddComponent<CountDownArea>();
            }

            countDown.enabled = true;
            ConfigureCountDown(countDown, request.CountdownSeconds);

            bool shouldNotifyGameEvacuation;
            bool shouldLoadBaseScene;
            ResolveFallbackActions(countDown.onCountDownSucceed, out shouldNotifyGameEvacuation, out shouldLoadBaseScene);
            ConfigureEvents(countDown, request, shouldNotifyGameEvacuation, shouldLoadBaseScene);

            ModeExtractionPointResult result = new ModeExtractionPointResult();
            result.GameObject = exitObj;
            result.CountDownArea = countDown;
            result.CreatedFromPrefab = createdFromPrefab;
            result.ShouldNotifyGameEvacuation = shouldNotifyGameEvacuation;
            result.ShouldLoadBaseScene = shouldLoadBaseScene;
            return result;
        }

        private static GameObject TryCreateFromOfficialPrefab(ModeExtractionPointRequest request, out bool createdFromPrefab)
        {
            createdFromPrefab = false;
            try
            {
                LevelManager levelManager = LevelManager.Instance;
                if (levelManager == null)
                {
                    levelManager = UnityEngine.Object.FindObjectOfType<LevelManager>();
                }

                if (levelManager == null || levelManager.ExitCreator == null || levelManager.ExitCreator.exitPrefab == null)
                {
                    return null;
                }

                createdFromPrefab = true;
                GameObject exitObj = UnityEngine.Object.Instantiate(levelManager.ExitCreator.exitPrefab, request.Position, Quaternion.identity);
                ModBehaviour.DevLog(request.LogPrefix + " 使用官方 exitPrefab 创建撤离点");
                return exitObj;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(request.LogPrefix + " [WARNING] 获取官方 exitPrefab 失败: " + e.Message);
                return null;
            }
        }

        private static void PrepareExtractionPrefab(GameObject exitObj, ModeExtractionPointRequest request)
        {
            if (exitObj == null)
            {
                return;
            }

            exitObj.transform.position = request.Position;
            exitObj.SetActive(true);
            ActivateAllChildren(exitObj);
            EnsureTriggerCollider(exitObj, request.FallbackTriggerRadius);

            Renderer[] renderers = exitObj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = true;
                }
            }

            if (request.DisableSmokeEffects)
            {
                DisableExitSmokeEffects(exitObj);
            }
        }

        private static void ActivateAllChildren(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            obj.SetActive(true);
            foreach (Transform child in obj.transform)
            {
                ActivateAllChildren(child.gameObject);
            }
        }

        private static void EnsureTriggerCollider(GameObject exitObj, float radius)
        {
            if (exitObj == null)
            {
                return;
            }

            Collider[] colliders = exitObj.GetComponentsInChildren<Collider>(true);
            bool hasTriggerCollider = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                collider.enabled = true;
                if (collider.isTrigger)
                {
                    hasTriggerCollider = true;
                }
            }

            if (hasTriggerCollider)
            {
                return;
            }

            SphereCollider fallbackCollider = exitObj.GetComponent<SphereCollider>();
            if (fallbackCollider == null)
            {
                fallbackCollider = exitObj.AddComponent<SphereCollider>();
            }

            fallbackCollider.enabled = true;
            fallbackCollider.isTrigger = true;
            fallbackCollider.radius = radius > 0f ? radius : 3f;
        }

        private static void DisableExitSmokeEffects(GameObject exitObj)
        {
            Transform[] children = exitObj.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == exitObj.transform)
                {
                    continue;
                }

                string lowerName = child.name.ToLowerInvariant();
                if (lowerName.Contains("smoke") ||
                    lowerName.Contains("fog") ||
                    lowerName.Contains("particle") ||
                    lowerName.Contains("vfx"))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static void ConfigureCountDown(CountDownArea countDown, float seconds)
        {
            if (countDown == null)
            {
                return;
            }

            try
            {
                if (RequiredExtractionTimeField != null)
                {
                    RequiredExtractionTimeField.SetValue(countDown, seconds);
                }

                if (CountDownTimeField != null)
                {
                    CountDownTimeField.SetValue(countDown, seconds);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Extraction] ConfigureCountDown failed: " + e.Message);
            }
        }

        private static void ConfigureEvents(
            CountDownArea countDown,
            ModeExtractionPointRequest request,
            bool shouldNotifyGameEvacuation,
            bool shouldLoadBaseScene)
        {
            if (countDown.onCountDownStarted == null)
            {
                countDown.onCountDownStarted = new UnityEvent<CountDownArea>();
            }
            countDown.onCountDownStarted.AddListener(delegate(CountDownArea area)
            {
                if (request.IsCurrentArea == null || request.IsCurrentArea(area))
                {
                    try { EvacuationCountdownUI.Request(area); } catch { }
                }
            });

            if (countDown.onCountDownStopped == null)
            {
                countDown.onCountDownStopped = new UnityEvent<CountDownArea>();
            }
            countDown.onCountDownStopped.AddListener(delegate(CountDownArea area)
            {
                if (request.IsCurrentArea == null || request.IsCurrentArea(area))
                {
                    try { EvacuationCountdownUI.Release(area); } catch { }
                }
            });

            if (countDown.onCountDownSucceed == null)
            {
                countDown.onCountDownSucceed = new UnityEvent();
            }
            countDown.onCountDownSucceed.AddListener(delegate
            {
                if (request.IsCurrentArea != null && !request.IsCurrentArea(countDown))
                {
                    return;
                }

                if (request.OnSucceed != null)
                {
                    request.OnSucceed();
                }

                if (shouldNotifyGameEvacuation && request.OnFallbackNotify != null)
                {
                    request.OnFallbackNotify(request.Position);
                }

                if (shouldLoadBaseScene && request.OnFallbackLoadBase != null)
                {
                    request.OnFallbackLoadBase();
                }
            });
        }

        private static void ResolveFallbackActions(
            UnityEvent onCountDownSucceed,
            out bool shouldNotifyGameEvacuation,
            out bool shouldLoadBaseScene)
        {
            shouldNotifyGameEvacuation = true;
            shouldLoadBaseScene = true;

            if (onCountDownSucceed == null)
            {
                return;
            }

            int persistentCount = onCountDownSucceed.GetPersistentEventCount();
            for (int i = 0; i < persistentCount; i++)
            {
                string methodName = onCountDownSucceed.GetPersistentMethodName(i);
                UnityEngine.Object target = onCountDownSucceed.GetPersistentTarget(i);
                string targetTypeName = target != null ? target.GetType().Name : string.Empty;

                if (string.Equals(methodName, "NotifyEvacuated", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "LevelManagerProxy", StringComparison.Ordinal))
                {
                    shouldNotifyGameEvacuation = false;
                }

                if (string.Equals(methodName, "LoadScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadBaseScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadMainMenu", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "SceneLoaderProxy", StringComparison.Ordinal))
                {
                    shouldLoadBaseScene = false;
                }
            }
        }
    }
}
