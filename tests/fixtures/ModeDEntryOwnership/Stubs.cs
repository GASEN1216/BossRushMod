using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true;
            var gameObject = value as GameObject;
            if (!ReferenceEquals(gameObject, null))
                foreach (Object component in gameObject.Components) Destroy(component);
        }
    }
    public class GameObject : Object
    {
        internal readonly List<Object> Components = new List<Object>();
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Component() { gameObject.Components.Add(this); }
    }
    public class Coroutine {}
    public static class Mathf { public static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { handle = 1 }; } }
}
namespace BossRush
{
    internal static class Probe
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static readonly List<string> Logs = new List<string>();
        internal static string Fault;
        internal static bool RiskAllowed, Naked;
        internal static int Ticket;
        internal static void Step(string name)
        {
            Calls.Add(name);
            if (Fault == name) throw new InvalidOperationException("probe_" + name);
        }
    }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour host) {}
        public virtual void OnSceneLoaded(SceneRuntimeContext context) {}
        public virtual void OnUpdate(float delta, float unscaled) {}
        public virtual void OnDestroy() {}
    }
    internal struct SceneRuntimeContext {}
    public class EnemyPresetInfo {}
    public class CharacterRandomPreset {}
    public class CharacterMainControl : UnityEngine.Component
    {
        public bool dropBoxOnDead;
    }
    internal sealed class ModeDItemPool
    {
        internal object FindTagByName(string value) { return null; }
        internal void InitializeModeDItemPools(Func<string, object> lookup) { Probe.Step("items"); }
        internal void EnsureModeDGlobalItemPool() { Probe.Step("pool"); }
        internal void GivePlayerStarterKit() { Probe.Step("starter"); }
    }
    internal sealed partial class ModeDRuntimeModule
    {
        private void TickModeDIntegrity(float delta) {}
        private void SetupSignForModeD(object sign) { Probe.Step("sign"); }
    }
    internal sealed class ModeERuntimeModule
    {
        internal bool modeEActive;
        public bool IsModeEActive { get { Probe.Step("mode-e"); return modeEActive; } }
    }
    internal static class ModeHRuntimeGates
    {
        internal static bool IsLegacyModeEntryAllowed() { Probe.Step("risk"); return Probe.RiskAllowed; }
        internal static string ResolveLegacyBlockedMessageKey() { Probe.Step("resolve"); return "blocked"; }
    }
    internal static class L10n
    {
        internal static string T(string key) { Probe.Step("translate-key"); return key; }
        internal static string T(string cn, string en) { Probe.Step("translate-text"); return en; }
    }
    internal static class ModeEntryInventory
    {
        internal static bool IsPlayerNakedWithAllowedItems(string tag, int first, int second, bool flags)
        {
            Probe.Step("inventory");
            if (tag != "ModeD" || first != Probe.Ticket || second != -1 || !flags)
                throw new InvalidOperationException("production inventory arguments changed");
            return Probe.Naked;
        }
    }
    public partial class ModBehaviour
    {
        public static ModBehaviour Instance;
        internal ModeDRuntimeModule modeDRuntime;
        internal ModeERuntimeModule modeERuntime;
        internal int bossRushTicketTypeId;
        internal bool StartedOnThisHost;
        public bool IsModeEActive { get { return modeERuntime.IsModeEActive; } }
        internal int ModeDConfiguredEnemiesPerWave { get { Probe.Step("config"); return 5; } }
        internal object ArenaRewardSignInteract { get { return null; } }
        public static void DevLog(string message) { Probe.Logs.Add(message); }
        public void ShowMessage(string message) { Probe.Step(message == "blocked" ? "blocked-message" : "message"); }
        public void ShowBigBanner(string message) { Probe.Step("banner"); }
        internal void ResetArenaForModeD() { Probe.Step("arena-reset"); StartedOnThisHost = true; }
        internal void ClearModeDEnemyRecoveryState() { Probe.Step("recovery"); }
        internal void InitializeModeDEnemyPoolsForRuntime() { Probe.Step("enemies"); }
        internal void TryRollMutatorsForArena(string mode) { Probe.Step("mutators:" + mode); }
        internal void ClearModeDMutators(string mode) {}
        internal void UnregisterEnemyRecoveryForArena(CharacterMainControl enemy) {}
        internal void StopCoroutine(UnityEngine.Coroutine coroutine) {}
    }
}
