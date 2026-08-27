using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class BossRushRuntimeModuleHost
    {
        private readonly List<IBossRushRuntimeModule> modules = new List<IBossRushRuntimeModule>();
        private ModBehaviour owner;

        internal void Register(IBossRushRuntimeModule module)
        {
            if (module == null || modules.Contains(module))
            {
                return;
            }

            modules.Add(module);
        }

        internal void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            for (int i = 0; i < modules.Count; i++)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnAwake(owner);
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnAwake", e);
                }
            }
        }

        internal void OnStart()
        {
            for (int i = 0; i < modules.Count; i++)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnStart();
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnStart", e);
                }
            }
        }

        internal void OnSceneLoaded(SceneRuntimeContext context)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnSceneLoaded(context);
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnSceneLoaded", e);
                }
            }
        }

        internal void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnUpdate(deltaTime, unscaledDeltaTime);
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnUpdate", e);
                }
            }
        }

        internal void OnLateUpdate()
        {
            for (int i = 0; i < modules.Count; i++)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnLateUpdate();
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnLateUpdate", e);
                }
            }
        }

        internal void OnDestroy()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                IBossRushRuntimeModule module = modules[i];
                try
                {
                    module.OnDestroy();
                }
                catch (Exception e)
                {
                    LogModuleError(module, "OnDestroy", e);
                }
            }

            owner = null;
        }

        private void LogModuleError(IBossRushRuntimeModule module, string lifecycle, Exception e)
        {
            string moduleName = module != null && !string.IsNullOrEmpty(module.ModuleName)
                ? module.ModuleName
                : "<unknown>";
            string message = e != null ? e.Message : string.Empty;
            // 模块生命周期异常按 模块+阶段 去重上报，避免每帧刷屏的同时保证玩家端可见
            ModBehaviour.CriticalLog(
                "runtime-module-" + moduleName + "-" + lifecycle,
                "[BossRush] [WARNING] Runtime module " + moduleName + " " + lifecycle + " failed: " + message);
        }
    }
}
