using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static int nextInstanceId;
        private readonly int instanceId = ++nextInstanceId;
        public int GetInstanceID() { return instanceId; }
    }

    public class Component : Object { }
    public class MonoBehaviour : Component { }

    public class GameObject : Object
    {
        public GameObject(string name) { BossRush.HudRuntimeTrace.Events.Add("create:" + name); }

        public T AddComponent<T>() where T : Component, new()
        {
            BossRush.HudRuntimeTrace.Events.Add("add:" + typeof(T).Name);
            return new T();
        }
    }

    public struct Color
    {
        public float Value;
        public static bool operator ==(Color left, Color right) { return left.Value == right.Value; }
        public static bool operator !=(Color left, Color right) { return !(left == right); }
        public override bool Equals(object value) { return value is Color && this == (Color)value; }
        public override int GetHashCode() { return Value.GetHashCode(); }
    }

    public static class Mathf
    {
        public static float Max(float left, float right) { return Math.Max(left, right); }
        public static float Abs(float value) { return Math.Abs(value); }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
        public static int RoundToInt(float value) { return (int)Math.Round(value); }
    }
}

namespace BossRush
{
    internal static class HudRuntimeTrace
    {
        internal static readonly List<string> Events = new List<string>();
    }

    internal enum ZombieModeRunOnlyObjectKind { Hud }

    public sealed class ZombieModeHudController : UnityEngine.MonoBehaviour
    {
        public int RunId;
        public bool HasGainText = true;
        public bool HasGainGroup = true;
        public string GainText;
        public float GainAlpha = -1f;

        public void Initialize(int runId)
        {
            RunId = runId;
            HudRuntimeTrace.Events.Add("initialize:" + runId);
        }

        public void SetPurificationGainText(string value) { GainText = value; }
        public void SetPurificationGainAlpha(float value) { GainAlpha = value; }
    }

    public sealed class ModBehaviour
    {
        public int PurificationPoints;
        public int GetZombieModePurificationPoints(int runId) { return PurificationPoints; }
    }

    internal static class L10n
    {
        internal static string T(string key) { return "points {0}"; }
    }

    internal static class BossRushUI
    {
        internal static float SmoothStep(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return t * t * (3f - 2f * t);
        }
    }

    public static class Program
    {
        private static int checks;

        private static void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception("FAIL " + message);
            Console.WriteLine("PASS " + message);
        }

        public static void Main()
        {
            var owner = new ModBehaviour();
            var module = new ZombieModeRuntimeModule(owner, 7);
            module.CreateZombieModeHud(6);
            Check(HudRuntimeTrace.Events.Count == 0 && module.HudStateCount == 0,
                "stale RunId returns before creating or registering a HUD");

            module.CreateZombieModeHud(7);
            ZombieModeHudController first = module.LastCreatedController;
            Check(HudRuntimeTrace.Events.Count == 4 &&
                  HudRuntimeTrace.Events[0] == "create:ZombieMode_Hud" &&
                  HudRuntimeTrace.Events[1] == "add:ZombieModeHudController" &&
                  HudRuntimeTrace.Events[2] == "initialize:7" &&
                  HudRuntimeTrace.Events[3] == "register:Hud",
                "valid HUD creation preserves root, component, initialization, RunOnly registration order");
            Check(module.HudStateCount == 1 && module.LastRunOnlyCleanup != null,
                "HUD state is attached before the RunOnly cleanup callback is stored");
            Check(module.SetZombieModeHudVisibility(first, true) &&
                  !module.SetZombieModeHudVisibility(first, true) &&
                  module.SetZombieModeHudVisibility(first, false),
                "module visibility cache reports only actual hidden-state transitions");

            var state = module.GetHudStateForTest(first);
            string cachedText = null;
            Check(module.CacheMainTextForTest(ref cachedText, "wave 1") &&
                  !module.CacheMainTextForTest(ref cachedText, new string("wave 1".ToCharArray())) &&
                  module.CacheMainTextForTest(ref cachedText, "wave 2"),
                "module text cache updates only when the displayed content changes");
            owner.PurificationPoints = 25;
            Check(!module.TickPurificationForTest(first, state, 0.1f) &&
                  state.ShownPurification == 25 && state.LastActualPurification == 25,
                "first purification sample initializes displayed and actual points without rolling");
            owner.PurificationPoints = 28;
            Check(module.TickPurificationForTest(first, state, 0.1f) &&
                  state.PendingGain == 3 && first.GainText == "points 3" &&
                  Math.Abs(state.ShownPurificationValue - 27f) < 0.001f && state.ShownPurification == 27,
                "purification gain aggregates and rolls at the production minimum speed");
            Check(module.TickPurificationForTest(first, state, 0.1f) &&
                  state.ShownPurification == 28 && state.PendingGain == 3,
                "purification interpolation continues without double-counting the same gain");
            Check(!module.TickPurificationForTest(first, state, 0.1f),
                "purification stops requesting per-frame text refresh once settled");
            owner.PurificationPoints = 26;
            Check(module.TickPurificationForTest(first, state, 0.1f) && state.PendingGain == 3 &&
                  state.LastActualPurification == 26,
                "purification loss rolls down without reporting a positive gain");

            module.CreateZombieModeHud(7);
            ZombieModeHudController second = module.LastCreatedController;
            Check(module.HudStateCount == 2 && module.SetZombieModeHudVisibility(second, true),
                "separate HUD instances receive separate runtime state");
            module.LastRunOnlyCleanup();
            Check(module.HudStateCount == 1 && module.GetHudStateForTest(first) != null,
                "RunOnly cleanup removes only its HUD state and leaves another instance intact");
            module.CleanupZombieModeHud(first);
            module.CleanupZombieModeHud(first);
            Check(module.HudStateCount == 0,
                "component destruction cleanup is idempotent after RunOnly cleanup");

            Console.WriteLine("AuditModeLifecycle HUD runtime: " + checks + " PASS / 0 FAIL");
        }
    }
}
