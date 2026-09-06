// 冰霜/雷霆回血只消费官方已经算出的元素贡献，不事后猜测护甲、暴击或耐久破损。
// 观察点：Health.Hurt 每元素最低伤害处理之后、累加到 finalDamage 之前。
// 原算式和控制流保持不变；IL 不匹配时整类补丁明确失败，回血缺证据即跳过。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class SetBonusDamageObservation
    {
        internal sealed class Observation
        {
            internal Observation Parent;
            internal ModBehaviour Owner;
            internal Health Target;
            internal List<ElementFactor> Factors;
            internal float Total;
            internal float Ice;
            internal float Electricity;
            internal bool HasContribution;
        }

        [ThreadStatic] private static Observation current;
        private static bool supported;
        private static bool missingReported;
        private static string supportDetail = "Health.Hurt 元素观察补丁尚未安装";

        internal static bool IsSupported { get { return supported; } }
        internal static string SupportDetail { get { return supportDetail; } }

        [HarmonyPrefix]
        private static void Begin(Health __instance, DamageInfo damageInfo, out Observation __state)
        {
            __state = null;
            ModBehaviour owner = ModBehaviour.Instance;
            bool observe = supported && owner != null && owner.HasSetBonusElementHealing
                && __instance != null && __instance.IsMainCharacterHealth;
            // 不穿套装时零上下文分配。正在观察的外层若嵌套另一 Hurt，也要放一层屏障，
            // 避免同一 Health 的重入把两个命中的贡献混到一起。
            if (!observe && current == null) return;
            __state = new Observation
            {
                Parent = current,
                Owner = observe ? owner : null,
                Target = __instance,
                Factors = damageInfo.elementFactors
            };
            current = __state;
        }

        [HarmonyFinalizer]
        private static Exception End(Exception __exception, Observation __state)
        {
            if (__state != null)
            {
                current = __state.Parent;
            }
            return __exception;
        }

        // 仅加法采样，没有游戏 API 调用、分配或异常分支，不影响原伤害循环。
        private static void Observe(Health target, ElementTypes element, float damage)
        {
            Observation observation = current;
            if (observation == null || observation.Owner == null
                || !object.ReferenceEquals(observation.Target, target)) return;
            observation.HasContribution = true;
            observation.Total += damage;
            if (element == ElementTypes.ice) observation.Ice += damage;
            if (element == ElementTypes.electricity) observation.Electricity += damage;
        }

        internal static float GetElementDamagePortion(Health health, DamageInfo info, ElementTypes element)
        {
            if (health == null || !(info.finalDamage > 0f) || float.IsInfinity(info.finalDamage)) return 0f;
            Observation observation = current;
            if (!supported || observation == null || observation.Owner == null
                || !object.ReferenceEquals(observation.Owner, ModBehaviour.Instance)
                || !observation.Owner.HasSetBonusElementHealing
                || !object.ReferenceEquals(observation.Target, health)
                || !object.ReferenceEquals(observation.Factors, info.elementFactors)
                || !observation.HasContribution)
            {
                ReportMissingObservation();
                return 0f;
            }

            float contribution = element == ElementTypes.ice ? observation.Ice
                : element == ElementTypes.electricity ? observation.Electricity : 0f;
            if (!(observation.Total > 0f) || float.IsInfinity(observation.Total)
                || !(contribution > 0f) || float.IsInfinity(contribution)) return 0f;
            // finalDamage 可能被官方剩余生命上限钳制。按真实贡献分摊该最终值，
            // 单一元素绝不超过本击总伤害；免疫/负贡献元素不产生治疗。
            return Mathf.Clamp(info.finalDamage * (contribution / observation.Total), 0f, info.finalDamage);
        }

        private static void ReportMissingObservation()
        {
            if (missingReported) return;
            missingReported = true;
            ModBehaviour.CriticalLog("set-bonus-damage-observation",
                "[SetBonus] [WARNING] 本次元素伤害缺少官方贡献记录，跳过回血而保留原伤害。" + supportDetail);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int elementCall;
            int sumAt;
            string failure;
            if (!TryFindObservationPoint(codes, out elementCall, out sumAt, out failure))
            {
                supported = false;
                supportDetail = "Health.Hurt IL 不匹配: " + failure;
                // 由既有逐类 apply 隔离并 CriticalLog 上报，禁止把未匹配的补丁记成已支持。
                throw new InvalidOperationException(supportDetail);
            }

            var injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(codes[elementCall - 2].opcode, codes[elementCall - 2].operand),
                new CodeInstruction(codes[elementCall - 1].opcode, codes[elementCall - 1].operand),
                new CodeInstruction(codes[sumAt + 1].opcode, codes[sumAt + 1].operand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SetBonusDamageObservation), nameof(Observe)))
            };
            // 原分支跳到累加点时也必须经过观察，异常块边界随原指令入口一起迁移。
            injected[0].labels.AddRange(codes[sumAt].labels);
            injected[0].blocks.AddRange(codes[sumAt].blocks);
            codes[sumAt].labels.Clear();
            codes[sumAt].blocks.Clear();
            codes.InsertRange(sumAt, injected);
            supported = true;
            missingReported = false;
            supportDetail = "Health.Hurt 元素最低伤害之后的唯一累加点已验证";
            return codes;
        }

        // 用调用/字段/局部变量的数据流定位，不依赖官方局部变量编号。
        // 必须同时命中：ElementFactor → 连乘结果 → 最低 1 点赋值 → 总和累加 → finalDamage。
        internal static bool TryFindObservationPoint(IList<CodeInstruction> codes,
            out int elementCall, out int sumAt, out string failure)
        {
            elementCall = -1;
            sumAt = -1;
            failure = "找不到唯一元素抗性调用";
            MethodInfo factorMethod = AccessTools.Method(typeof(Health), nameof(Health.ElementFactor));
            FieldInfo elementField = AccessTools.Field(typeof(ElementFactor), nameof(ElementFactor.elementType));
            FieldInfo finalField = AccessTools.Field(typeof(DamageInfo), nameof(DamageInfo.finalDamage));
            for (int i = 2; i < codes.Count; i++)
            {
                if (!codes[i].Calls(factorMethod)) continue;
                if (elementCall >= 0) return false;
                elementCall = i;
            }
            if (elementCall < 2 || elementCall + 7 >= codes.Count
                || codes[elementCall - 1].opcode != OpCodes.Ldfld
                || !object.Equals(codes[elementCall - 1].operand, elementField)
                || (codes[elementCall - 2].opcode != OpCodes.Ldloca
                    && codes[elementCall - 2].opcode != OpCodes.Ldloca_S
                    && LocalIndex(codes[elementCall - 2], false) < 0)) return false;

            failure = "元素伤害连乘结构变化";
            int resistanceLocal = LocalIndex(codes[elementCall + 1], true);
            int valueStore = elementCall + 7;
            int damageLocal = LocalIndex(codes[valueStore], true);
            if (resistanceLocal < 0 || damageLocal < 0
                || LocalIndex(codes[elementCall + 2], false) < 0
                || LocalIndex(codes[elementCall + 3], false) < 0
                || codes[elementCall + 4].opcode != OpCodes.Mul
                || LocalIndex(codes[elementCall + 5], false) != resistanceLocal
                || codes[elementCall + 6].opcode != OpCodes.Mul) return false;

            failure = "找不到唯一元素伤害累加点";
            for (int i = valueStore + 1; i + 3 < codes.Count; i++)
            {
                int sumLocal = LocalIndex(codes[i], false);
                if (sumLocal < 0 || sumLocal == damageLocal
                    || LocalIndex(codes[i + 1], false) != damageLocal
                    || codes[i + 2].opcode != OpCodes.Add
                    || LocalIndex(codes[i + 3], true) != sumLocal) continue;
                if (sumAt >= 0) return false;
                sumAt = i;
            }
            if (sumAt < 0) return false;

            failure = "元素最低伤害或 finalDamage 赋值结构变化";
            bool minimumFound = false;
            for (int i = valueStore + 1; i + 1 < sumAt; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && object.Equals(codes[i].operand, 1f)
                    && LocalIndex(codes[i + 1], true) == damageLocal) minimumFound = true;
            }
            int totalLocal = LocalIndex(codes[sumAt], false);
            bool finalFound = false;
            for (int i = sumAt + 4; i + 1 < codes.Count; i++)
            {
                if (LocalIndex(codes[i], false) == totalLocal && codes[i + 1].opcode == OpCodes.Stfld
                    && object.Equals(codes[i + 1].operand, finalField)) finalFound = true;
            }
            if (!minimumFound || !finalFound) return false;
            failure = null;
            return true;
        }

        private static int LocalIndex(CodeInstruction instruction, bool store)
        {
            OpCode op = instruction.opcode;
            if (op == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
            if (op == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
            if (op == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
            if (op == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
            if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc)
                && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
            LocalVariableInfo local = instruction.operand as LocalVariableInfo;
            return local != null ? local.LocalIndex : Convert.ToInt32(instruction.operand);
        }
    }
}
