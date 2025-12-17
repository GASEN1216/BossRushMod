// ============================================================================
// Config.cs - BossRush 配置系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的配置项，支持：
//   - 本地文件配置（BossRushModConfig.txt）
//   - ModConfig 模组动态配置（如果已安装）
//   
// 配置项：
//   - waveIntervalSeconds: 波次间休息时间（2-60秒，默认15秒）
//   - enableRandomBossLoot: Boss 掉落随机化（时间加成）
//   - useInteractBetweenWaves: 波次间使用交互点开启下一波
//   - lootBoxBlocksBullets: Boss 掉落箱作为掩体（挡子弹）
//   - infiniteHellBossesPerWave: 无间炼狱每波 Boss 数量（1-10，默认3）
//   - bossStatMultiplier: Boss 全局数值倍率（0.1-10，默认1）
//   - modeDEnemiesPerWave: 白手起家每波敌人数（1-10，默认3）
//   - disabledBosses: 被禁用的 Boss 名称列表（用于 Boss 池筛选）
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// BossRush 配置系统模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 配置数据结构
        
        /// <summary>
        /// BossRush 配置数据类
        /// </summary>
        [Serializable]
        private class BossRushConfig
        {
            public float waveIntervalSeconds = 15f;
            public bool enableRandomBossLoot = true;
            public bool useInteractBetweenWaves = false;
            public bool lootBoxBlocksBullets = false;
            public int infiniteHellBossesPerWave = 3;
            public float bossStatMultiplier = 1f;

            /// <summary>白手起家每波敌人数（1-10，默认3）</summary>
            public int modeDEnemiesPerWave = 3;

            /// <summary>被禁用的 Boss 名称列表（用于 Boss 池筛选）</summary>
            public List<string> disabledBosses = new List<string>();
        }
        
        #endregion
        
        #region 配置字段
        
        /// <summary>模组名称（用于 ModConfig 注册）</summary>
        private const string ModName = "BossRush";
        
        /// <summary>当前配置实例</summary>
        private BossRushConfig config = new BossRushConfig();
        
        #endregion
        
        #region 配置文件路径

        /// <summary>
        /// 配置文件路径（StreamingAssets/BossRushModConfig.txt）
        /// </summary>
        private static string ConfigFilePath
        {
            get
            {
                try
                {
                    return Path.Combine(Application.streamingAssetsPath, "BossRushModConfig.txt");
                }
                catch
                {
                    return null;
                }
            }
        }
        
        #endregion
        
        #region 配置加载与保存

        /// <summary>
        /// 通过反射查找 ModConfig 类型
        /// </summary>
        /// <param name="typeName">类型全名</param>
        /// <returns>找到的类型，未找到返回 null</returns>
        private Type FindModConfigType(string typeName)
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in assemblies)
                {
                    try
                    {
                        Type type = assembly.GetType(typeName);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从本地文件加载配置
        /// </summary>
        private void LoadConfigFromFile()
        {
            try
            {
                string path = ConfigFilePath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json))
                    {
                        BossRushConfig loaded = JsonUtility.FromJson<BossRushConfig>(json);
                        if (loaded != null)
                        {
                            if (loaded.waveIntervalSeconds <= 0f)
                            {
                                loaded.waveIntervalSeconds = 15f;
                            }
                            config = loaded;
                        }
                    }
                }
                else
                {
                    SaveConfigToFile();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 保存配置到本地文件
        /// </summary>
        private void SaveConfigToFile()
        {
            try
            {
                string path = ConfigFilePath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (config == null)
                {
                    config = new BossRushConfig();
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 从 ModConfig 模组加载配置
        /// </summary>
        private void LoadConfigFromModConfig()
        {
            try
            {
                Type optionsManagerType = FindModConfigType("ModConfig.OptionsManager_Mod");
                if (optionsManagerType == null)
                {
                    DevLog("[BossRush] ModConfig.OptionsManager_Mod 类型未找到");
                    return;
                }
                
                DevLog("[BossRush] 找到 ModConfig.OptionsManager_Mod 类型，开始加载配置");

                if (config == null)
                {
                    config = new BossRushConfig();
                }

                float currentWave = config.waveIntervalSeconds;
                string waveKey = ModName + "_waveIntervalSeconds";
                string lootKey = ModName + "_EnableRandomBossLoot";
                string interactKey = ModName + "_UseInteractBetweenWaves";
                string coverKey = ModName + "_LootBoxBlocksBullets";
                string hellBossKey = ModName + "_InfiniteHellBossesPerWave";
                string bossStatKey = ModName + "_BossStatMultiplier";

                MethodInfo loadMethod = optionsManagerType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
                if (loadMethod != null)
                {
                    MethodInfo floatLoadMethod = loadMethod.MakeGenericMethod(typeof(float));
                    object waveResult = floatLoadMethod.Invoke(null, new object[] { waveKey, currentWave });
                    float loadedWave = (float)waveResult;

                    if (loadedWave <= 0f)
                    {
                        loadedWave = 15f;
                    }

                    if (loadedWave < 2f)
                    {
                        loadedWave = 2f;
                    }

                    if (loadedWave > 60f)
                    {
                        loadedWave = 60f;
                    }

                    config.waveIntervalSeconds = loadedWave;

                    MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                    object lootResult = boolLoadMethod.Invoke(null, new object[] { lootKey, config.enableRandomBossLoot });
                    bool loadedLoot = (bool)lootResult;
                    config.enableRandomBossLoot = loadedLoot;

                    object interactResult = boolLoadMethod.Invoke(null, new object[] { interactKey, config.useInteractBetweenWaves });
                    bool loadedInteract = (bool)interactResult;
                    config.useInteractBetweenWaves = loadedInteract;

                    object coverResult = boolLoadMethod.Invoke(null, new object[] { coverKey, config.lootBoxBlocksBullets });
                    bool loadedCover = (bool)coverResult;
                    config.lootBoxBlocksBullets = loadedCover;

                    MethodInfo intLoadMethod = loadMethod.MakeGenericMethod(typeof(int));
                    int currentHell = (config != null) ? config.infiniteHellBossesPerWave : 3;
                    object hellResult = intLoadMethod.Invoke(null, new object[] { hellBossKey, currentHell });
                    int loadedHell = (int)hellResult;
                    if (loadedHell < 1)
                    {
                        loadedHell = 1;
                    }
                    if (loadedHell > 10)
                    {
                        loadedHell = 10;
                    }
                    config.infiniteHellBossesPerWave = loadedHell;

                    float currentBossStat = (config != null) ? config.bossStatMultiplier : 1f;
                    object bossStatResult = floatLoadMethod.Invoke(null, new object[] { bossStatKey, currentBossStat });
                    float loadedBossStat = (float)bossStatResult;
                    if (loadedBossStat < 0.1f)
                    {
                        loadedBossStat = 0.1f;
                    }
                    if (loadedBossStat > 10f)
                    {
                        loadedBossStat = 10f;
                    }
                    config.bossStatMultiplier = loadedBossStat;

                    // 加载 Mode D 每波敌人数
                    string modeDKey = ModName + "_ModeDEnemiesPerWave";
                    int currentModeD = (config != null) ? config.modeDEnemiesPerWave : 3;
                    object modeDResult = intLoadMethod.Invoke(null, new object[] { modeDKey, currentModeD });
                    int loadedModeD = (int)modeDResult;
                    if (loadedModeD < 1)
                    {
                        loadedModeD = 1;
                    }
                    if (loadedModeD > 10)
                    {
                        loadedModeD = 10;
                    }
                    config.modeDEnemiesPerWave = loadedModeD;

                    DevLog("[BossRush] 从 ModConfig 加载配置: waveIntervalSeconds=" + loadedWave + ", enableRandomBossLoot=" + loadedLoot + ", useInteractBetweenWaves=" + loadedInteract + ", lootBoxBlocksBullets=" + loadedCover + ", infiniteHellBossesPerWave=" + loadedHell + ", bossStatMultiplier=" + loadedBossStat + ", modeDEnemiesPerWave=" + loadedModeD);
                }
                else
                {
                    Debug.LogWarning("[BossRush] 未找到 Load 方法");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossRush] LoadConfigFromModConfig 失败: " + ex.Message);
            }
        }

        #endregion
        
        #region ModConfig 事件处理
        
        /// <summary>
        /// ModConfig 配置变更事件处理
        /// </summary>
        /// <param name="changedKey">变更的配置键</param>
        private void OnModConfigOptionsChanged(string changedKey)
        {
            try
            {
                string waveKey = ModName + "_waveIntervalSeconds";
                string lootKey = ModName + "_EnableRandomBossLoot";
                string interactKey = ModName + "_UseInteractBetweenWaves";
                string coverKey = ModName + "_LootBoxBlocksBullets";
                string hellBossKey = ModName + "_InfiniteHellBossesPerWave";
                string bossStatKey = ModName + "_BossStatMultiplier";
                string modeDKey = ModName + "_ModeDEnemiesPerWave";
                
                if (changedKey == waveKey || changedKey == lootKey || changedKey == interactKey || changedKey == coverKey || changedKey == hellBossKey || changedKey == bossStatKey || changedKey == modeDKey)
                {
                    DevLog("[BossRush] 检测到配置变更: " + changedKey);
                    LoadConfigFromModConfig();
                    SaveConfigToFile();
                    DevLog("[BossRush] 配置已更新并保存到本地文件");

                    // 如果在下一波倒计时过程中修改了波次间隔，重启倒计时以使用新配置
                    if (changedKey == waveKey && waitingForNextWave)
                    {
                        DevLog("[BossRush] 波次间隔配置在倒计时过程中被修改，重启下一波倒计时");
                        StartNextWaveCountdown();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossRush] OnModConfigOptionsChanged 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 设置 ModConfig 配置项（注册到 ModConfig 模组）
        /// </summary>
        private void SetupModConfig()
        {
            try
            {
                Type modBehaviourType = FindModConfigType("ModConfig.ModBehaviour");
                if (modBehaviourType == null)
                {
                    DevLog("[BossRush] ModConfig.ModBehaviour 类型未找到，ModConfig 可能未安装");
                    return;
                }
                
                DevLog("[BossRush] 找到 ModConfig.ModBehaviour 类型，开始注册配置项");
                
                MethodInfo addDelegateMethod = modBehaviourType.GetMethod("AddOnOptionsChangedDelegate", BindingFlags.Public | BindingFlags.Static);
                if (addDelegateMethod != null)
                {
                    Action<string> handler = new Action<string>(OnModConfigOptionsChanged);
                    addDelegateMethod.Invoke(null, new object[] { handler });
                    DevLog("[BossRush] 已注册配置变更事件监听");
                }

                float value = 15f;
                if (config != null)
                {
                    value = config.waveIntervalSeconds;
                }

                if (value < 2f)
                {
                    value = 2f;
                }

                if (value > 60f)
                {
                    value = 60f;
                }

                if (config == null)
                {
                    config = new BossRushConfig();
                }

                config.waveIntervalSeconds = value;

                // 使用 L10n 工具类进行本地化

                // 1) 原有：波次间休息时间滑条
                string label = L10n.T("波次间休息时间(秒)", "Wave Interval (seconds)");
                string key = ModName + "_waveIntervalSeconds";
                Vector2 range = new Vector2(2f, 60f);
                
                MethodInfo addSliderMethod = modBehaviourType.GetMethod("AddInputWithSlider", BindingFlags.Public | BindingFlags.Static);
                if (addSliderMethod != null)
                {
                    DevLog("[BossRush] 尝试注册配置项: key=" + key + ", value=" + value);
                    addSliderMethod.Invoke(null, new object[] { ModName, key, label, typeof(float), value, range });
                    DevLog("[BossRush] 配置项注册成功");
                }
                else
                {
                    Debug.LogWarning("[BossRush] 未找到 AddInputWithSlider 方法");
                }

                // 1b) 新增：无间炼狱每波 Boss 数量
                try
                {
                    int hellValue = (config != null) ? config.infiniteHellBossesPerWave : 3;
                    if (hellValue < 1)
                    {
                        hellValue = 1;
                    }
                    if (hellValue > 10)
                    {
                        hellValue = 10;
                    }
                    if (config != null)
                    {
                        config.infiniteHellBossesPerWave = hellValue;
                    }

                    string hellLabel = L10n.T("无间炼狱：每波Boss数量", "Infinite Hell: bosses per wave");
                    string hellKey = ModName + "_InfiniteHellBossesPerWave";
                    Vector2 hellRange = new Vector2(1f, 10f);

                    if (addSliderMethod != null)
                    {
                        DevLog("[BossRush] 尝试注册无间炼狱 Boss 数配置项: key=" + hellKey + ", value=" + hellValue);
                        addSliderMethod.Invoke(null, new object[] { ModName, hellKey, hellLabel, typeof(int), hellValue, hellRange });
                        DevLog("[BossRush] 无间炼狱 Boss 数配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册无间炼狱 Boss 数配置项失败: " + ex.Message);
                }

                // 2) 新增：Boss 掉落随机化（时间加成）布尔下拉
                try
                {
                    string lootLabel = L10n.T("Boss 掉落随机化（时间加成）", "Boss loot randomization (time bonus)");
                    string lootKey = ModName + "_EnableRandomBossLoot";

                    MethodInfo addBoolMethod = modBehaviourType.GetMethod("AddBoolDropdownList", BindingFlags.Public | BindingFlags.Static);
                    if (addBoolMethod != null)
                    {
                        bool defaultLootValue = config != null && config.enableRandomBossLoot;
                        DevLog("[BossRush] 尝试注册随机掉落配置项: key=" + lootKey + ", value=" + defaultLootValue);
                        addBoolMethod.Invoke(null, new object[] { ModName, lootKey, lootLabel, defaultLootValue });
                        DevLog("[BossRush] 随机掉落配置项注册成功");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 AddBoolDropdownList 方法，随机掉落配置项不会显示在 ModConfig 中");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册随机掉落配置项失败: " + ex.Message);
                }

                // 3) 新增：Boss 掉落箱是否作为掩体（挡子弹）
                try
                {
                    string coverLabel = L10n.T("Boss 掉落箱作为掩体（挡子弹）", "Boss loot box blocks bullets");
                    string coverKey = ModName + "_LootBoxBlocksBullets";

                    MethodInfo addBoolMethodCover = modBehaviourType.GetMethod("AddBoolDropdownList", BindingFlags.Public | BindingFlags.Static);
                    if (addBoolMethodCover != null)
                    {
                        bool defaultCoverValue = config != null && config.lootBoxBlocksBullets;
                        DevLog("[BossRush] 尝试注册掉落箱掩体配置项: key=" + coverKey + ", value=" + defaultCoverValue);
                        addBoolMethodCover.Invoke(null, new object[] { ModName, coverKey, coverLabel, defaultCoverValue });
                        DevLog("[BossRush] 掉落箱掩体配置项注册成功");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 AddBoolDropdownList 方法，掉落箱掩体配置项不会显示在 ModConfig 中");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册掉落箱掩体配置项失败: " + ex.Message);
                }

                // 4) 新增：波次间是否需要交互点开下一波
                try
                {
                    string interactLabel = L10n.T("波次间使用交互点开启下一波", "Use interact point between waves");
                    string interactKey = ModName + "_UseInteractBetweenWaves";

                    MethodInfo addBoolMethod2 = modBehaviourType.GetMethod("AddBoolDropdownList", BindingFlags.Public | BindingFlags.Static);
                    if (addBoolMethod2 != null)
                    {
                        bool defaultInteractValue = config != null && config.useInteractBetweenWaves;
                        DevLog("[BossRush] 尝试注册波次交互配置项: key=" + interactKey + ", value=" + defaultInteractValue);
                        addBoolMethod2.Invoke(null, new object[] { ModName, interactKey, interactLabel, defaultInteractValue });
                        DevLog("[BossRush] 波次交互配置项注册成功");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 AddBoolDropdownList 方法，波次交互配置项不会显示在 ModConfig 中");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册波次交互配置项失败: " + ex.Message);
                }
                try
                {
                    float bossStatValue = (config != null) ? config.bossStatMultiplier : 1f;
                    if (bossStatValue < 0.1f)
                    {
                        bossStatValue = 0.1f;
                    }
                    if (bossStatValue > 10f)
                    {
                        bossStatValue = 10f;
                    }
                    if (config != null)
                    {
                        config.bossStatMultiplier = bossStatValue;
                    }

                    string bossStatLabel = L10n.T("Boss全局数值倍率", "Boss global stat multiplier");
                    string bossStatKey = ModName + "_BossStatMultiplier";
                    Vector2 bossStatRange = new Vector2(0.1f, 10f);

                    MethodInfo addSliderMethodBoss = modBehaviourType.GetMethod("AddInputWithSlider", BindingFlags.Public | BindingFlags.Static);
                    if (addSliderMethodBoss != null)
                    {
                        DevLog("[BossRush] 尝试注册Boss数值倍率配置项: key=" + bossStatKey + ", value=" + bossStatValue);
                        addSliderMethodBoss.Invoke(null, new object[] { ModName, bossStatKey, bossStatLabel, typeof(float), bossStatValue, bossStatRange });
                        DevLog("[BossRush] Boss数值倍率配置项注册成功");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 AddInputWithSlider 方法，Boss数值倍率配置项不会显示在 ModConfig 中");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册Boss数值倍率配置项失败: " + ex.Message);
                }

                // 注册 Mode D 每波敌人数配置项
                try
                {
                    int modeDValue = 3;
                    if (config != null)
                    {
                        modeDValue = config.modeDEnemiesPerWave;
                    }

                    string modeDLabel = L10n.T("白手起家每波敌人数", "Rags to Riches enemies per wave");
                    string modeDKey = ModName + "_ModeDEnemiesPerWave";
                    Vector2 modeDRange = new Vector2(1f, 10f);

                    MethodInfo addSliderMethodModeD = modBehaviourType.GetMethod("AddInputWithSlider", BindingFlags.Public | BindingFlags.Static);
                    if (addSliderMethodModeD != null)
                    {
                        DevLog("[BossRush] 尝试注册Mode D每波敌人数配置项: key=" + modeDKey + ", value=" + modeDValue);
                        addSliderMethodModeD.Invoke(null, new object[] { ModName, modeDKey, modeDLabel, typeof(int), modeDValue, modeDRange });
                        DevLog("[BossRush] Mode D每波敌人数配置项注册成功");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 AddInputWithSlider 方法，Mode D每波敌人数配置项不会显示在 ModConfig 中");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BossRush] 注册Mode D每波敌人数配置项失败: " + ex.Message);
                }

            }
            catch (Exception ex)
            {
                Debug.LogError("[BossRush] SetupModConfig 失败: " + ex.Message);
                Debug.LogError("[BossRush] 堆栈: " + ex.StackTrace);
            }
        }

        #endregion
        
        #region 配置访问方法
        
        /// <summary>
        /// 获取波次间隔时间（秒）
        /// </summary>
        /// <returns>波次间隔时间，范围 2-60 秒</returns>
        private float GetWaveIntervalSeconds()
        {
            float value = 15f;
            if (config != null)
            {
                value = config.waveIntervalSeconds;
            }

            if (value < 2f)
            {
                value = 2f;
            }

            if (value > 60f)
            {
                value = 60f;
            }

            return value;
        }
        
        #endregion
    }
}
