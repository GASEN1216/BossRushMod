using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeBootstrapRuntime()
        {
            try
            {
                var harmony = new Harmony("com.bossrush.mod");
                harmony.PatchAll();
                DevLog("[BossRush] Harmony Patch 已应用（Item.OnEnable）");

                // 注册 Harmony Patch 分组（仅日志与元数据，不改变 Patch apply 方式）
                HarmonyPatchGroupRegistrar.Clear();
                HarmonyPatchGroupRegistrar.Register(new BaseHubPatchGroup());
                HarmonyPatchGroupRegistrar.Register(new CombatPatchGroup());
                HarmonyPatchGroupRegistrar.Register(new DeathPatchGroup());
                HarmonyPatchGroupRegistrar.LogRegisteredGroups();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] Harmony Patch 应用失败: " + e.Message);
            }
        }

        internal void InitializeAlwaysOnRuntime()
        {
            try
            {
                string modPath = GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    EntityModelFactory.Initialize(modPath);
                    // 初始化地图刷新点注册表（同步扫描 Assets/SpawnPoints/*.json）
                    _mapSpawnRegistry.Initialize(modPath);
                }
                else
                {
                    DevLog("[BossRush] [WARNING] 无法获取 Mod 路径，EntityModelFactory 未初始化");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] EntityModelFactory 初始化异常: " + e.Message);
            }

            WikiContentManager.Instance.ResetCache();
            InitializeAffinitySystem();
        }

        internal void TickAlwaysOnRuntime()
        {
            UpdateMessage();
            AffinityManager.UpdateDeferredSave();
        }

        internal void OnSceneUnloadAlwaysOnRuntime()
        {
            AffinityUIManager.OnSceneUnload();
            AffinityManager.OnSceneUnload();
        }

        internal void CleanupAlwaysOnRuntimeOnDestroy()
        {
            try
            {
                AffinityManager.OnAffinityChanged -= OnAffinityChanged;
                AffinityManager.OnLevelUp -= OnAffinityLevelUp;
                AffinityManager.Shutdown();
                AffinityUIManager.Cleanup();
            }
            catch
            {
            }

            try
            {
                EntityModelFactory.Shutdown();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] EntityModelFactory 卸载异常: " + e.Message);
            }
        }
    }
}
