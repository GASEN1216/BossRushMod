using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    // 接在既有 Health.Hurt 补丁上：官方完成护甲/暴击/元素计算后、生命上限钳制与死亡之前。
    // 不能在 OnHurt 里补血：致命一击先发 OnDead，marker 已注销，护盾根本没有机会吸收。
    internal static class ZombieModeDamageRuntime
    {
        internal static void ReduceFinalDamage(Health health, ref DamageInfo info)
        {
            ModBehaviour owner = ModBehaviour.Instance;
            if (owner == null || !owner.IsZombieModeActive || health == null || health.IsDead
                || info.fromCharacter == null
                || !(info.finalDamage > 0f) || float.IsInfinity(info.finalDamage)) return;

            CharacterMainControl target = health.TryGetCharacter();
            ZombieModeEnemyRuntimeMarker marker;
            if (target == null || !owner.TryGetZombieModeKnownEnemyMarker(target, out marker)
                || marker == null || marker.RunId != owner.ZombieModeCurrentRunId
                || marker.DeathSettled || marker.RemovedFromRuntime) return;

            owner.ApplyZombieModeEnemyDefense(health, ref info, marker);
            if (!info.fromCharacter.IsMainCharacter) return;

            float absorbed = 0f;
            if (marker.IsBoss)
            {
                absorbed = owner.AbsorbZombieModeBossFinalDamage(target, marker, info.finalDamage);
            }
            else
            {
                ZombieModeBossShieldRuntime shield = marker.AllyShield;
                if (shield != null && shield.IsShieldActive()) absorbed = shield.AbsorbDamage(info.finalDamage);
                absorbed += owner.ApplyZombieModeShielderAuraFinalDamageReduction(target,
                    Mathf.Max(0f, info.finalDamage - absorbed));
            }
            info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);
        }

        internal static IEnumerable<CodeInstruction> InjectBeforeHealthLoss(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo finalDamage = AccessTools.Field(typeof(DamageInfo), nameof(DamageInfo.finalDamage));
            MethodInfo getHealth = AccessTools.PropertyGetter(typeof(Health), nameof(Health.CurrentHealth));
            MethodInfo setHealth = AccessTools.PropertySetter(typeof(Health), nameof(Health.CurrentHealth));
            int firstStore = -1;
            int stores = 0;
            int subtract = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Stfld && Equals(codes[i].operand, finalDamage))
                {
                    if (firstStore < 0) firstStore = i;
                    stores++;
                }
                if (i >= 4 && codes[i].Calls(setHealth) && codes[i - 1].opcode == OpCodes.Sub
                    && codes[i - 2].opcode == OpCodes.Ldfld && Equals(codes[i - 2].operand, finalDamage)
                    && codes[i - 4].Calls(getHealth)) subtract = i;
            }
            // 两次写入分别为元素累加值与过量伤害钳制。未知版本拒绝安装，交给逐类补丁诊断。
            if (stores != 2 || firstStore < 2 || subtract <= firstStore
                || (codes[firstStore - 2].opcode != OpCodes.Ldarga_S && codes[firstStore - 2].opcode != OpCodes.Ldarga)
                || Convert.ToInt32(codes[firstStore - 2].operand) != 1)
                throw new InvalidOperationException("[ZombieMode] Health.Hurt finalDamage / health-loss IL changed");

            codes.InsertRange(firstStore + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarga_S, (byte)1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ZombieModeDamageRuntime), nameof(ReduceFinalDamage)))
            });
            return codes;
        }
    }
}
