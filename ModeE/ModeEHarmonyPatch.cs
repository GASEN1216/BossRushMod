// ============================================================================
// ModeEHarmonyPatch.cs - Mode E 阵营保护 Harmony Patch
// ============================================================================
// 修复原版 ItemAgent_Gun.ShootOneBullet 中的防作弊逻辑：
//   当玩家阵营非 player 时，开枪触发 SetTeam(Teams.all)。
//   Mode E 中玩家阵营是 scav/usec 等，导致阵营永久变为 all。
// 修复：Patch SetTeam，Mode E 中阻止主角被设为 Teams.all。
// ============================================================================

using HarmonyLib;

namespace BossRush
{
    /// <summary>
    /// Patch CharacterMainControl.SetTeam：
    /// Mode E 中阻止主角阵营被篡改为 Teams.all
    /// </summary>
    [HarmonyPatch(typeof(CharacterMainControl), "SetTeam")]
    public static class ModeESetTeamPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CharacterMainControl __instance, Teams _team)
        {
            // 非 Mode E 或非 Teams.all 时放行
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive || _team != Teams.all)
                return true;

            // 只保护主角
            if (!__instance.IsMainCharacter)
                return true;

            // 阻止 SetTeam(Teams.all)，保持玩家正确阵营
            return false;
        }
    }
}
