using System;
using HarmonyLib;

namespace BossRush
{
    /// <summary>
    /// WIRE+：玩家开枪时把弹道告诉云蚋（`Projectile.Init(ProjectileContext)` 后置补丁；官方反编译源 `Projectile.cs:181`，
    /// 霰弹每一丸各调一次，`ItemAgent_Gun` 先摆好位置再调 Init）。
    ///
    /// - 不在天空岛（没有 <see cref="SkyIslandGnats.Current"/>）时只做一次静态判空就返回，官方地图零行为变化；
    /// - 只看主角自己的直线弹，有重力或会爆炸的弹不预测（爆炸与范围伤害本来就该打得死云蚋），过滤在 <see cref="SkyIslandMosquitoRules.ShouldTrackProjectile"/>；
    /// - 预测与异常处理都在 <see cref="SkyIslandGnats.OnProjectile"/> 里，异常一律吞掉，不进官方开枪流程。
    /// 目标方法有无参与带 `ProjectileContext` 两个重载，按参数类型点名。
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "Init", new Type[] { typeof(ProjectileContext) })]
    internal static class SkyIslandGnatProjectilePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Projectile __instance, ProjectileContext _context)
        {
            SkyIslandGnats swarm = SkyIslandGnats.Current;
            if (swarm == null) return;
            swarm.OnProjectile(__instance, _context);
        }
    }
}
