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
                ApplyHarmonyPatchesPerClass(harmony);
                DevLog("[BossRush] Harmony Patch 扫描完成");
            }
            catch (System.Exception e)
            {
                CriticalLog("[BossRush] [ERROR] Harmony Patch 扫描失败: " + e);
            }

            bool criticalDynamicItemPatchesReady = false;
            try
            {
                criticalDynamicItemPatchesReady = BossRush.Patches.ItemStatsSystem.DynamicItemRegistrationPatchSupport
                    .EnsureCriticalPatchesApplied(harmony);
            }
            catch (System.Exception e)
            {
                CriticalLog("[BossRush] [ERROR] 动态物品关键补丁验证失败: " + e);
            }

            if (!criticalDynamicItemPatchesReady)
            {
                try
                {
                    int readyCount = BossRushDynamicItemRegistry.EnsureAllRegistered();
                    int totalCount = BossRushDynamicItemRegistry.PublishedTypeCount;
                    string level = readyCount == totalCount ? "[WARNING]" : "[ERROR]";
                    CriticalLog("[BossRush] " + level + " 动态物品关键补丁不完整，已执行全量同步注册: "
                        + readyCount + "/" + totalCount);
                }
                catch (System.Exception e)
                {
                    CriticalLog("[BossRush] [ERROR] 动态物品全量同步注册失败: " + e);
                }
            }

            try
            {
                // 注册 Harmony Patch 分组（仅日志与元数据，不参与 Patch apply）
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

            HarmonyBindingSelfCheck.RunStartupSelfCheck(harmony);
        }

        // ====================================================================
        // Harmony 补丁逐类应用
        // ====================================================================
        // harmony.PatchAll() 内部就是
        //     AccessTools.GetTypesFromAssembly(assembly).Do(t => CreateClassProcessor(t).Patch())
        // 本方法保持同一个程序集、同一个类型顺序、同一个 processor 调用，
        // 因此**全部补丁成功时行为与 PatchAll 完全一致**。
        //
        // 差别只在失败路径：PatchAll 遇到第一个无法解析的目标就整体抛出，
        // 官方更新只要改动 50 个补丁目标里的任意 1 个，其余补丁全部不生效；
        // 逐类隔离后失配的那个补丁类单独失效，其余照常，并经 CriticalLog 报告。
        private static void ApplyHarmonyPatchesPerClass(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            var failures = new System.Collections.Generic.List<string>();
            int patchedClassCount = 0;

            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModBehaviour).Assembly))
            {
                if (!HasHarmonyPatchMetadata(type))
                {
                    continue;
                }

                try
                {
                    var processor = harmony.CreateClassProcessor(type);
                    if (processor == null)
                    {
                        continue;
                    }

                    var patchedMethods = processor.Patch();
                    if (patchedMethods != null && patchedMethods.Count > 0)
                    {
                        patchedClassCount++;
                    }
                }
                catch (System.Exception e)
                {
                    failures.Add((type != null ? type.FullName : "<unknown>")
                        + ": " + (e != null ? e.Message : "unknown"));
                }
            }

            if (failures.Count > 0)
            {
                CriticalLog("harmony-per-class-apply",
                    "[BossRush] [ERROR] Harmony 补丁逐类应用失败 " + failures.Count
                    + " 个（其余 " + patchedClassCount + " 个补丁类已生效）: "
                    + string.Join(" | ", failures.ToArray()));
            }
            else
            {
                DevLog("[BossRush] Harmony 补丁逐类应用完成: " + patchedClassCount + " 个补丁类生效");
            }
        }

        /// <summary>
        /// Harmony 的 class processor 会把普通类型上名为 Cleanup 的方法误判为
        /// Harmony cleanup 回调。PatchAll 内部虽遍历全程序集，但当前 Harmony 版本的
        /// processor 对无补丁元数据类型并非安全 no-op，因此先做与声明元数据等价的门控。
        /// 同时保留“类无特性、方法带 HarmonyPatch”的合法写法。
        /// </summary>
        private static bool HasHarmonyPatchMetadata(System.Type type)
        {
            if (type == null) return false;

            try
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    return true;
                }

                var methods = type.GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] Harmony 元数据检查失败: " + type.FullName + ": " + e.Message);
            }

            return false;
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

            try
            {
                AffixRuntimeService.EnsureRuntime();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] AffixRuntimeService initialization exception: " + e.Message);
            }

            // UI 皮肤必须在**任何面板创建之前**注入：ApplyPanelSkin 只在创建时赋一次
            // sprite，已建好的面板不会追溯换图。缺 bundle 时静默回退程序化皮肤（fail-open）。
            try
            {
                BossRushUISkinLoader.EnsureInjected();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] UI skin injection exception: " + e.Message);
            }

            WikiContentManager.Instance.ResetCache();
        }

        internal void InitializeAlwaysOnDeferredContent()
        {
            string modPath = GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                // 取不到 Mod 路径 = 实体模型与地图刷新点两套数据都装不起来，属玩家可见故障
                CriticalLog("[BossRush] [ERROR] Could not get Mod path; EntityModelFactory not initialized");
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
                    CriticalLog("[BossRush] [ERROR] MapSpawnPointRegistry initialization exception: " + e.Message);
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
                // MagicBlend 兼容补丁的两张热路径短路表。补丁本身随程序集常驻，
                // 因此清理挂在 always-on 层，与它的生命周期一致。
                BossRush.Patches.Compatibility.MagicBlendInitializationOrderPatch.ResetStaticCaches();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] MagicBlend 兼容补丁缓存清理异常: " + e.Message);
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
            HarmonyBindingSelfCheck.ResetStaticCaches();
            ModBehaviour.ResetCriticalLogStaticCaches();
        }
    }
}
