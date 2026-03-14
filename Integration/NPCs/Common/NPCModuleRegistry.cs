// ============================================================================
// NPCModuleRegistry.cs - NPC模块注册中心
// ============================================================================
// 模块说明：
//   统一管理公共NPC（快递员、哥布林、护士等）的注册、好感度配置注册与场景生成。
//   目标：新增NPC时尽量只新增“模块类 + 业务实现”，减少对 ModBehaviour 的侵入修改。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// NPC运行模块接口
    /// </summary>
    public interface INPCModule
    {
        /// <summary>模块唯一标识（建议与 NPC ID 一致）</summary>
        string NpcId { get; }

        /// <summary>生成顺序（越小越先生成）</summary>
        int SpawnOrder { get; }

        /// <summary>创建该NPC的好感度配置（无则返回 null）</summary>
        INPCAffinityConfig CreateAffinityConfig();

        /// <summary>是否应在当前场景生成</summary>
        bool ShouldSpawnInScene(ModBehaviour mod, string sceneName);

        /// <summary>执行生成</summary>
        void Spawn(ModBehaviour mod);

        /// <summary>执行销毁</summary>
        void Destroy(ModBehaviour mod);
    }

    /// <summary>
    /// NPC模块注册中心
    /// </summary>
    public static class NPCModuleRegistry
    {
        private static readonly List<INPCModule> modules = new List<INPCModule>();
        private static readonly HashSet<Type> registeredTypes = new HashSet<Type>();
        private static readonly HashSet<string> registeredNpcIds = new HashSet<string>();
        private static bool initialized = false;

        /// <summary>
        /// 初始化模块中心（幂等）
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;

            modules.Clear();
            registeredTypes.Clear();
            registeredNpcIds.Clear();

            // 内置核心模块
            RegisterInternal(new CourierNPCModule());
            RegisterInternal(new GoblinNPCModule());
            RegisterInternal(new NurseNPCModule());

            // 自动发现模块（可选扩展）
            AutoDiscoverModules();

            modules.Sort((a, b) => a.SpawnOrder.CompareTo(b.SpawnOrder));
            initialized = true;
            ModBehaviour.DevLog("[NPCRegistry] 初始化完成，模块数量: " + modules.Count);
        }

        /// <summary>
        /// 外部注册模块（运行时扩展）
        /// </summary>
        public static bool Register(INPCModule module)
        {
            Initialize();
            bool registered = RegisterInternal(module);
            if (registered)
            {
                modules.Sort((a, b) => a.SpawnOrder.CompareTo(b.SpawnOrder));

                try
                {
                    INPCAffinityConfig config = module.CreateAffinityConfig();
                    if (config != null)
                    {
                        AffinityManager.RegisterNPC(config);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] Register affinity config failed: " + module.NpcId + " - " + e.Message);
                }

                ModBehaviour.DevLog("[NPCRegistry] External module registered: " + module.NpcId + ", total=" + modules.Count);
            }
            return registered;
        }

        /// <summary>
        /// 统一注册全部模块的好感度配置
        /// </summary>
        public static int RegisterAffinityConfigs()
        {
            Initialize();
            int count = 0;

            for (int i = 0; i < modules.Count; i++)
            {
                INPCModule module = modules[i];
                if (module == null) continue;

                try
                {
                    INPCAffinityConfig config = module.CreateAffinityConfig();
                    if (config == null) continue;

                    AffinityManager.RegisterNPC(config);
                    count++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] 注册好感度配置失败: " + module.NpcId + " - " + e.Message);
                }
            }

            ModBehaviour.DevLog("[NPCRegistry] 好感度配置注册完成，数量: " + count);
            return count;
        }

        /// <summary>
        /// 在当前场景按模块规则生成公共NPC
        /// </summary>
        public static int SpawnForCurrentScene(ModBehaviour mod, string context)
        {
            if (mod == null) return 0;

            Initialize();
            string sceneName = SceneManager.GetActiveScene().name;
            bool useRandomSupportNpcSelection = mod.ShouldUseRandomSupportNpcSelection(sceneName);
            List<INPCModule> supportNpcCandidates = useRandomSupportNpcSelection ? new List<INPCModule>() : null;

            int spawnCount = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                INPCModule module = modules[i];
                if (module == null) continue;

                bool shouldSpawn = false;
                try
                {
                    shouldSpawn = module.ShouldSpawnInScene(mod, sceneName);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] ShouldSpawnInScene 异常: " + module.NpcId + " - " + e.Message);
                }

                if (!shouldSpawn) continue;

                if (useRandomSupportNpcSelection && IsSupportNpcCandidate(module))
                {
                    supportNpcCandidates.Add(module);
                    continue;
                }

                try
                {
                    module.Spawn(mod);
                    spawnCount++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] Spawn 异常: " + module.NpcId + " - " + e.Message);
                }
            }

            if (useRandomSupportNpcSelection && supportNpcCandidates != null && supportNpcCandidates.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, supportNpcCandidates.Count);
                INPCModule selectedModule = supportNpcCandidates[randomIndex];

                try
                {
                    selectedModule.Spawn(mod);
                    spawnCount++;
                    ModBehaviour.DevLog("[NPCRegistry] Random support NPC selected: " + selectedModule.NpcId + " [" + (randomIndex + 1) + "/" + supportNpcCandidates.Count + "]");
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] Random support NPC spawn failed: " + selectedModule.NpcId + " - " + e.Message);
                }
            }

            ModBehaviour.DevLog("[NPCRegistry] " + context + "，场景=" + sceneName + "，触发生成模块数: " + spawnCount);
            return spawnCount;
        }

        /// <summary>
        /// 判断指定场景是否有任意模块需要生成
        /// </summary>
        public static bool ShouldSpawnAnyInScene(ModBehaviour mod, string sceneName)
        {
            if (mod == null || string.IsNullOrEmpty(sceneName)) return false;

            Initialize();
            for (int i = 0; i < modules.Count; i++)
            {
                INPCModule module = modules[i];
                if (module == null) continue;

                try
                {
                    if (module.ShouldSpawnInScene(mod, sceneName))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] ShouldSpawnAnyInScene 异常: " + module.NpcId + " - " + e.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// 重置注册中心状态（热重载/Mod卸载时调用）
        /// </summary>
        public static void Reset()
        {
            modules.Clear();
            registeredTypes.Clear();
            registeredNpcIds.Clear();
            initialized = false;
            ModBehaviour.DevLog("[NPCRegistry] 注册中心已重置");
        }

        /// <summary>
        /// 销毁全部已注册模块对应的NPC实例
        /// </summary>
        public static int DestroyAll(ModBehaviour mod, string reason)
        {
            if (mod == null) return 0;

            Initialize();
            int destroyCount = 0;
            string currentScene = SceneManager.GetActiveScene().name;

            for (int i = 0; i < modules.Count; i++)
            {
                INPCModule module = modules[i];
                if (module == null) continue;

                if (ShouldSkipDestroyForMarriedNpc(reason, currentScene, module.NpcId))
                {
                    ModBehaviour.DevLog("[NPCRegistry] Keep married NPC alive: " + module.NpcId + ", reason=" + reason);
                    continue;
                }

                try
                {
                    module.Destroy(mod);
                    destroyCount++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCRegistry] [WARNING] Destroy failed: " + module.NpcId + " - " + e.Message);
                }
            }

            ModBehaviour.DevLog("[NPCRegistry] DestroyAll completed, reason=" + reason + ", count=" + destroyCount);
            return destroyCount;
        }

        private static bool ShouldSkipDestroyForMarriedNpc(string reason, string sceneName, string npcId)
        {
            if (!string.Equals(reason, "LeaveBossRushScene", StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(sceneName, "Base_SceneV2", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(npcId))
            {
                return false;
            }

            if (npcId == GoblinAffinityConfig.NPC_ID || npcId == NurseAffinityConfig.NPC_ID)
            {
                return AffinityManager.IsMarriedToPlayer(npcId);
            }

            return false;
        }

        private static bool IsSupportNpcCandidate(INPCModule module)
        {
            if (module == null || string.IsNullOrEmpty(module.NpcId))
            {
                return false;
            }

            return !string.Equals(module.NpcId, "courier_awen", StringComparison.Ordinal);
        }

        private static bool RegisterInternal(INPCModule module)
        {
            if (module == null) return false;

            Type type = module.GetType();
            if (registeredTypes.Contains(type)) return false;

            string npcId = module.NpcId;
            if (string.IsNullOrEmpty(npcId))
            {
                ModBehaviour.DevLog("[NPCRegistry] [WARNING] 跳过注册：NpcId 为空，类型=" + type.FullName);
                return false;
            }

            if (registeredNpcIds.Contains(npcId))
            {
                ModBehaviour.DevLog("[NPCRegistry] [WARNING] 跳过注册：NpcId 冲突 " + npcId + "，类型=" + type.FullName);
                return false;
            }

            modules.Add(module);
            registeredTypes.Add(type);
            registeredNpcIds.Add(npcId);
            return true;
        }

        private static void AutoDiscoverModules()
        {
            try
            {
                Assembly assembly = typeof(ModBehaviour).Assembly;
                Type moduleType = typeof(INPCModule);
                int discovered = 0;

                Type[] types = assembly.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!moduleType.IsAssignableFrom(type)) continue;

                    ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;

                    INPCModule module = ctor.Invoke(null) as INPCModule;
                    if (RegisterInternal(module))
                    {
                        discovered++;
                    }
                }

                if (discovered > 0)
                {
                    ModBehaviour.DevLog("[NPCRegistry] 自动发现模块数量: " + discovered);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCRegistry] [WARNING] 自动发现模块失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 快递员模块
    /// </summary>
    internal sealed class CourierNPCModule : INPCModule
    {
        public string NpcId { get { return "courier_awen"; } }
        public int SpawnOrder { get { return 10; } }
        public INPCAffinityConfig CreateAffinityConfig() { return null; }

        public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)
        {
            if (mod == null || string.IsNullOrEmpty(sceneName)) return false;

            // BossRush相关模式下始终允许（内部会按场景坐标规则处理）
            if (mod.IsActive || mod.IsModeDActive || mod.IsBossRushArenaActive || mod.IsModeEActive)
            {
                return true;
            }

            // 普通模式仅在配置场景生成
            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }

        public void Spawn(ModBehaviour mod)
        {
            mod.SpawnCourierNPC();
        }

        public void Destroy(ModBehaviour mod)
        {
            mod.DestroyCourierNPC();
        }
    }

    /// <summary>
    /// 哥布林模块
    /// </summary>
    internal sealed class GoblinNPCModule : INPCModule
    {
        public string NpcId { get { return GoblinAffinityConfig.NPC_ID; } }
        public int SpawnOrder { get { return 20; } }
        public INPCAffinityConfig CreateAffinityConfig() { return GoblinAffinityConfig.Instance; }

        public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)
        {
            if (AffinityManager.IsMarriedToPlayer(GoblinAffinityConfig.NPC_ID))
            {
                return false;
            }
            if (mod == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            if (mod.ShouldUseRandomSupportNpcSelection(sceneName))
            {
                return mod.IsValidBossRushArenaScene(sceneName);
            }

            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }

        public void Spawn(ModBehaviour mod)
        {
            mod.SpawnGoblinNPC();
        }

        public void Destroy(ModBehaviour mod)
        {
            mod.DestroyGoblinNPC();
        }
    }

    /// <summary>
    /// 护士模块
    /// </summary>
    internal sealed class NurseNPCModule : INPCModule
    {
        public string NpcId { get { return NurseAffinityConfig.NPC_ID; } }
        public int SpawnOrder { get { return 30; } }
        public INPCAffinityConfig CreateAffinityConfig() { return NurseAffinityConfig.Instance; }

        public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)
        {
            if (AffinityManager.IsMarriedToPlayer(NurseAffinityConfig.NPC_ID))
            {
                return false;
            }
            if (mod == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            if (mod.ShouldUseRandomSupportNpcSelection(sceneName))
            {
                return mod.IsValidBossRushArenaScene(sceneName);
            }

            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }

        public void Spawn(ModBehaviour mod)
        {
            mod.SpawnNurseNPC();
        }

        public void Destroy(ModBehaviour mod)
        {
            mod.DestroyNurseNPC();
        }
    }
}
