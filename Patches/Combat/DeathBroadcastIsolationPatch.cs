// ============================================================================
// DeathBroadcastIsolationPatch.cs - 官方死亡 / 受伤广播的逐订阅者异常隔离
// ============================================================================
// 问题：官方 Health.Hurt 的致死顺序是
//     OnDeadEvent（实例 UnityEvent）-> Health.OnDead（静态多播）-> SetActive(false) -> OnHurtEvent -> Health.OnHurt
// 全程没有 try。任何一个订阅者抛异常（创意工坊击杀提示类 Mod 不判空攻击者，流血 / 毒区这类
// 无攻击者死亡必抛 NRE），排在它后面的订阅者全部收不到这次死亡，后续 SetActive(false) 也不执行；
// 而 DestroyOnDelay 早已启动，尸体照常消失。依赖死亡回调推进的模式（丧尸 Boss 波、宿命回响、
// 标准竞技场、Mode D/E/F/H、天空岛 Boss、征程终局……）就会出现「Boss 不见了却卡关」。
// 做法：把这几处广播的调用点换成逐订阅者 try/catch 的等价调用，顺序与参数不变，
// 只在有订阅者抛异常时记录（5 秒节流）并继续通知下一个。所有模式、官方与第三方订阅者一并受益，
// 不需要各模式改订阅写法。
//
// 覆盖面：
//   Health.Hurt：OnDeadEvent.Invoke、Health.OnDead、Health.OnHurt（OnHurtEvent 每击都发，不做反射隔离）。
//   CharacterMainControl.OnDead：BeforeCharacterSpawnLootOnDead（掉落钩子抛异常不再吞掉死亡箱与后续监听）。
// 性能：静态多播按委托引用缓存调用列表，稳态零分配；实例 UnityEvent 只在死亡时走一次反射。
// 失配：IL 找不到预期形态时原样返回并告警，不抛异常（独立补丁类，不牵连其它 Hurt 补丁）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    // 独立补丁类：只改写广播调用点，不改伤害、准入与返回值。
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class HealthDeathBroadcastIsolationPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int deathEvent, death, hurt;
            List<CodeInstruction> codes = DeathBroadcastIsolation.IsolateHurtBroadcasts(
                instructions, out deathEvent, out death, out hurt);
            if (deathEvent != 1 || death != 1 || hurt != 1)
            {
                Debug.LogWarning("[BossRush] Health.Hurt 广播隔离只命中部分调用点（OnDeadEvent="
                    + deathEvent + ", OnDead=" + death + ", OnHurt=" + hurt + "），未命中的保持官方原样");
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
    internal static class CharacterLootBroadcastIsolationPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int rewritten;
            List<CodeInstruction> codes = DeathBroadcastIsolation.IsolateBeforeLootBroadcast(instructions, out rewritten);
            if (rewritten != 1)
            {
                Debug.LogWarning("[BossRush] CharacterMainControl.OnDead 掉落广播隔离未命中（" + rewritten + " 处），保持官方原样");
            }
            return codes;
        }
    }

    internal static class DeathBroadcastIsolation
    {
        // 字段读取到调用之间的最大指令数：官方形态是 ldsfld / dup / brtrue / pop / br / ldarg / ldarg / callvirt。
        private const int BroadcastCallWindow = 8;
        private const int FaultLogIntervalMilliseconds = 5000;

        // 静态多播的调用列表缓存：静态事件的委托只在订阅 / 退订时换新，命中时不分配。
        private static Action<Health, DamageInfo> cachedDeathSource;
        private static Action<Health, DamageInfo>[] cachedDeathList;
        private static Action<Health, DamageInfo> cachedHurtSource;
        private static Action<Health, DamageInfo>[] cachedHurtList;

        // UnityEvent 内部成员（UnityEventBase.PrepareInvoke 与 BaseInvokableCall.Invoke(object[])），
        // 与官方 UnityEvent<T>.Invoke 遍历的是同一份监听表；解析失败退回整体调用 + catch。
        private static bool unityEventInternalsResolved;
        private static MethodInfo prepareInvokeMethod;
        private static MethodInfo invokableCallInvokeMethod;

        private static int lastFaultLogTick;
        private static bool hasLoggedFault;

        /// <summary>被隔离下来的订阅者异常次数（诊断与执行回归读取）。</summary>
        internal static int SubscriberFaultCount;

        // ------------------------------------------------------------------
        // IL 改写（纯函数，执行回归直接对本机官方 IL 调用）
        // ------------------------------------------------------------------

        internal static List<CodeInstruction> IsolateHurtBroadcasts(
            IEnumerable<CodeInstruction> instructions, out int deathEvent, out int death, out int hurt)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            deathEvent = RewriteBroadcastCalls(codes,
                AccessTools.Field(typeof(Health), "OnDeadEvent"),
                typeof(UnityEvent<DamageInfo>),
                AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(InvokeDeathUnityEvent)));
            death = RewriteBroadcastCalls(codes,
                AccessTools.Field(typeof(Health), "OnDead"),
                typeof(Action<Health, DamageInfo>),
                AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(InvokeDeathBroadcast)));
            hurt = RewriteBroadcastCalls(codes,
                AccessTools.Field(typeof(Health), "OnHurt"),
                typeof(Action<Health, DamageInfo>),
                AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(InvokeHurtBroadcast)));
            return codes;
        }

        internal static List<CodeInstruction> IsolateBeforeLootBroadcast(
            IEnumerable<CodeInstruction> instructions, out int rewritten)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            rewritten = RewriteBroadcastCalls(codes,
                AccessTools.Field(typeof(CharacterMainControl), "BeforeCharacterSpawnLootOnDead"),
                typeof(Action<DamageInfo>),
                AccessTools.Method(typeof(DeathBroadcastIsolation), nameof(InvokeBeforeLootBroadcast)));
            return codes;
        }

        /// <summary>
        /// 找到「读广播字段」之后窗口内的第一处 Invoke 调用，原地换成等栈形的静态隔离调用
        /// （委托 / 事件实例与参数原样留在栈上），标签与异常块不动。返回改写处数。
        /// </summary>
        private static int RewriteBroadcastCalls(List<CodeInstruction> codes, FieldInfo field, Type delegateType, MethodInfo isolated)
        {
            if (field == null || isolated == null)
            {
                return 0;
            }

            int rewritten = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!ReadsField(codes[i], field))
                {
                    continue;
                }

                int end = Math.Min(codes.Count, i + 1 + BroadcastCallWindow);
                for (int j = i + 1; j < end; j++)
                {
                    if (ReadsField(codes[j], field))
                    {
                        break;
                    }

                    MethodInfo target = codes[j].operand as MethodInfo;
                    if ((codes[j].opcode == OpCodes.Call || codes[j].opcode == OpCodes.Callvirt) &&
                        target != null && target.Name == "Invoke" && target.DeclaringType == delegateType)
                    {
                        codes[j].opcode = OpCodes.Call;
                        codes[j].operand = isolated;
                        rewritten++;
                        break;
                    }
                }
            }
            return rewritten;
        }

        private static bool ReadsField(CodeInstruction instruction, FieldInfo field)
        {
            return (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldsfld) &&
                   Equals(instruction.operand, field);
        }

        // ------------------------------------------------------------------
        // 隔离调用（替换官方调用点；栈形与原 Invoke 一致）
        // ------------------------------------------------------------------

        internal static void InvokeDeathBroadcast(Action<Health, DamageInfo> broadcast, Health health, DamageInfo damageInfo)
        {
            InvokeIsolated(broadcast, health, damageInfo, ref cachedDeathSource, ref cachedDeathList, "Health.OnDead");
        }

        internal static void InvokeHurtBroadcast(Action<Health, DamageInfo> broadcast, Health health, DamageInfo damageInfo)
        {
            InvokeIsolated(broadcast, health, damageInfo, ref cachedHurtSource, ref cachedHurtList, "Health.OnHurt");
        }

        private static void InvokeIsolated(
            Action<Health, DamageInfo> broadcast,
            Health health,
            DamageInfo damageInfo,
            ref Action<Health, DamageInfo> cachedSource,
            ref Action<Health, DamageInfo>[] cachedList,
            string channel)
        {
            if (broadcast == null)
            {
                return;
            }

            Action<Health, DamageInfo>[] list = cachedList;
            if (list == null || !ReferenceEquals(broadcast, cachedSource))
            {
                Delegate[] raw = broadcast.GetInvocationList();
                list = new Action<Health, DamageInfo>[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    list[i] = (Action<Health, DamageInfo>)raw[i];
                }
                cachedSource = broadcast;
                cachedList = list;
            }

            // 本地持有快照：订阅者在回调里订阅 / 退订、或嵌套死亡换掉缓存，都不影响本轮（与多播语义一致）。
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    list[i](health, damageInfo);
                }
                catch (Exception exception)
                {
                    ReportSubscriberFault(channel, list[i], exception);
                }
            }
        }

        internal static void InvokeBeforeLootBroadcast(Action<DamageInfo> broadcast, DamageInfo damageInfo)
        {
            if (broadcast == null)
            {
                return;
            }

            // 每个角色一份实例委托，缓存命中率低；只在死亡时发生，分配可忽略。
            Delegate[] list = broadcast.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                Action<DamageInfo> handler = (Action<DamageInfo>)list[i];
                try
                {
                    handler(damageInfo);
                }
                catch (Exception exception)
                {
                    ReportSubscriberFault("CharacterMainControl.BeforeCharacterSpawnLootOnDead", handler, exception);
                }
            }
        }

        internal static void InvokeDeathUnityEvent(UnityEvent<DamageInfo> deathEvent, DamageInfo damageInfo)
        {
            if (deathEvent == null)
            {
                return;
            }

            IList calls = null;
            if (EnsureUnityEventInternals())
            {
                try
                {
                    calls = prepareInvokeMethod.Invoke(deathEvent, null) as IList;
                }
                catch (Exception)
                {
                    calls = null;
                }
            }

            if (calls == null)
            {
                try
                {
                    deathEvent.Invoke(damageInfo);
                }
                catch (Exception exception)
                {
                    ReportSubscriberFault("Health.OnDeadEvent", null, exception);
                }
                return;
            }

            // 与官方 UnityEvent<T>.Invoke 相同：PrepareInvoke 给出本轮执行快照，逐个调用；
            // 每个监听器单独 try，前一个抛异常不再截断后面的监听器。
            object[] arguments = null;
            object[] invokeParameters = null;
            for (int i = 0; i < calls.Count; i++)
            {
                object call = calls[i];
                if (call == null)
                {
                    continue;
                }

                if (invokeParameters == null)
                {
                    arguments = new object[] { damageInfo };
                    invokeParameters = new object[] { arguments };
                }

                try
                {
                    invokableCallInvokeMethod.Invoke(call, invokeParameters);
                }
                catch (TargetInvocationException exception)
                {
                    ReportSubscriberFault("Health.OnDeadEvent", null, exception.InnerException ?? exception);
                }
                catch (Exception exception)
                {
                    ReportSubscriberFault("Health.OnDeadEvent", null, exception);
                }
            }
        }

        internal static bool EnsureUnityEventInternals()
        {
            if (unityEventInternalsResolved)
            {
                return prepareInvokeMethod != null && invokableCallInvokeMethod != null;
            }

            unityEventInternalsResolved = true;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo prepare = null;
                for (Type type = typeof(UnityEvent<DamageInfo>); type != null && prepare == null; type = type.BaseType)
                {
                    prepare = type.GetMethod("PrepareInvoke", flags, null, Type.EmptyTypes, null);
                }

                if (prepare == null || !typeof(IList).IsAssignableFrom(prepare.ReturnType) ||
                    !prepare.ReturnType.IsGenericType)
                {
                    return false;
                }

                Type callType = prepare.ReturnType.GetGenericArguments()[0];
                MethodInfo invoke = callType.GetMethod("Invoke", flags, null, new Type[] { typeof(object[]) }, null);
                if (invoke == null)
                {
                    return false;
                }

                prepareInvokeMethod = prepare;
                invokableCallInvokeMethod = invoke;
                return true;
            }
            catch (Exception)
            {
                prepareInvokeMethod = null;
                invokableCallInvokeMethod = null;
                return false;
            }
        }

        private static void ReportSubscriberFault(string channel, Delegate handler, Exception exception)
        {
            SubscriberFaultCount++;
            try
            {
                int now = Environment.TickCount;
                if (hasLoggedFault && unchecked(now - lastFaultLogTick) < FaultLogIntervalMilliseconds)
                {
                    return;
                }

                hasLoggedFault = true;
                lastFaultLogTick = now;
                Debug.LogWarning("[BossRush] " + channel + " 的订阅者抛异常，已隔离，其余订阅者照常收到（"
                    + DescribeHandler(handler) + "）: " + exception);
            }
            catch
            {
                // 诊断失败不得影响宿主死亡流程
            }
        }

        private static string DescribeHandler(Delegate handler)
        {
            if (handler == null || handler.Method == null)
            {
                return "监听器见异常栈";
            }

            Type owner = handler.Method.DeclaringType;
            string assembly = owner != null ? owner.Assembly.GetName().Name : "?";
            return assembly + ": " + (owner != null ? owner.FullName : "?") + "." + handler.Method.Name;
        }

        /// <summary>Mod 卸载时释放缓存的委托引用（与 Harmony 解绑同一清理路径）。</summary>
        internal static void ResetStaticCaches()
        {
            cachedDeathSource = null;
            cachedDeathList = null;
            cachedHurtSource = null;
            cachedHurtList = null;
            hasLoggedFault = false;
            lastFaultLogTick = 0;
            SubscriberFaultCount = 0;
        }
    }
}
