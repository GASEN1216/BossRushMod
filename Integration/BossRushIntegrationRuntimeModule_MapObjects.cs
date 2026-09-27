using System;
using System.Collections.Generic;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>竞技场地图克隆与撤离点生成的 Integration runtime 业务。</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        /// <summary>
        /// 在 BossRush 模式下生成地图阻挡物（通用函数）
        /// [性能优化] 使用模板缓存避免重复查找，减少 FindObjectsOfType 调用
        /// </summary>
        internal void SpawnBossRushMapObjects()
        {
            try
            {
                // Mode E（划地为营）不走 BossRush 竞技场流程，不应到达此方法
                // 保留防御性检查以防万一
                if (_owner.IsModeEActive)
                {
                    ModBehaviour.DevLog("[BossRush] SpawnBossRushMapObjects: Mode E 模式，跳过所有竞技场物件生成");
                    return;
                }

                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                List<MapObjectCloneConfig> configs = GetMapCloneConfigs(currentScene);

                if (configs.Count == 0)
                {
                    ModBehaviour.DevLog("[BossRush] SpawnBossRushMapObjects: 当前地图 " + currentScene + " 没有配置复制物品");
                    return;
                }

                ModBehaviour.DevLog("[BossRush] SpawnBossRushMapObjects: 开始在 " + currentScene + " 生成 " + configs.Count + " 个阻挡物");

                // [性能优化] 统一走异步分帧生成，避免在过图帧同步 FindObjectsOfType<Transform>
                // 全场景扫描 + O(transforms×configs) 嵌套循环造成卡顿。
                // 之前 ≤20 个配置走同步分支，仍会在场景加载帧整段阻塞。
                _owner.StartCoroutine(SpawnMapObjectsAsync(configs));

                // 为特定地图创建撤离点（使用游戏原生方式）
                CreateBossRushExitForScene(currentScene);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SpawnBossRushMapObjects 失败: " + e.Message);
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
            UnityEngine.Object[] allTransforms = ObjectCache.GetSceneObjectsByType(typeof(Transform));

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

            ModBehaviour.DevLog("[BossRush] SpawnBossRushMapObjects: 异步生成完成，共创建 " + totalCreated + " 个阻挡物");
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

                    ModBehaviour.DevLog("[BossRush] 撤离点已激活: " + config.cloneName + " 位置: " + config.targetPosition + ", 子对象数: " + clone.transform.childCount);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] CloneMapObjectFast 失败: " + e.Message);
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

        internal System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            ModBehaviour.DevLog("[BossRush] WaitForLevelInitializedThenSetup: 开始等待地图完全初始化...");

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
                    ModBehaviour.DevLog("[BossRush] WaitForLevelInitializedThenSetup: 地图初始化完成，第 " + attempt + " 次检查，elapsed=" + elapsed + "s");
                    break;
                }

                if (attempt % 10 == 0)
                {
                    ModBehaviour.DevLog("[BossRush] WaitForLevelInitializedThenSetup: 第 " + attempt + " 次检查, sceneLoaded=" + sceneLoaded + ", sceneLoaderDone=" + sceneLoaderDone + ", mainExists=" + mainExists + ", cameraExists=" + cameraExists + ", levelInited=" + levelInited + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            ModBehaviour.DevLog("[BossRush] WaitForLevelInitializedThenSetup: 结束等待, elapsed=" + elapsed + "s");

            // 执行原来的设置逻辑
            _owner.StartBossRushDemoChallengeSetupForScene(scene);
        }

        /// <summary>
        /// BossRush 地图物品复制配置
        /// </summary>
        private class MapObjectCloneConfig
        {
            public string templateName;      // 模板对象名称
            public string parentNamePrefix;  // 父对象名称前缀（用于查找）
            public Vector3 targetPosition;   // 目标位置
            public string cloneName;         // 克隆后的名称
            public float? rotationY;         // Y轴旋转角度（可选，null表示使用模板旋转）

            public MapObjectCloneConfig(string template, string parentPrefix, Vector3 pos, string name, float? rotation = null)
            {
                templateName = template;
                parentNamePrefix = parentPrefix;
                targetPosition = pos;
                cloneName = name;
                rotationY = rotation;
            }
        }

        /// <summary>
        /// 获取指定地图的物品复制配置列表
        /// </summary>
        private List<MapObjectCloneConfig> GetMapCloneConfigs(string sceneName)
        {
            List<MapObjectCloneConfig> configs = new List<MapObjectCloneConfig>();

            if (sceneName == "Level_GroundZero_1")
            {
                // 零号区地图的复制配置

                // 1. 路障 - 封堵出口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_Roadblock_1",
                    "Group_",
                    new Vector3(425.35f, 0.02f, 254.49f),
                    "BossRush_Roadblock"
                ));

                // 2. 火焰烟雾特效 - 复制到出口位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_GroundZero_1",
                    new Vector3(447.50f, 0.01f, 288.27f),
                    "BossRush_Exit_FireSmoke"
                ));

                // 3. 铁丝网 - 封堵地形缺口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_BarbedWire_01_03_20",
                    "Group_",
                    new Vector3(455.80f, 0.02f, 306.78f),
                    "BossRush_BarbedWire"
                ));
            }
            else if (sceneName == "Level_ChallengeSnow")
            {
                // 零度挑战地图的复制配置

                // 1. 集装箱 - 作为竞技场边界
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Container_01_B_Season_64",
                    "Env",
                    new Vector3(242.54f, -0.01f, 259.44f),
                    "BossRush_Container_Clone",
                    270f  // Y轴旋转270度
                ));

                // 2. 篝火 - 作为交互点载体
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Campingfire",
                    "Env",
                    new Vector3(225.32f, 0.01f, 285.64f),
                    "BossRush_Campfire_Interact",
                    0f
                ));
            }
            else if (sceneName == "Level_HiddenWarehouse")
            {
                // 仓库区地图的围栏配置（使用 Prfb_Roadblock_33）
                // 围栏数据从 Player.log 提取

                // 南侧围栏（旋转0°和180°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(125.90f, 0.00f, 162.66f), "BossRush_Barrier_1", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(123.65f, 0.00f, 162.67f), "BossRush_Barrier_2", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(121.36f, 0.00f, 162.67f), "BossRush_Barrier_3", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(119.09f, 0.00f, 162.65f), "BossRush_Barrier_4", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(116.81f, 0.00f, 162.64f), "BossRush_Barrier_5", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(114.54f, 0.00f, 162.64f), "BossRush_Barrier_6", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(112.27f, 0.00f, 162.63f), "BossRush_Barrier_7", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.02f, 0.00f, 162.64f), "BossRush_Barrier_8", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.76f, 0.00f, 162.65f), "BossRush_Barrier_9", 0f));

                // 西侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.72f, 0.00f, 163.89f), "BossRush_Barrier_10", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 166.16f), "BossRush_Barrier_11", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 168.44f), "BossRush_Barrier_12", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.75f, 0.00f, 170.70f), "BossRush_Barrier_13", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 172.90f), "BossRush_Barrier_14", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 175.11f), "BossRush_Barrier_15", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 177.37f), "BossRush_Barrier_16", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 179.62f), "BossRush_Barrier_17", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.77f, 0.00f, 181.81f), "BossRush_Barrier_18", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 184.01f), "BossRush_Barrier_19", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 186.30f), "BossRush_Barrier_20", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 188.55f), "BossRush_Barrier_21", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 190.81f), "BossRush_Barrier_22", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.82f, 0.00f, 193.07f), "BossRush_Barrier_23", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.83f, 0.00f, 195.29f), "BossRush_Barrier_24", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.84f, 0.00f, 197.51f), "BossRush_Barrier_25", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.87f, 0.00f, 199.79f), "BossRush_Barrier_26", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 202.05f), "BossRush_Barrier_27", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 204.25f), "BossRush_Barrier_28", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.89f, 0.00f, 206.50f), "BossRush_Barrier_29", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.90f, 0.00f, 208.77f), "BossRush_Barrier_30", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.91f, 0.00f, 210.97f), "BossRush_Barrier_31", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 213.22f), "BossRush_Barrier_32", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 214.60f), "BossRush_Barrier_33", 270f));

                // 北侧围栏（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.93f, 0.00f, 215.85f), "BossRush_Barrier_34", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.17f, 0.00f, 215.86f), "BossRush_Barrier_35", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.43f, 0.00f, 216.92f), "BossRush_Barrier_36", 300f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(111.13f, 0.00f, 218.23f), "BossRush_Barrier_37", 150f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(118.65f, 0.00f, 218.73f), "BossRush_Barrier_38", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(120.92f, 0.00f, 218.72f), "BossRush_Barrier_39", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(124.35f, 0.00f, 218.83f), "BossRush_Barrier_40", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(127.81f, 0.00f, 218.77f), "BossRush_Barrier_41", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.08f, 0.00f, 218.77f), "BossRush_Barrier_42", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.32f, 0.00f, 218.76f), "BossRush_Barrier_43", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(134.58f, 0.00f, 218.78f), "BossRush_Barrier_44", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(136.80f, 0.00f, 218.79f), "BossRush_Barrier_45", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.07f, 0.00f, 218.78f), "BossRush_Barrier_46", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.35f, 0.00f, 218.79f), "BossRush_Barrier_47", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(143.62f, 0.00f, 218.79f), "BossRush_Barrier_48", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(145.88f, 0.00f, 218.81f), "BossRush_Barrier_49", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.10f, 0.00f, 218.79f), "BossRush_Barrier_50", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.38f, 0.00f, 218.69f), "BossRush_Barrier_51", 15f));

                // 东侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 214.34f), "BossRush_Barrier_52", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 212.05f), "BossRush_Barrier_53", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 209.79f), "BossRush_Barrier_54", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 207.58f), "BossRush_Barrier_55", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 205.34f), "BossRush_Barrier_56", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 203.05f), "BossRush_Barrier_57", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 200.83f), "BossRush_Barrier_58", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 198.57f), "BossRush_Barrier_59", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 196.52f), "BossRush_Barrier_60", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 194.30f), "BossRush_Barrier_61", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 192.01f), "BossRush_Barrier_62", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.06f, 0.00f, 189.82f), "BossRush_Barrier_63", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 187.61f), "BossRush_Barrier_64", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 185.38f), "BossRush_Barrier_65", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.10f, 0.00f, 183.12f), "BossRush_Barrier_66", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 180.88f), "BossRush_Barrier_67", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 178.68f), "BossRush_Barrier_68", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 176.52f), "BossRush_Barrier_69", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 174.28f), "BossRush_Barrier_70", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 172.10f), "BossRush_Barrier_71", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 169.86f), "BossRush_Barrier_72", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 167.65f), "BossRush_Barrier_73", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 165.50f), "BossRush_Barrier_74", 270f));

                // 南侧围栏补充（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.64f, 0.00f, 162.87f), "BossRush_Barrier_75", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.84f, 0.00f, 162.84f), "BossRush_Barrier_76", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(135.07f, 0.00f, 162.82f), "BossRush_Barrier_77", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(137.24f, 0.00f, 162.80f), "BossRush_Barrier_78", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.52f, 0.00f, 162.80f), "BossRush_Barrier_79", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.76f, 0.00f, 162.78f), "BossRush_Barrier_80", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(144.03f, 0.00f, 162.80f), "BossRush_Barrier_81", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(146.31f, 0.00f, 162.81f), "BossRush_Barrier_82", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.57f, 0.00f, 162.78f), "BossRush_Barrier_83", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(149.91f, 0.00f, 163.34f), "BossRush_Barrier_84", 285f));

                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_HiddenWarehouse",
                    new Vector3(108.64f, 0.02f, 213.95f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_Farm_01")
            {
                // 农场镇地图的围栏配置（使用 Prfb_Shop_Shelf_01_53 商店货架）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Zone_D1/Pfb_Store_01/Indoor/Prfb_Shop_Shelf_01_53
                // 直接父对象是 Indoor

                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.33f, 0.02f, 600.91f), "BossRush_Barrier_1", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(365.53f, 0.02f, 597.44f), "BossRush_Barrier_2", 272f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(384.55f, 0.02f, 600.93f), "BossRush_Barrier_3", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.01f, 0.02f, 589.50f), "BossRush_Barrier_4", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(419.89f, 0.02f, 582.90f), "BossRush_Barrier_5", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.07f, 0.02f, 576.10f), "BossRush_Barrier_6", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(400.42f, 0.02f, 557.33f), "BossRush_Barrier_7", 182f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.64f, 0.02f, 557.40f), "BossRush_Barrier_8", 182f));

                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_Farm_01",
                    new Vector3(355.37f, 0.02f, 589.19f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_JLab_1")
            {
                // J-Lab 实验室地图的围栏配置（使用 Pfb_JLABContainer_13 集装箱）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Center_01/Group/Pfb_JLABContainer_13

                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-94.99f, 0.00f, -56.06f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-80.70f, 0.00f, -44.28f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-65.28f, 1.01f, -17.18f), "BossRush_Barrier_3", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-11.35f, 0.00f, -16.58f), "BossRush_Barrier_4", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.02f, 0.00f, -49.92f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.06f, 0.00f, -54.86f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(5.85f, 0.00f, -56.93f), "BossRush_Barrier_7", 330f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-24.20f, 0.00f, -73.11f), "BossRush_Barrier_8", 270f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-54.28f, 0.00f, -63.48f), "BossRush_Barrier_9", 270f));

                // 撤离点 - 复制场景中的 ExitNoSmoke_1 到指定位置（无烟雾版本，使用 CapsuleCollider）
                configs.Add(new MapObjectCloneConfig(
                    "ExitNoSmoke_1",
                    "Exits",
                    new Vector3(-90.76f, 0.02f, -56.25f),
                    "BossRush_Exit_JLab"
                ));
            }
            else if (sceneName == "Level_StormZone_B0")
            {
                // 风暴区地下地图的围栏配置（使用 Pfb_BarbedWire_01_03 铁丝网）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Boss/BarbedWire_Line/Pfb_BarbedWire_01_03

                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(102.76f, 0.09f, 454.12f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(105.05f, 0.00f, 454.16f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(107.33f, 0.00f, 454.09f), "BossRush_Barrier_3", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(109.64f, 0.00f, 454.07f), "BossRush_Barrier_4", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(111.90f, 0.00f, 454.12f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(114.18f, 0.00f, 454.09f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(116.46f, 0.00f, 454.04f), "BossRush_Barrier_7", 345f));

                // 撤离点使用 CreateBossRushExit 方法创建（不再复制模板）
            }
            else if (sceneName == "Level_SnowMilitaryBase")
            {
                // 37号实验区地图的围栏配置（使用 Pfb_Car_02 汽车作为障碍物）
                // 模板路径: Pfb_MilitaryBase/Indoor/Pfb_Car_02

                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(471.95f, -0.01f, 525.12f), "BossRush_Barrier_1", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(477.32f, -0.01f, 523.98f), "BossRush_Barrier_2", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(520.18f, -0.01f, 537.38f), "BossRush_Barrier_3", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(521.07f, -0.01f, 541.55f), "BossRush_Barrier_4", 261f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(520.36f, -0.01f, 562.45f), "BossRush_Barrier_5", 261f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(494.75f, 0.75f, 576.29f), "BossRush_Barrier_6", 291f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(476.92f, 0.02f, 574.87f), "BossRush_Barrier_7", 276f));
            }
            else if (sceneName == "Level_SnowMilitaryBase_ColdStorage")
            {
                // 迷宫地图的围栏配置（使用 Pfb_RoadblockGRP_3 路障组合）
                // 模板路径: Pfb_RoadblockGRP_3/Col_Wall_FowBlock_1
                // 根对象: Pfb_RoadblockGRP_3（场景根级对象，parentNamePrefix 留空）

                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(-11.28f, 0.00f, -34.97f), "BossRush_Barrier_1", 272f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(-11.13f, 0.00f, -30.67f), "BossRush_Barrier_2", 272f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(7.31f, 0.00f, -65.16f), "BossRush_Barrier_3", 182f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(11.50f, 0.00f, -65.29f), "BossRush_Barrier_4", 182f));
            }
            // 后续可以添加其他地图的配置

            return configs;
        }

        /// <summary>
        /// 使用游戏原生 exitPrefab 创建 BossRush 撤离点
        /// </summary>
        private void CreateBossRushExit(Vector3 position, string exitName)
        {
            try
            {
                // 方案1：使用 LevelManager.ExitCreator.exitPrefab（最优方案，直接使用游戏原生预制体）
                if (LevelManager.Instance != null && LevelManager.Instance.ExitCreator != null)
                {
                    GameObject exitPrefab = LevelManager.Instance.ExitCreator.exitPrefab;
                    if (exitPrefab != null)
                    {
                        GameObject exit = UnityEngine.Object.Instantiate(exitPrefab, position, Quaternion.identity);
                        exit.name = exitName;
                        exit.SetActive(true);

                        // 确保 CountDownArea 启用
                        CountDownArea countDown = exit.GetComponent<CountDownArea>();
                        if (countDown != null)
                        {
                            countDown.enabled = true;
                        }

                        // 禁用烟雾/粒子效果（室内场景不需要）
                        DisableExitSmokeEffects(exit);

                        ModBehaviour.DevLog("[BossRush] 使用 exitPrefab 创建撤离点: " + exitName + " 位置: " + position);
                        return;
                    }
                }

                // 方案2：从头创建一个简单的撤离点（无烟雾效果）
                // 跳过 FindObjectsOfType 遍历，直接创建简单撤离点，性能更优
                CreateSimpleExit(position, exitName);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] CreateBossRushExit 失败: " + e.Message);
                // 回退到简单撤离点
                CreateSimpleExit(position, exitName);
            }
        }

        /// <summary>
        /// 禁用撤离点的烟雾/粒子效果（用于室内场景）
        /// [性能优化] 使用字符串缓存避免重复 ToLower 调用
        /// </summary>
        private void DisableExitSmokeEffects(GameObject exit)
        {
            try
            {
                int disabledCount = 0;

                // 查找并禁用名称包含烟雾/粒子相关关键词的子对象
                foreach (Transform child in exit.GetComponentsInChildren<Transform>(true))
                {
                    if (child == exit.transform) continue;  // 跳过根对象

                    // [性能优化] 使用 IndexOf 替代 Contains + ToLower，减少字符串分配
                    string name = child.name;
                    if (name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("fog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("particle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("effect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        child.gameObject.SetActive(false);
                        disabledCount++;
                    }
                }

                if (disabledCount > 0)
                {
                    ModBehaviour.DevLog("[BossRush] 已禁用撤离点烟雾效果，禁用子对象数: " + disabledCount);
                }
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "DisableExitSmokeEffects",
                    "禁用撤离点烟雾效果失败",
                    e);
            }
        }

        /// <summary>
        /// 从头创建一个简单的撤离点（当无法获取预制体时使用）
        /// </summary>
        private void CreateSimpleExit(Vector3 position, string exitName)
        {
            try
            {
                // 创建撤离点 GameObject
                GameObject exit = new GameObject(exitName);
                exit.transform.position = position;

                // 添加触发器 Collider
                BoxCollider collider = exit.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(3f, 2f, 3f);  // 3x2x3 的触发区域
                collider.center = new Vector3(0f, 1f, 0f);  // 中心稍微抬高

                // 添加 CountDownArea 组件
                CountDownArea countDown = exit.AddComponent<CountDownArea>();

                // 通过反射设置 requiredExtrationTime（私有字段）
                try
                {
                    System.Reflection.FieldInfo timeField = typeof(CountDownArea).GetField("requiredExtrationTime",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (timeField != null)
                    {
                        timeField.SetValue(countDown, 5f);  // 5秒撤离时间
                    }
                }
                catch (Exception e)
                {
                    LogIntegrationWarningLimited(
                        "CreateSimpleExit_requiredExtractionTime",
                        "设置简易撤离点倒计时失败",
                        e);
                }

                // 订阅撤离成功事件
                countDown.onCountDownSucceed = new UnityEngine.Events.UnityEvent();
                countDown.onCountDownSucceed.AddListener(() => {
                    ModBehaviour.DevLog("[BossRush] 撤离成功！");
                    // 触发游戏的撤离逻辑
                    try
                    {
                        if (LevelManager.Instance != null)
                        {
                            // 创建撤离信息并通知
                            EvacuationInfo info = new EvacuationInfo(
                                Duckov.Scenes.MultiSceneCore.ActiveSubSceneID,
                                position
                            );
                            LevelManager.Instance.NotifyEvacuated(info);
                        }
                    }
                    catch (System.Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] 调用 NotifyEvacuated 失败: " + e.Message);
                    }
                });

                // 订阅倒计时开始/停止事件（显示UI）
                countDown.onCountDownStarted = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStarted.AddListener((area) => {
                    EvacuationCountdownUI.Request(area);
                });

                countDown.onCountDownStopped = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStopped.AddListener((area) => {
                    EvacuationCountdownUI.Release(area);
                });

                // 室内场景不创建视觉指示器（绿色光柱）

                ModBehaviour.DevLog("[BossRush] 创建简单撤离点: " + exitName + " 位置: " + position);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] CreateSimpleExit 失败: " + e.Message);
            }
        }
    }
}
