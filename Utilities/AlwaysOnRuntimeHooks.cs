using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeBootstrapRuntime()
        {
            var harmony = new Harmony("com.bossrush.mod");
            try
            {
                harmony.PatchAll();
                DevLog("[BossRush] Harmony Patch 扫描完成");
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] Harmony Patch 扫描失败: " + e);
            }

            bool criticalDynamicItemPatchesReady = false;
            try
            {
                criticalDynamicItemPatchesReady = BossRush.Patches.ItemStatsSystem.DynamicItemRegistrationPatchSupport
                    .EnsureCriticalPatchesApplied(harmony);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [ERROR] 动态物品关键补丁验证失败: " + e);
            }

            if (!criticalDynamicItemPatchesReady)
            {
                try
                {
                    int readyCount = BossRushDynamicItemRegistry.EnsureAllRegistered();
                    int totalCount = BossRushDynamicItemRegistry.PublishedTypeCount;
                    string level = readyCount == totalCount ? "[WARNING]" : "[ERROR]";
                    DevLog("[BossRush] " + level + " 动态物品关键补丁不完整，已执行全量同步注册: "
                        + readyCount + "/" + totalCount);
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] [ERROR] 动态物品全量同步注册失败: " + e);
                }
            }

            try
            {
                // 注册 Harmony Patch 分组（仅日志与元数据，不改变 Patch apply 方式）
                HarmonyPatchGroupRegistrar.Clear();
                HarmonyPatchGroupRegistrar.Register(new BaseHubPatchGroup());
                HarmonyPatchGroupRegistrar.Register(new CombatPatchGroup());
                HarmonyPatchGroupRegistrar.Register(new DeathPatchGroup());
                HarmonyPatchGroupRegistrar.LogRegisteredGroups();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] Harmony Patch 分组元数据注册失败: " + e);
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
