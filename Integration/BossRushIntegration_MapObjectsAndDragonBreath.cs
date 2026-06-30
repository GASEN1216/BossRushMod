using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 在 BossRush 模式下生成地图阻挡物（通用函数）
        /// [性能优化] 使用模板缓存避免重复查找，减少 FindObjectsOfType 调用
        /// </summary>
        private void SpawnBossRushMapObjects()
        {
            try
            {
                // Mode E（划地为营）不走 BossRush 竞技场流程，不应到达此方法
                // 保留防御性检查以防万一
                if (modeEActive)
                {
                    DevLog("[BossRush] SpawnBossRushMapObjects: Mode E 模式，跳过所有竞技场物件生成");
                    return;
                }

                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                List<MapObjectCloneConfig> configs = GetMapCloneConfigs(currentScene);

                if (configs.Count == 0)
                {
                    DevLog("[BossRush] SpawnBossRushMapObjects: 当前地图 " + currentScene + " 没有配置复制物品");
                    return;
                }

                DevLog("[BossRush] SpawnBossRushMapObjects: 开始在 " + currentScene + " 生成 " + configs.Count + " 个阻挡物");

                // 如果配置数量较多（如仓库区84个围栏），使用协程分帧生成避免卡顿
                if (configs.Count > 20)
                {
                    StartCoroutine(SpawnMapObjectsAsync(configs));
                }
                else
                {
                    // [性能优化] 少量配置也使用模板缓存，避免重复遍历
                    SpawnMapObjectsWithCache(configs);
                }

                // 为特定地图创建撤离点（使用游戏原生方式）
                CreateBossRushExitForScene(currentScene);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] SpawnBossRushMapObjects 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为指定场景创建 BossRush 撤离点
        /// </summary>
        private void CreateBossRushExitForScene(string sceneName)
        {
            // 风暴区地下场景
            if (sceneName == "Level_StormZone_B0")
            {
                CreateBossRushExit(new Vector3(109.92f, 0.02f, 503.95f), "BossRush_Exit_StormZone");
            }
            // 37号实验区撤离点
            else if (sceneName == "Level_SnowMilitaryBase")
            {
                CreateBossRushExit(new Vector3(511.32f, 0.04f, 558.61f), "BossRush_Exit_Zone37");
            }
            // 迷宫撤离点
            else if (sceneName == "Level_SnowMilitaryBase_ColdStorage")
            {
                CreateBossRushExit(new Vector3(24.78f, 0.02f, -60.28f), "BossRush_Exit_Maze");
            }
            // 其他需要自定义撤离点的场景可以在这里添加
        }

        /// <summary>
        /// 使用模板缓存同步生成地图阻挡物（适用于少量配置）
        /// [性能优化] 只遍历一次场景对象，缓存所有需要的模板
        /// </summary>
        private void SpawnMapObjectsWithCache(List<MapObjectCloneConfig> configs)
        {
            // 收集所有需要查找的模板名称
            HashSet<string> templateNames = new HashSet<string>();
            foreach (var config in configs)
            {
                templateNames.Add(config.templateName);
            }

            // 一次性查找所有模板（只遍历一次场景）
            Dictionary<string, GameObject> templateCache = new Dictionary<string, GameObject>();
            Dictionary<string, Transform> parentCache = new Dictionary<string, Transform>();

            // [性能优化] 使用 FindObjectsOfType<Transform> 比 FindObjectsOfType<GameObject> 更快
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();

            foreach (Transform t in allTransforms)
            {
                if (templateNames.Contains(t.name))
                {
                    // 为每个配置检查父对象前缀
                    foreach (var config in configs)
                    {
                        if (t.name == config.templateName)
                        {
                            string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                            if (!templateCache.ContainsKey(cacheKey))
                            {
                                if (t.parent != null && t.parent.name.StartsWith(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                                else if (string.IsNullOrEmpty(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                            }
                        }
                    }
                }
            }

            // 使用缓存的模板生成所有阻挡物
            int totalCreated = 0;
            foreach (var config in configs)
            {
                string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                if (templateCache.TryGetValue(cacheKey, out GameObject template))
                {
                    Transform parentTransform = parentCache.ContainsKey(cacheKey) ? parentCache[cacheKey] : null;
                    CloneMapObjectFast(template, parentTransform, config);
                    totalCreated++;
                }
                else
                {
                    DevLog("[BossRush] CloneMapObject: 未找到模板 " + config.templateName + " (父对象前缀: " + config.parentNamePrefix + ")");
                }
            }

            DevLog("[BossRush] SpawnBossRushMapObjects: 完成，共创建 " + totalCreated + " 个阻挡物");
        }

        /// <summary>
        /// 异步分帧生成地图阻挡物（平滑生成，在2秒内完成，避免卡顿）
        /// [性能优化] 使用 Transform 查找替代 GameObject，减少内存分配
        /// </summary>
        private System.Collections.IEnumerator SpawnMapObjectsAsync(List<MapObjectCloneConfig> configs)
        {
            // 等待一小段时间让场景完全稳定后再开始生成
            yield return new WaitForSeconds(0.3f);

            // [性能优化] 收集所有需要查找的模板名称
            HashSet<string> templateNames = new HashSet<string>();
            foreach (var config in configs)
            {
                templateNames.Add(config.templateName);
            }

            // [性能优化] 使用 FindObjectsOfType<Transform> 比 FindObjectsOfType<GameObject> 更快
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();

            // 预先查找并缓存模板对象（只遍历一次）
            Dictionary<string, GameObject> templateCache = new Dictionary<string, GameObject>();
            Dictionary<string, Transform> parentCache = new Dictionary<string, Transform>();

            foreach (Transform t in allTransforms)
            {
                if (templateNames.Contains(t.name))
                {
                    foreach (var config in configs)
                    {
                        if (t.name == config.templateName)
                        {
                            string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                            if (!templateCache.ContainsKey(cacheKey))
                            {
                                if (t.parent != null && t.parent.name.StartsWith(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                                else if (string.IsNullOrEmpty(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                            }
                        }
                    }
                }
            }

            // 平滑生成：每帧只生成少量对象，在约2秒内完成
            // 84个围栏，2秒 = 120帧（60fps），每帧约0.7个 -> 每帧1个，间隔约0.02秒
            const int batchSize = 3;  // [性能优化] 每批生成3个，加快生成速度
            const float batchInterval = 0.016f;  // 约60fps的帧间隔
            int count = 0;
            int totalCreated = 0;

            foreach (MapObjectCloneConfig config in configs)
            {
                string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                GameObject template = templateCache.ContainsKey(cacheKey) ? templateCache[cacheKey] : null;
                Transform parentTransform = parentCache.ContainsKey(cacheKey) ? parentCache[cacheKey] : null;

                if (template != null)
                {
                    // 快速克隆（不输出单个日志）
                    CloneMapObjectFast(template, parentTransform, config);
                    totalCreated++;
                }

                count++;
                if (count >= batchSize)
                {
                    count = 0;
                    yield return new WaitForSeconds(batchInterval);  // 等待一小段时间
                }
            }

            DevLog("[BossRush] SpawnBossRushMapObjects: 异步生成完成，共创建 " + totalCreated + " 个阻挡物");
        }

        /// <summary>
        /// 快速克隆地图物品（不输出单个日志，用于批量生成）
        /// [修复] 撤离点复制后自动激活，确保玩家可以使用
        /// </summary>
        private void CloneMapObjectFast(GameObject template, Transform parentTransform, MapObjectCloneConfig config)
        {
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(template);
                clone.name = config.cloneName;
                clone.transform.position = config.targetPosition;

                if (config.rotationY.HasValue)
                {
                    Vector3 templateEuler = template.transform.rotation.eulerAngles;
                    clone.transform.rotation = Quaternion.Euler(templateEuler.x, config.rotationY.Value, templateEuler.z);
                }
                else
                {
                    clone.transform.rotation = template.transform.rotation;
                }
                clone.transform.localScale = template.transform.localScale;

                if (parentTransform != null)
                {
                    clone.transform.SetParent(parentTransform);
                }

                // [修复] 如果是撤离点（包含 Exit 或 CountDownArea），确保完全激活
                if (config.cloneName.Contains("Exit") || clone.GetComponent<CountDownArea>() != null)
                {
                    // 递归激活所有子对象（确保视觉效果可见）
                    ActivateAllChildren(clone);

                    // 确保 CountDownArea 组件启用
                    CountDownArea countDown = clone.GetComponent<CountDownArea>();
                    if (countDown != null)
                    {
                        countDown.enabled = true;
                    }

                    // 确保 Collider 启用（用于触发进入检测）
                    Collider col = clone.GetComponent<Collider>();
                    if (col != null)
                    {
                        col.enabled = true;
                    }

                    // 启用所有 Renderer（确保可见）
                    Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        r.enabled = true;
                    }

                    DevLog("[BossRush] 撤离点已激活: " + config.cloneName + " 位置: " + config.targetPosition + ", 子对象数: " + clone.transform.childCount);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CloneMapObjectFast 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 递归激活 GameObject 及其所有子对象
        /// </summary>
        private void ActivateAllChildren(GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(true);
            foreach (Transform child in obj.transform)
            {
                ActivateAllChildren(child.gameObject);
            }
        }

        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 开始等待地图完全初始化...");

            // 等待条件：场景已加载、SceneLoader 不在加载中、CharacterMainControl.Main 和 GameCamera.Instance 均已存在
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxWait)
            {
                attempt++;
                bool sceneLoaded = scene.isLoaded;
                bool sceneLoaderDone = ReadSceneLoaderDoneWithWarning("WaitForLevelInitializedThenSetup");
                bool mainExists = ReadMainExistsWithWarning("WaitForLevelInitializedThenSetup");
                bool cameraExists = ReadCameraExistsWithWarning("WaitForLevelInitializedThenSetup");
                bool levelInited = ReadLevelInitedWithWarning("WaitForLevelInitializedThenSetup");

                if (sceneLoaded && sceneLoaderDone && mainExists && cameraExists && levelInited)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 地图初始化完成，第 " + attempt + " 次检查，elapsed=" + elapsed + "s");
                    break;
                }

                if (attempt % 10 == 0)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 第 " + attempt + " 次检查, sceneLoaded=" + sceneLoaded + ", sceneLoaderDone=" + sceneLoaderDone + ", mainExists=" + mainExists + ", cameraExists=" + cameraExists + ", levelInited=" + levelInited + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 结束等待, elapsed=" + elapsed + "s");

            // 执行原来的设置逻辑
            StartCoroutine(SetupBossRushInDemoChallenge(scene));
        }

        // ========== 龙息武器火焰特效（仅视觉效果）==========

        // 是否已订阅手持物品变更事件
        private bool dragonBreathEffectEventSubscribed = false;
        // 缓存的玩家角色引用
        private CharacterMainControl cachedMainCharForEffect = null;

        /// <summary>
        /// 延迟订阅龙息武器火焰特效事件（等待玩家角色初始化）
        /// </summary>
        private System.Collections.IEnumerator DelayedSubscribeDragonBreathEvents()
        {
            // 等待0.5秒确保玩家角色已初始化
            yield return sharedWait05s;
            SubscribeDragonBreathEffectEvent();
        }

        /// <summary>
        /// 延迟重新应用龙枪弹种属性覆盖（场景加载后等待玩家角色和武器初始化）
        /// </summary>
        private System.Collections.IEnumerator DelayedApplyDragonGunAmmoOverride()
        {
            yield return new WaitForSeconds(0.6f);
            try
            {
                DragonKingBossGunRuntime.ReapplyAmmoAttributeOverrideForScene();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] 龙枪弹种属性覆盖异常: " + e.Message);
            }
        }

        /// <summary>
        /// 订阅手持物品变更事件（用于添加火焰特效）
        /// </summary>
        private void SubscribeDragonBreathEffectEvent()
        {
            try
            {
                if (LevelManager.Instance == null) return;
                var mainChar = LevelManager.Instance.MainCharacter;
                if (mainChar == null) return;

                // 如果已订阅同一个角色，跳过
                if (dragonBreathEffectEventSubscribed && cachedMainCharForEffect == mainChar) return;

                // 先取消之前的订阅
                UnsubscribeDragonBreathEffectEvent();

                // 订阅手持物品变更事件
                mainChar.OnHoldAgentChanged += OnPlayerHoldAgentChanged;

                cachedMainCharForEffect = mainChar;
                dragonBreathEffectEventSubscribed = true;

                DevLog("[DragonBreath] 已订阅手持物品变更事件（火焰特效）");

                // 始终订阅Buff事件（龙裔遗族Boss也会发射龙息子弹，需要触发龙焰灼烧Buff）
                DragonBreathBuffHandler.Subscribe();

                DuckovItemAgent currentHoldAgent = mainChar.CurrentHoldItemAgent;
                RestoreRuntimeStateForHoldAgent(currentHoldAgent, "SubscribeDragonBreathEffectEvent");

                // 检查当前手持的武器（处理玩家进入存档时已装备特殊武器的情况）
                if (currentHoldAgent != null &&
                    currentHoldAgent.Item != null &&
                    currentHoldAgent.Item.TypeID == FrostmourneIds.WeaponTypeId)
                {
                    FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(currentHoldAgent.gameObject);
                }

                var currentGun = mainChar.GetGun();
                if (currentGun != null)
                {
                    // 添加火焰特效
                    DragonBreathWeaponConfig.TryAddFireEffectsToAgent(currentGun);
                    DragonKingBossGunRuntime.TryAddFireEffectsToAgent(currentGun);
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonBreath] 订阅火焰特效事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消订阅火焰特效事件
        /// </summary>
        private void UnsubscribeDragonBreathEffectEvent()
        {
            CharacterMainControl subscribedCharacter = cachedMainCharForEffect;
            try
            {
                if (!dragonBreathEffectEventSubscribed || subscribedCharacter == null) return;

                subscribedCharacter.OnHoldAgentChanged -= OnPlayerHoldAgentChanged;
            }
            catch (Exception e)
            {
                DevLog("[DragonBreath] [WARNING] 取消订阅火焰特效事件失败: " + e.Message);
            }
            finally
            {
                cachedMainCharForEffect = null;
                dragonBreathEffectEventSubscribed = false;
            }
        }

        /// <summary>
        /// 玩家手持物品变更回调（添加火焰特效）
        /// 注意：Buff事件订阅不再与玩家手持武器绑定，因为龙裔遗族Boss也会发射龙息子弹
        /// </summary>
        private void OnPlayerHoldAgentChanged(DuckovItemAgent newAgent)
        {
            RestoreRuntimeStateForHoldAgent(newAgent, "OnHoldAgentChanged");

            bool isFrostmourne = newAgent != null &&
                                 newAgent.Item != null &&
                                 newAgent.Item.TypeID == FrostmourneIds.WeaponTypeId;
            var gunAgent = newAgent as ItemAgent_Gun;

            // 检查是否为龙息武器
            bool isDragonBreath = gunAgent != null &&
                                  gunAgent.Item != null &&
                                  gunAgent.Item.TypeID == DragonBreathConfig.WEAPON_TYPE_ID;
            bool isDragonKingBossGun = gunAgent != null &&
                                       gunAgent.Item != null &&
                                       gunAgent.Item.TypeID == DragonKingBossGunConfig.WeaponTypeId;

            if (isFrostmourne)
            {
                FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(newAgent.gameObject);
            }
            else if (isDragonBreath)
            {
                // 装备龙息武器：添加火焰特效
                DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gunAgent);
            }
            else if (isDragonKingBossGun)
            {
                DragonKingBossGunRuntime.TryAddFireEffectsToAgent(gunAgent);
            }
            // 不再在此处取消订阅Buff事件，因为Boss的龙息子弹也需要触发龙焰灼烧Buff
        }
    }
}
