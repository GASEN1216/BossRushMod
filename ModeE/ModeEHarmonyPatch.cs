// ============================================================================
// ModeEHarmonyPatch.cs - Mode E Harmony Patches
// ============================================================================
// 包含：
//   1. SetTeam 阵营保护 Patch：阻止原版防作弊逻辑篡改玩家阵营
//   2. HealthBar 友方血条绿色 Patch：同阵营单位血条显示为绿色
// ============================================================================

using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Duckov.UI;

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

    /// <summary>
    /// Patch HealthBar.Refresh：
    /// Mode E 中将同阵营友方单位的血条颜色覆盖为绿色
    /// </summary>
    [HarmonyPatch(typeof(HealthBar), "Refresh")]
    public static class ModeEHealthBarColorPatch
    {
        /// <summary>友方血条绿色（鲜明的绿色，易于辨识）</summary>
        private static readonly Color AllyHealthBarColor = new Color(0.2f, 0.9f, 0.2f, 1f);

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, Image ___fill)
        {
            // 非 Mode E 时跳过
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive)
                return;

            // 获取血条绑定的 Health 目标
            Health target = __instance.target;
            if (target == null) return;

            // 获取角色
            CharacterMainControl character = target.TryGetCharacter();
            if (character == null || character.IsMainCharacter) return;

            // 判断是否与玩家同阵营
            if (character.Team == inst.ModeEPlayerFaction)
            {
                // 同阵营友方：覆盖血条颜色为绿色
                if (___fill != null)
                {
                    ___fill.color = AllyHealthBarColor;
                }
            }
        }
    }
}
