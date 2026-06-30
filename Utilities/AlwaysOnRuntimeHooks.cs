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
                WishFountainService.EnsureRuntime();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] WishFountainService initialization exception: " + e.Message);
            }

            WikiContentManager.Instance.ResetCache();
        }

        internal void InitializeAlwaysOnDeferredContent()
        {
            string modPath = GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                DevLog("[BossRush] [WARNING] Could not get Mod path; EntityModelFactory not initialized");
            }
            else
            {
                try
                {
                    EntityModelFactory.Initialize(modPath);
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] [WARNING] EntityModelFactory initialization exception: " + e.Message);
                }

                try
                {
                    _mapSpawnRegistry.Initialize(modPath);
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] [ERROR] MapSpawnPointRegistry initialization exception: " + e.Message);
                }
            }

            InitializeAffinitySystem();
        }

        internal void TickAlwaysOnRuntime()
        {
            UpdateMessage();
            AffinityManager.UpdateDeferredSave();
            // 死亡帧把亡魂列表写盘的成本已移走，这里兜底把内存中的脏数据去抖刷回 ES3。
            UpdateDeferredDeathWraithSave_DeathWraith();
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
                AffinityManager.ResetStaticCaches();
                AffinityUIManager.Cleanup();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] Affinity runtime cleanup failed: " + e.Message);
            }

            try
            {
                EntityModelFactory.ResetStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] EntityModelFactory 卸载异常: " + e.Message);
            }

            try
            {
                ModBehaviour.ResetBossRushAudioHooksStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] BossRushAudioHooks 卸载异常: " + e.Message);
            }

            try
            {
                BossRush.Utils.NPCUIAssetCache.ResetStaticCaches();
                DialogueActorFactory.ResetStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] NPC UI/Dialogue 缓存卸载异常: " + e.Message);
            }

            try
            {
                MapThumbnailCache.ResetStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] 地图缩略图缓存卸载异常: " + e.Message);
            }

            try
            {
                WishFountainService.ShutdownRuntime();
                WishFountainService.ResetStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] WishFountainService 卸载异常: " + e.Message);
            }

            BossRushEventBus.ResetStaticCaches();
            SafeRuntime.ResetStaticCaches();
            BossRush.Common.Utils.ReflectionCache.ResetStaticCaches();
        }
    }
}
