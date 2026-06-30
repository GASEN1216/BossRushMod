using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 查找并注入交互目标
        /// scanTimes: 扫描次数，<=0 表示无限(不建议)
        /// Bug #2 修复：成功注入后立即停止扫描
        /// </summary>
        private IEnumerator FindInteractionTargets(int scanTimes)
        {
            int count = 0;
            while (count < scanTimes)
            {
                bool injected = ScanAndInject();
                count++;

                if (injected)
                {
                    DevLog("[BossRush] 场景扫描成功，已注入 BossRush 交互点，停止扫描。");
                    yield break;
                }

                yield return sharedWait1s;
            }
            DevLog("[BossRush] 场景扫描结束（未找到合适的注入点）。");
        }

        private bool ScanAndInject()
        {
            bool anyInjected = false;
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                string sceneName = activeScene.name;

                Vector3 targetPos;
                bool isBaseScene = IsBaseHubSceneName(sceneName);
                bool isArenaScene = sceneName == BossRushArenaSceneName;

                if (isBaseScene)
                {
                    targetPos = BaseEntryPosition;
                }
                else if (isArenaScene)
                {
                    return false;
                }
                else
                {
                    return false;
                }

                var allInteractables = FindObjectsOfType<InteractableBase>(true);
                InteractableBase boatInteract = null;
                float boatDistSq = float.MaxValue;

                if (allInteractables != null)
                {
                    foreach (var interact in allInteractables)
                    {
                        if (interact == null || interact.gameObject == null) continue;
                        if (interact is BossRushInteractable) continue;

                        string goName = interact.gameObject.name;
                        string path = GetGameObjectPath(interact.gameObject);
                        float distSq = (interact.transform.position - targetPos).sqrMagnitude;

                        bool isBoatPath = path.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isMainInteract = goName == "Interact" || interact.interactableGroup;
                        bool isSubInteract = goName.Contains("_");

                        if (isBoatPath && isMainInteract && !isSubInteract && distSq < boatDistSq)
                        {
                            boatDistSq = distSq;
                            boatInteract = interact;
                        }
                    }
                }

                if (boatInteract != null)
                {
                    if (TryInjectBaseHubBoatInteractable(boatInteract))
                    {
                        anyInjected = true;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 扫描出错: " + e.Message + "\n" + e.StackTrace);
            }
            return anyInjected;
        }

        /// <summary>
        /// 获取 GameObject 的完整层级路径
        /// </summary>
        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "<null>";
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        /// <summary>
        /// 获取私有的 group 列表
        /// </summary>
        private List<InteractableBase> GetGroupList(InteractableBase target)
        {
            try
            {
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field != null)
                {
                    return field.GetValue(target) as List<InteractableBase>;
                }
            }
            catch {}
            return null;
        }

        /// <summary>
        /// 通过反射注入到 InteractableBase 的 group 列表
        /// </summary>
        private bool InjectIntoInteractableBaseGroup(InteractableBase target)
        {
            return InjectIntoInteractableBaseGroup_UIAndSigns(target);
        }
    }
}
