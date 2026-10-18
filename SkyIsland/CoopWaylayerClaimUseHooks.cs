using System;
using HarmonyLib;
using ItemStatsSystem;

namespace BossRush
{
    // A private original stays unusable until its native save and readback publish it.
    // These gates follow the exact instance marker, including a use started before locking.
    [HarmonyPatch(typeof(CharacterMainControl), "UseItem", new Type[] { typeof(Item) })]
    internal static class CoopWaylayerClaimStartUseHook
    {
        [HarmonyPrefix] private static bool Prefix(Item item) { return !CoopWaylayerClaims.Locked(item); }
    }

    [HarmonyPatch(typeof(UsageUtilities), "IsUsable", new Type[] { typeof(Item), typeof(object) })]
    internal static class CoopWaylayerClaimUsableHook
    {
        [HarmonyPrefix] private static bool Prefix(Item item, ref bool __result)
        { if (!CoopWaylayerClaims.Locked(item)) return true; __result = false; return false; }
    }

    [HarmonyPatch(typeof(Item), "Use", new Type[] { typeof(object), typeof(bool) })]
    internal static class CoopWaylayerClaimUseHook
    {
        // Reject before onUse/onUseStatic can perform effects of their own.
        [HarmonyPrefix] private static bool Prefix(Item __instance) { return !CoopWaylayerClaims.Locked(__instance); }
    }

    [HarmonyPatch(typeof(UsageUtilities), "Use", new Type[] { typeof(Item), typeof(object), typeof(bool) })]
    internal static class CoopWaylayerClaimUtilitiesUseHook
    {
        [HarmonyPrefix] private static bool Prefix(Item item) { return !CoopWaylayerClaims.Locked(item); }
    }

    [HarmonyPatch(typeof(UsageBehavior), "Use", new Type[] { typeof(Item), typeof(object) })]
    internal static class CoopWaylayerClaimBehaviorUseHook
    {
        [HarmonyPrefix] private static bool Prefix(Item item) { return !CoopWaylayerClaims.Locked(item); }
    }

    [HarmonyPatch(typeof(CA_UseItem), "OnFinish")]
    internal static class CoopWaylayerClaimFinishUseHook
    {
        // The official finish decrements Count after Item.Use even if that call was skipped.
        // Its caller still stops the action and returns the held agent normally.
        [HarmonyPrefix] private static bool Prefix(Item ___item) { return !CoopWaylayerClaims.Locked(___item); }
    }
}
