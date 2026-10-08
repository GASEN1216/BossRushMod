// ============================================================================
// AstralStaffAttackPatch.cs - 星阙轻击的官方动作挂钩
// ============================================================================
// CA_Attack.OnStart 前缀：重击进行中不许官方轻击起手（不然左右键能叠着打，三档重击就没了分量）。
// CA_Attack.OnStart 后缀：真正起手后开一个新挥击编号（命中记段按编号去重），并画金色光弧——
//   三段轻击左右交替，第三段更宽更亮，读得出「一套连招」。
// 两段都先判「主玩家 + 手上是 500104」，其它武器零开销返回。
// ============================================================================

using System;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class AstralStaffAttackPatch
    {
        private static int comboStep;
        private static float lastSwingTime = -10f;

        /// <summary>当前轻击是三段连招的第几段（0..2，2 是收尾重段）；命中反馈按它分轻重。</summary>
        internal static int CurrentComboStep { get { return comboStep; } }

        [HarmonyPrefix]
        public static bool Prefix(CA_Attack __instance, ref bool __result)
        {
            try
            {
                if (!AstralStaffController.BlocksLightAttack) return true;
                CharacterMainControl character = __instance.characterController;
                if (character == null || character != CharacterMainControl.Main) return true;
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null || melee.Item.TypeID != AstralStaffConfig.TypeId) return true;
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(CA_Attack __instance, bool __result)
        {
            if (!__result) return;
            try
            {
                CharacterMainControl character = __instance.characterController;
                if (character == null || character != CharacterMainControl.Main) return;
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null || melee.Item.TypeID != AstralStaffConfig.TypeId) return;

                AstralStaffController.NotifyLightSwing();

                float now = Time.time;
                comboStep = now - lastSwingTime > 1.1f ? 0 : (comboStep + 1) % 3;
                lastSwingTime = now;

                Vector3 forward = character.CurrentAimDirection;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = character.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

                float rangeScale = Mathf.Max(0.3f, melee.AttackRange / AstralStaffConfig.AttackRange);
                bool finisher = comboStep == 2;
                float sweep = (comboStep == 1 ? -1f : 1f) * (finisher ? 170f : 130f);
                Vector3 at = character.transform.position + Vector3.up * 1.05f + forward * 0.15f;
                AstralStaffFx.PlayStroke(at, forward, sweep, AstralStaffConfig.AttackRange * rangeScale * (finisher ? 1.08f : 1f),
                    AstralStaffConfig.Gold, AstralStaffConfig.DealDamageTime, finisher ? 0.2f : 0.15f);

                AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
                if (hand != null) hand.SetSurge(finisher ? 0.38f : 0.2f);
            }
            catch { /* 表现失败不能影响攻击本身 */ }
        }

        internal static void ResetStaticCaches()
        {
            comboStep = 0;
            lastSwingTime = -10f;
        }
    }
}
