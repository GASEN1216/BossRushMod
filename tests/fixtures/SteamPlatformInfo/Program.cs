using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
        public static readonly List<string> Lookups = new List<string>();
        public static Type TypeByName(string name)
        {
            Lookups.Add(name);
            Type result;
            return Types.TryGetValue(name, out result) ? result : null;
        }
    }
}
namespace BossRush
{
    public static class ModBehaviour
    {
        public static readonly List<string> Logs = new List<string>();
        public static void DevLog(string text) { Logs.Add(text); }
    }
}
public static class FakeFriends
{
    public static string Name;
    public static bool Throw;
    public static int Calls;
    public static string GetPersonaName() { Calls++; if (Throw) throw new Exception("primary failed"); return Name; }
}
public static class FakeManager
{
    public static object Name;
    public static bool Throw;
    public static int Calls;
    public static object GetSteamDisplay() { Calls++; if (Throw) throw new Exception("fallback failed"); return Name; }
}
internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    private static void Reset()
    {
        // Reset private production statics only between independent scenarios.
        foreach (var field in typeof(SteamPlatformInfo).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            field.SetValue(null, field.FieldType == typeof(bool) ? (object)false : null);
        HarmonyLib.AccessTools.Types.Clear(); HarmonyLib.AccessTools.Lookups.Clear(); ModBehaviour.Logs.Clear();
        FakeFriends.Name = null; FakeFriends.Throw = false; FakeFriends.Calls = 0;
        FakeManager.Name = null; FakeManager.Throw = false; FakeManager.Calls = 0;
    }
    private static void Main()
    {
        Reset();
        HarmonyLib.AccessTools.Types["Steamworks.SteamFriends"] = typeof(FakeFriends);
        HarmonyLib.AccessTools.Types["SteamManager"] = typeof(FakeManager);
        FakeFriends.Name = "first"; FakeManager.Name = "fallback";
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == "first", "primary Steam name wins");
        FakeFriends.Name = "second";
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == "second" && FakeFriends.Calls == 2, "cache retains MethodInfo rather than stale player name");
        Check(HarmonyLib.AccessTools.Lookups.Count == 1 && FakeManager.Calls == 0, "successful primary caches lookup and never invokes fallback");
        FakeFriends.Name = "";
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == "fallback", "empty primary name uses SteamManager fallback");
        FakeFriends.Throw = true;
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == "fallback", "primary invocation exception preserves fallback");
        SteamPlatformInfo.TryGetSteamPersonaName();
        Check(ModBehaviour.Logs.Count == 1, "primary invocation warning is logged once");
        Check(HarmonyLib.AccessTools.Lookups.Count == 2, "both resolved MethodInfo caches survive repeated calls");
        FakeManager.Throw = true;
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == null, "dual invocation failures safely return null");
        SteamPlatformInfo.TryGetSteamPersonaName();
        Check(ModBehaviour.Logs.Count == 2, "fallback invocation warning is independently logged once");

        Reset();
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == null, "missing Steam assemblies return null");
        SteamPlatformInfo.TryGetSteamPersonaName();
        Check(ModBehaviour.Logs.Count == 2 && HarmonyLib.AccessTools.Lookups.Count == 4, "missing lookups retry while warning only once per target");
        HarmonyLib.AccessTools.Types["SteamManager"] = typeof(FakeManager); FakeManager.Name = "late";
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == "late", "later assembly availability is not blocked by negative caching");
        FakeManager.Name = 42;
        Check(SteamPlatformInfo.TryGetSteamPersonaName() == null, "non-string compatibility result remains a safe miss");
        Console.WriteLine("PASS SteamPlatformInfo: " + assertions + " assertions");
    }
}
