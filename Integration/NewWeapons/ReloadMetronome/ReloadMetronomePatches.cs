using System;
using HarmonyLib;
using ItemStatsSystem;

namespace BossRush
{
    [HarmonyPatch(typeof(ItemAgent_Gun), nameof(ItemAgent_Gun.BeginReload), new Type[] { })]
    internal static class ReloadMetronomeBeginPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ItemAgent_Gun __instance, bool __result)
        {
            if (!__result) return;
            try { ReloadMetronomeRuntime.OnReloadStarted(__instance); }
            catch (Exception e) { ModBehaviour.DevLog("[ReloadMetronome] 绑定换弹失败: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.TryToReload), new Type[] { typeof(Item) })]
    internal static class ReloadMetronomeInputPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(CharacterMainControl __instance, Item preferedBulletToLoad)
        {
            try { ReloadMetronomeRuntime.OnReloadRequested(__instance, preferedBulletToLoad); }
            catch (Exception e) { ModBehaviour.DevLog("[ReloadMetronome] 换弹判定失败: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemAgent_Gun), nameof(ItemAgent_Gun.UpdateStates), new Type[] { })]
    internal static class ReloadMetronomeFinishPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ItemAgent_Gun __instance, ref float ___stateTimer)
        {
            try { ReloadMetronomeRuntime.BeforeGunUpdate(__instance, ref ___stateTimer); }
            catch { /* 热路径失败时仍执行官方换弹，不刷日志。 */ }
        }
    }

    [HarmonyPatch(typeof(ItemAgent_Gun), nameof(ItemAgent_Gun.CancleReload), new Type[] { })]
    internal static class ReloadMetronomeCancelPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ItemAgent_Gun __instance)
        {
            try { ReloadMetronomeRuntime.OnReloadCancelled(__instance); }
            catch (Exception e) { ModBehaviour.DevLog("[ReloadMetronome] 取消换弹失败: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(AimMarker), "LateUpdate", new Type[] { })]
    internal static class ReloadMetronomeHudPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(AimMarker __instance)
        {
            try { ReloadMetronomeRuntime.Present(__instance); }
            catch { /* UI 不可用不影响原版准星和装填。 */ }
        }
    }
}
