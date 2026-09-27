using System;
using System.Collections.Generic;

namespace UnityEngine.SceneManagement { }

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            var go = value as GameObject;
            if (go != null) foreach (Object component in go.Components) component.Destroyed = true;
            value.Destroyed = true;
        }
    }
    public sealed class GameObject : Object
    {
        internal readonly List<Object> Components = new List<Object>();
    }
}

namespace BossRush
{
    internal static class Probe
    {
        internal static readonly List<string> Trace = new List<string>();
        internal static void Add(string value) { Trace.Add(value); }
    }
    internal struct SceneRuntimeContext { }
    internal sealed class AchievementRuntimeModule : BossRushRuntimeModuleBase
    {
        public override string ModuleName { get { return "Achievement"; } }
        internal void CleanupAchievementRuntime() { Probe.Add("achievement.cleanup"); }
        public override void OnDestroy() { Probe.Add("module.achievement.destroy"); }
    }
    internal sealed class TailModule : BossRushRuntimeModuleBase
    {
        public override string ModuleName { get { return "Tail"; } }
        public override void OnDestroy() { Probe.Add("module.tail.destroy"); }
    }
    internal static class SafeRuntime
    {
        internal static void Run(string tag, Action action) { Probe.Add(tag); action(); }
    }
    internal static class BossRushUISkinLoader { internal static void Cleanup() { } }
    internal static class HarmonyPatchGroupRegistrar { internal static void Clear() { Probe.Add("harmony.clear"); } }
    internal static class ModeG { internal static void PrepareHostDestroy() { Probe.Add("modeg.prepare"); } }
    internal static class BossRushAchievementManager { internal static void ResetStaticCaches() { } }
    internal static class AchievementIconLoader { internal static void ResetStaticCaches() { } }
    internal static class BossRushUI { internal static void ResetStaticCaches() { } }
    internal static class BossBgmCoordinator { internal static void ResetStaticCaches() { } }
    internal static class MutatorUI { internal static void ResetStaticCaches() { } }
    internal static class BossRushSaveFileThrottle { internal static void ResetStaticCaches() { } }

    public partial class ModBehaviour : UnityEngine.Object
    {
        public static ModBehaviour Instance { get; private set; }
        private readonly BossRushRuntimeModuleHost runtimeModuleHost = new BossRushRuntimeModuleHost();
        private AchievementRuntimeModule achievementRuntime;
        internal readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public ModBehaviour() { gameObject.Components.Add(this); }
        private void DontDestroyOnLoad(UnityEngine.Object value) { }
        internal static void DevLog(string value) { }
        internal static void CriticalLog(string key, string value) { throw new Exception(value); }
        private void RegisterRuntimeModules()
        {
            Probe.Add("modules.register");
            achievementRuntime = new AchievementRuntimeModule();
            runtimeModuleHost.Register(achievementRuntime);
            runtimeModuleHost.Register(new TailModule());
        }
        private void InitializeBootstrapRuntime() { }
        private void InitializeAlwaysOnRuntime() { }
        private void RegisterPlayerLifecycleRuntimeEvents() { }
        private void InitializeDebugToolsRuntime() { }
        private void InitializeAchievementRuntime() { }
        private void ShutdownLanguageChangeSubscription() { Probe.Add("language.cleanup"); }
        private void CleanupDebugToolsOnDestroy() { Probe.Add("debug.cleanup"); }
        private void CleanupPlayerLifecycleRuntimeEvents() { Probe.Add("player.cleanup"); }
        private void CleanupAffixForgeRuntimeOnDestroy() { Probe.Add("affix.cleanup"); }
        private void CleanupAlwaysOnRuntimeOnDestroy() { Probe.Add("always.cleanup"); }
        private void CleanupIntegrationRuntimeOnDestroy() { Probe.Add("integration.cleanup"); }
        private void CleanupModeRuntimeOnDestroy() { Probe.Add("modes.cleanup"); }
        internal void InvokeAwake() { Awake(); }
        internal void InvokeDestroy() { OnDestroy(); }
        internal static void SetInstance(ModBehaviour value) { Instance = value; }
    }
}
