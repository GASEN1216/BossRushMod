// BossRushStartupGuard.cs - 宿主启动期防护：前置程序集、阶段隔离与正式版启动自检
//
// 官方 ModManager.ActivateMod 在 AddComponent 时同步执行宿主 Awake（反编译源 Duckov/Modding/ModManager.cs）。
// 本类型不引用任何 HarmonyLib 类型，必须在宿主第一次触碰 Harmony 之前运行。它负责三件事：
//
// 1. 0Harmony 不随本 Mod 分发，靠前置 HarmonyLib（创意工坊 3589088839）。官方按优先级激活 Mod，优先级相同的
//    顺序并不稳定；本 Mod 一旦先于前置激活，Mono 第一次解析 0Harmony 失败后会把这条引用永久记为缺失
//    （2026-11-26 用 Unity 2022.3 自带的 Mono 6.13 实测：之后再加载 0Harmony、再开关本 Mod 都救不回来）。
//    所以先从前置目录预加载；前置不在时退到其它已安装 Mod 自带的副本（取最高版本）。Mono 对未签名程序集只按
//    名字匹配、再次 LoadFrom 同一身份返回同一份（同日实测），前置之后自己加载时不会出现第二份。
// 2. 宿主 Awake 逐段隔离：任何一段抛错（包括方法体 JIT 时程序集缺失）不再中断后面的阶段；
//    补丁层整段失败时，退回把全部 Mod 物品同步登记进官方动态表，读档仍能拿到真物品。
// 3. 正式构建也输出一行启动自检：实际目录、激活时场景、是否迟到激活、Harmony 来源、关键补丁与失败阶段。
//    玩家反馈「重进就加载不出来」时，这一行就能定位卡在哪一段。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class BossRushStartupGuard
    {
        internal const string HarmonyAssemblyName = "0Harmony";
        internal const string HarmonyPrerequisiteWorkshopId = "3589088839";
        private const string HarmonyFileName = "0Harmony.dll";

        private static readonly List<string> failedStages = new List<string>();
        private static string harmonySource = "unchecked";
        private static string activationScene = string.Empty;
        private static bool lateActivation;
        private static int criticalPatchesVerified = -1;
        private static int criticalPatchesTotal = -1;

        /// <summary>动态物品关键补丁核验结果，只给启动自检日志读。</summary>
        internal static void NoteCriticalPatches(int verified, int total)
        {
            criticalPatchesVerified = verified;
            criticalPatchesTotal = total;
        }

        /// <summary>宿主 Awake 第一行（重复实例检查之后）调用。</summary>
        internal static void BeginAwake()
        {
            failedStages.Clear();
            lateActivation = false;
            criticalPatchesVerified = -1;
            criticalPatchesTotal = -1;
            try { activationScene = SceneManager.GetActiveScene().name; }
            catch { activationScene = "?"; }
            try
            {
                LevelManager level = LevelManager.Instance;
                lateActivation = level != null && level.MainCharacter != null;
            }
            catch { lateActivation = false; }
            harmonySource = EnsureHarmonyLoaded();
        }

        /// <summary>宿主 Awake 某一段抛错时调用：只记阶段名与异常，后面的阶段照常跑。</summary>
        internal static void FailStage(string stage, Exception e)
        {
            failedStages.Add(stage);
            Debug.LogError("[BossRush] 启动阶段失败 stage=" + stage + ": " + e);
        }

        /// <summary>补丁层整段失败（例如 0Harmony 仍不可用）时调用：同步登记全部 Mod 物品，保住读档。</summary>
        internal static void FailBootstrap(Exception e)
        {
            FailStage("harmony-bootstrap", e);
            RunStage("dynamic-items-fallback", () => BossRushDynamicItemRegistry.EnsureAllRegistered());
        }

        private static void RunStage(string stage, Action action)
        {
            try { action(); }
            catch (Exception e) { FailStage(stage, e); }
        }

        /// <summary>宿主 Awake 最后一行调用：输出启动自检，并接上读档装备抢救。</summary>
        internal static void CompleteAwake(ModBehaviour host)
        {
            int verified = criticalPatchesVerified;
            int total = criticalPatchesTotal;
            string line = "[BossRush] 启动自检 dir=" + SafeModDirectory()
                + " scene=" + activationScene
                + " late=" + (lateActivation ? "yes" : "no")
                + " harmony=" + harmonySource
                + " patches=" + verified + "/" + total
                + " failed=" + (failedStages.Count == 0 ? "none" : string.Join(",", failedStages.ToArray()));
            if (failedStages.Count > 0 || verified < 0 || verified != total) Debug.LogWarning(line);
            else Debug.Log(line);

            RunStage("equipment-rescue", () => BossRushEquipmentRescue.Attach(host, lateActivation));
        }

        /// <summary>宿主 OnDestroy 调用。</summary>
        internal static void Shutdown()
        {
            BossRushEquipmentRescue.Shutdown();
            failedStages.Clear();
            lateActivation = false;
        }

        /// <summary>
        /// 第一次触碰 Harmony 之前确保 0Harmony 已在进程里。返回来源描述，只进启动自检日志。
        /// </summary>
        internal static string EnsureHarmonyLoaded()
        {
            try
            {
                Assembly loaded = FindLoadedAssembly(HarmonyAssemblyName);
                if (loaded != null) return "loaded:" + loaded.GetName().Version;
                string path = FindHarmonyCopy();
                if (path == null) return "missing";
                Assembly preloaded = Assembly.LoadFrom(path);
                return "preloaded:" + preloaded.GetName().Version + "@" + Path.GetFileName(Path.GetDirectoryName(path));
            }
            catch (Exception e)
            {
                return "error:" + e.GetType().Name;
            }
        }

        private static Assembly FindLoadedAssembly(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                try
                {
                    if (assembly != null && string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal)) return assembly;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 前置 HarmonyLib 自带的副本优先（与按顺序激活时加载的是同一个文件）；没有前置时取已安装 Mod 里版本最高的副本。
        /// 只看本 Mod 所在目录的同级目录、游戏本地 Mods 目录与本游戏的创意工坊目录，启动时一次。
        /// </summary>
        private static string FindHarmonyCopy()
        {
            string modDirectory = SafeModDirectory();
            List<string> roots = new List<string>();
            AddRoot(roots, string.IsNullOrEmpty(modDirectory) ? null : Path.GetDirectoryName(modDirectory));
            string dataPath = null;
            try { dataPath = Application.dataPath; } catch { }
            if (!string.IsNullOrEmpty(dataPath))
            {
                AddRoot(roots, Path.Combine(dataPath, "Mods"));
                // Steam 布局：steamapps/common/<游戏>/Duckov_Data → steamapps/workshop/content/3167020
                try
                {
                    DirectoryInfo steamApps = Directory.GetParent(dataPath)?.Parent?.Parent;
                    if (steamApps != null) AddRoot(roots, Path.Combine(steamApps.FullName, "workshop", "content", "3167020"));
                }
                catch { }
            }

            string prerequisite = null, best = null;
            Version bestVersion = null;
            if (!string.IsNullOrEmpty(modDirectory)) Consider(Path.Combine(modDirectory, HarmonyFileName), ref best, ref bestVersion);
            for (int r = 0; r < roots.Count; r++)
            {
                string[] directories;
                try { directories = Directory.GetDirectories(roots[r]); }
                catch { continue; }
                for (int i = 0; i < directories.Length; i++)
                {
                    string candidate = Path.Combine(directories[i], HarmonyFileName);
                    if (!File.Exists(candidate)) continue;
                    if (prerequisite == null && string.Equals(Path.GetFileName(directories[i]), HarmonyPrerequisiteWorkshopId, StringComparison.Ordinal))
                        prerequisite = candidate;
                    Consider(candidate, ref best, ref bestVersion);
                }
            }
            return prerequisite ?? best;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                if (!Directory.Exists(full)) return;
                for (int i = 0; i < roots.Count; i++)
                    if (string.Equals(roots[i], full, StringComparison.OrdinalIgnoreCase)) return;
                roots.Add(full);
            }
            catch { }
        }

        private static void Consider(string path, ref string best, ref Version bestVersion)
        {
            try
            {
                if (!File.Exists(path)) return;
                AssemblyName name = AssemblyName.GetAssemblyName(path);
                if (name == null || !string.Equals(name.Name, HarmonyAssemblyName, StringComparison.Ordinal)) return;
                if (bestVersion == null || (name.Version != null && name.Version > bestVersion))
                {
                    best = path;
                    bestVersion = name.Version;
                }
            }
            catch { }
        }

        private static string SafeModDirectory()
        {
            // Awake 期官方还没写 info.path，GetModPath 会退到本程序集所在目录
            try { return ModBehaviour.GetModPath() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
