#if BOSSRUSH_DEV
using System;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    // 原版错误栈不带实例。只观察基地箱子的 Start：报错给实物完整路径；嫌疑箱正常启动也留一条对照。
    [HarmonyPatch(typeof(InteractableLootbox), "Start")]
    internal static class F3BaseLootboxProbe
    {
        [HarmonyFinalizer]
        private static Exception Finalizer(InteractableLootbox __instance, Exception __exception)
        {
            try
            {
                if (__instance == null || !ModBehaviour.IsBaseHubSceneName(__instance.gameObject.scene.name)) return __exception;
                Transform t = __instance.transform;
                if (__exception == null && !(t.name == "LootBox" && t.parent != null && t.parent.name == "Pfb_PortaPotty_Base"))
                    return null;
                string path = t.name;
                while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
                string data = "[BossRushValidation] BASE_BOX_START result=" + (__exception == null ? "ok" : "failed")
                    + " scene=" + __instance.gameObject.scene.path + " path=" + path + " instance=" + __instance.GetInstanceID()
                    + " position=" + __instance.transform.position.ToString("F2") + " level_manager=" + (LevelManager.Instance != null);
                if (__exception != null) Debug.LogError(data + " exception=" + __exception.GetType().Name, __instance);
                else Debug.Log(data, __instance);
            }
            catch (Exception) { /* 只观察；永远保留抛出者自己的异常。 */ }
            return __exception;
        }
    }
}
#endif
