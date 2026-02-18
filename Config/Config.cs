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

            /// <summary>龙套装冲刺功能开关（默认开启）</summary>
            public bool enableDragonDash = true;

            /// <summary>成就界面快捷键（默认L键，索引对应KeyCode枚举）</summary>
            public int achievementHotkey = (int)UnityEngine.KeyCode.L;

            /// <summary>荒野号角使用狼模型替换坐骑（默认开启）</summary>
            public bool useWolfModelForWildHorn = true;
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

                    // 加载龙套装冲刺开关
                    string dragonDashKey = ModName + "_EnableDragonDash";
                    object dragonDashResult = boolLoadMethod.Invoke(null, new object[] { dragonDashKey, config.enableDragonDash });
                    bool loadedDragonDash = (bool)dragonDashResult;
                    config.enableDragonDash = loadedDragonDash;

                    // 加载成就界面快捷键
                    string achievementHotkeyKey = ModName + "_AchievementHotkey";
                    int currentHotkey = (config != null) ? config.achievementHotkey : (int)UnityEngine.KeyCode.L;
                    object hotkeyResult = intLoadMethod.Invoke(null, new object[] { achievementHotkeyKey, currentHotkey });
                    int loadedHotkey = (int)hotkeyResult;
                    config.achievementHotkey = loadedHotkey;

                    // 加载荒野号角狼模型开关
                    string wolfModelKey = ModName + "_UseWolfModelForWildHorn";
                    object wolfModelResult = boolLoadMethod.Invoke(null, new object[] { wolfModelKey, config.useWolfModelForWildHorn });
                    bool loadedWolfModel = (bool)wolfModelResult;
                    config.useWolfModelForWildHorn = loadedWolfModel;

                    DevLog("[BossRush] 从 ModConfig 加载配置: waveIntervalSeconds=" + loadedWave + ", enableRandomBossLoot=" + loadedLoot + ", useInteractBetweenWaves=" + loadedInteract + ", lootBoxBlocksBullets=" + loadedCover + ", infiniteHellBossesPerWave=" + loadedHell + ", bossStatMultiplier=" + loadedBossStat + ", modeDEnemiesPerWave=" + loadedModeD + ", enableDragonDash=" + loadedDragonDash + ", achievementHotkey=" + loadedHotkey + ", useWolfModelForWildHorn=" + loadedWolfModel);
                }
                else
                {
                    DevLog("[BossRush] 未找到 Load 方法");
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] LoadConfigFromModConfig 失败: " + ex.Message);
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
                string achievementHotkeyKey = ModName + "_AchievementHotkey";
                
                if (changedKey == waveKey || changedKey == lootKey || changedKey == interactKey || changedKey == coverKey || changedKey == hellBossKey || changedKey == bossStatKey || changedKey == modeDKey || changedKey == achievementHotkeyKey)
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

                // 龙套装冲刺开关
                string dragonDashKey = ModName + "_EnableDragonDash";
                if (changedKey == dragonDashKey)
                {
                    DevLog("[BossRush] 检测到龙套装冲刺配置变更");
                    LoadConfigFromModConfig();
                    SaveConfigToFile();
                }

                // 荒野号角狼模型开关
                string wolfModelKey = ModName + "_UseWolfModelForWildHorn";
                if (changedKey == wolfModelKey)
                {
                    DevLog("[BossRush] 检测到荒野号角狼模型配置变更");
                    LoadConfigFromModConfig();
                    SaveConfigToFile();
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] OnModConfigOptionsChanged 失败: " + ex.Message);
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
                
                // 注册配置变更事件监听
                MethodInfo addDelegateMethod = modBehaviourType.GetMethod("AddOnOptionsChangedDelegate", BindingFlags.Public | BindingFlags.Static);
                if (addDelegateMethod != null)
                {
                    Action<string> handler = new Action<string>(OnModConfigOptionsChanged);
                    addDelegateMethod.Invoke(null, new object[] { handler });
                    DevLog("[BossRush] 已注册配置变更事件监听");
                }

                if (config == null)
                {
                    config = new BossRushConfig();
                }

                MethodInfo addSliderMethod = modBehaviourType.GetMethod("AddInputWithSlider", BindingFlags.Public | BindingFlags.Static);
                MethodInfo addBoolMethod = modBehaviourType.GetMethod("AddBoolDropdownList", BindingFlags.Public | BindingFlags.Static);

                // ========== 开关类配置 ==========
                
                // Boss 掉落随机化（时间加成）
                try
                {
                    string lootLabel = L10n.T("Boss掉落随机化（时间加成）", "Boss loot randomization (time bonus)");
                    string lootKey = ModName + "_EnableRandomBossLoot";
                    
                    if (addBoolMethod != null)
                    {
                        addBoolMethod.Invoke(null, new object[] { ModName, lootKey, lootLabel, config.enableRandomBossLoot });
                        DevLog("[BossRush] 随机掉落配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册随机掉落配置项失败: " + ex.Message);
                }

                // Boss 掉落箱作为掩体
                try
                {
                    string coverLabel = L10n.T("Boss掉落箱作为掩体（挡子弹）", "Boss loot box blocks bullets");
                    string coverKey = ModName + "_LootBoxBlocksBullets";
                    
                    if (addBoolMethod != null)
                    {
                        addBoolMethod.Invoke(null, new object[] { ModName, coverKey, coverLabel, config.lootBoxBlocksBullets });
                        DevLog("[BossRush] 掉落箱掩体配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册掉落箱掩体配置项失败: " + ex.Message);
                }

                // 波次间使用交互点开启下一波
                try
                {
                    string interactLabel = L10n.T("波次间使用交互点开启下一波", "Use interact point between waves");
                    string interactKey = ModName + "_UseInteractBetweenWaves";
                    
                    if (addBoolMethod != null)
                    {
                        addBoolMethod.Invoke(null, new object[] { ModName, interactKey, interactLabel, config.useInteractBetweenWaves });
                        DevLog("[BossRush] 波次交互配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册波次交互配置项失败: " + ex.Message);
                }

                // 龙套装：双击冲刺
                try
                {
                    string dragonDashLabel = L10n.T("龙套装：双击冲刺", "Dragon Set: Double-tap Dash");
                    string dragonDashKey = ModName + "_EnableDragonDash";
                    
                    if (addBoolMethod != null)
                    {
                        addBoolMethod.Invoke(null, new object[] { ModName, dragonDashKey, dragonDashLabel, config.enableDragonDash });
                        DevLog("[BossRush] 龙套装冲刺配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册龙套装冲刺配置项失败: " + ex.Message);
                }

                // 荒野号角使用狼模型
                try
                {
                    string wolfModelLabel = L10n.T("荒野号角：使用狼模型", "Wild Horn: Use Wolf Model");
                    string wolfModelKey = ModName + "_UseWolfModelForWildHorn";
                    
                    if (addBoolMethod != null)
                    {
                        addBoolMethod.Invoke(null, new object[] { ModName, wolfModelKey, wolfModelLabel, config.useWolfModelForWildHorn });
                        DevLog("[BossRush] 荒野号角狼模型配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册荒野号角狼模型配置项失败: " + ex.Message);
                }

                // ========== 数值滑条类配置 ==========
                
                // 波次间休息时间
                try
                {
                    float waveValue = Mathf.Clamp(config.waveIntervalSeconds, 2f, 60f);
                    config.waveIntervalSeconds = waveValue;
                    
                    string waveLabel = L10n.T("波次间休息时间(秒)", "Wave Interval (seconds)");
                    string waveKey = ModName + "_waveIntervalSeconds";
                    
                    if (addSliderMethod != null)
                    {
                        addSliderMethod.Invoke(null, new object[] { ModName, waveKey, waveLabel, typeof(float), waveValue, new Vector2(2f, 60f) });
                        DevLog("[BossRush] 波次间隔配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册波次间隔配置项失败: " + ex.Message);
                }

                // Boss 全局数值倍率
                try
                {
                    float bossStatValue = Mathf.Clamp(config.bossStatMultiplier, 0.1f, 10f);
                    config.bossStatMultiplier = bossStatValue;
                    
                    string bossStatLabel = L10n.T("Boss全局数值倍率", "Boss global stat multiplier");
                    string bossStatKey = ModName + "_BossStatMultiplier";
                    
                    if (addSliderMethod != null)
                    {
                        addSliderMethod.Invoke(null, new object[] { ModName, bossStatKey, bossStatLabel, typeof(float), bossStatValue, new Vector2(0.1f, 10f) });
                        DevLog("[BossRush] Boss数值倍率配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册Boss数值倍率配置项失败: " + ex.Message);
                }

                // 无间炼狱每波 Boss 数量
                try
                {
                    int hellValue = Mathf.Clamp(config.infiniteHellBossesPerWave, 1, 10);
                    config.infiniteHellBossesPerWave = hellValue;
                    
                    string hellLabel = L10n.T("无间炼狱：每波Boss数量", "Infinite Hell: bosses per wave");
                    string hellKey = ModName + "_InfiniteHellBossesPerWave";
                    
                    if (addSliderMethod != null)
                    {
                        addSliderMethod.Invoke(null, new object[] { ModName, hellKey, hellLabel, typeof(int), hellValue, new Vector2(1f, 10f) });
                        DevLog("[BossRush] 无间炼狱Boss数配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册无间炼狱Boss数配置项失败: " + ex.Message);
                }

                // 白手起家每波敌人数
                try
                {
                    int modeDValue = Mathf.Clamp(config.modeDEnemiesPerWave, 1, 10);
                    config.modeDEnemiesPerWave = modeDValue;
                    
                    string modeDLabel = L10n.T("白手起家：每波敌人数", "Rags to Riches: enemies per wave");
                    string modeDKey = ModName + "_ModeDEnemiesPerWave";
                    
                    if (addSliderMethod != null)
                    {
                        addSliderMethod.Invoke(null, new object[] { ModName, modeDKey, modeDLabel, typeof(int), modeDValue, new Vector2(1f, 10f) });
                        DevLog("[BossRush] 白手起家敌人数配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册白手起家敌人数配置项失败: " + ex.Message);
                }

                // ========== 按键配置 ==========
                
                // 成就界面快捷键
                try
                {
                    MethodInfo addDropdownMethod = modBehaviourType.GetMethod("AddDropdownList", BindingFlags.Public | BindingFlags.Static);
                    if (addDropdownMethod != null)
                    {
                        string hotkeyLabel = L10n.T("成就界面快捷键", "Achievement Hotkey");
                        string hotkeyKey = ModName + "_AchievementHotkey";
                        
                        // 创建按键选项列表
                        var hotkeyOptions = new System.Collections.Generic.SortedDictionary<string, object>();
                        hotkeyOptions.Add("L", (int)UnityEngine.KeyCode.L);
                        hotkeyOptions.Add("K", (int)UnityEngine.KeyCode.K);
                        hotkeyOptions.Add("J", (int)UnityEngine.KeyCode.J);
                        hotkeyOptions.Add("H", (int)UnityEngine.KeyCode.H);
                        hotkeyOptions.Add("G", (int)UnityEngine.KeyCode.G);
                        hotkeyOptions.Add("Y", (int)UnityEngine.KeyCode.Y);
                        hotkeyOptions.Add("U", (int)UnityEngine.KeyCode.U);
                        hotkeyOptions.Add("O", (int)UnityEngine.KeyCode.O);
                        hotkeyOptions.Add("P", (int)UnityEngine.KeyCode.P);
                        hotkeyOptions.Add("F5", (int)UnityEngine.KeyCode.F5);
                        hotkeyOptions.Add("F6", (int)UnityEngine.KeyCode.F6);
                        hotkeyOptions.Add("F7", (int)UnityEngine.KeyCode.F7);
                        hotkeyOptions.Add("F8", (int)UnityEngine.KeyCode.F8);
                        
                        addDropdownMethod.Invoke(null, new object[] { ModName, hotkeyKey, hotkeyLabel, hotkeyOptions, typeof(int), config.achievementHotkey });
                        DevLog("[BossRush] 成就界面快捷键配置项注册成功");
                    }
                }
                catch (Exception ex)
                {
                    DevLog("[BossRush] 注册成就界面快捷键配置项失败: " + ex.Message);
                }

            }
            catch (Exception ex)
            {
                DevLog("[BossRush] SetupModConfig 失败: " + ex.Message);
                DevLog("[BossRush] 堆栈: " + ex.StackTrace);
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

        /// <summary>
        /// 获取荒野号角是否使用狼模型
        /// </summary>
        /// <returns>true 使用狼模型，false 使用原版马匹模型</returns>
        public bool GetUseWolfModelForWildHorn()
        {
            if (config != null)
            {
                return config.useWolfModelForWildHorn;
            }
            return true; // 默认开启
        }
        
        #endregion
    }
}
