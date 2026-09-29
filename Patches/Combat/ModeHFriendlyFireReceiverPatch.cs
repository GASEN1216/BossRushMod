// ============================================================================
// ModeHFriendlyFireReceiverPatch.cs - 鸭王杯群战同队伤害屏障的拦截点（2026-09-29）
// ============================================================================
// owner 实测：鸭王杯群战里一个队的 Boss 会伤害自己的队友。
// 官方子弹本来就不打同队（Projectile 对 context.team 与受击者同队直接跳过）；漏网的是爆炸
// （ExplosionManager 在 canHurtSelf 时按 Teams.all 谁都炸）、近战挥砍的范围判定与部分 Boss 技能。
// 这三条都经 DamageReceiver.Hurt(DamageInfo) 进入受伤流程，在这里能同时看到攻击者与受击者。
//
// 为什么不挂在 Health.Hurt 上：那条链只允许 BossRushHealthHurtContextPatch 一个准入补丁，
// 签名是冻结字面量（ReverseScale / Mode G / 遗种巢守卫都盯着），不能加 DamageInfo 参数。
// Mode H 目录不允许新增 Harmony 补丁（ModeHStandInGuard），跨模块的受伤拦截点放在 Patches/Combat。
//
// 契约：先读 ModeHFriendlyFireBarrier.IsArmed 静态 bool 快速早返（非鸭王杯群战零开销）；
// 只拦「攻击者与受击者同队、都不是玩家、不是自伤」；判定 no-throw，异常一律放行。
// ============================================================================

using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(DamageReceiver), nameof(DamageReceiver.Hurt), new System.Type[] { typeof(DamageInfo) })]
    internal static class ModeHFriendlyFireReceiverPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DamageReceiver __instance, DamageInfo damageInfo, ref bool __result)
        {
            if (!ModeHFriendlyFireBarrier.IsArmed) return true;
            Health health = null;
            try { health = __instance != null ? __instance.health : null; }
            catch { return true; }
            if (!ModeHFriendlyFireBarrier.ShouldBlock(health, damageInfo.fromCharacter)) return true;
            __result = false;
            return false;
        }
    }
}
