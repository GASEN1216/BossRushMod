// ============================================================================
// HarmonyBindingSelfCheck.cs - Harmony 绑定启动期自检
// ============================================================================
// 背景：
//   Mod 与官方游戏之间没有稳定 API 边界，全部靠 [HarmonyPatch] 的字符串方法名
//   动态绑定。官方更新改掉任一目标方法后，该补丁会静默不生效——功能无声死亡，
//   日志无痕（见 docs/架构说明/Harmony补丁契约稳定性.md 的 F1/F4/F5 失败模式）。
//
//   Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs 的
//   EnsureCriticalPatchesApplied 已经给出了正确范式：声明规格 → 校验实际挂载 →
//   汇总 verified/total。但它只覆盖 8 个动态物品方法。
//
// 本模块把该范式推广到全部补丁类：启动期只读校验每个 [HarmonyPatch] 类的目标
// 方法是否真的挂上了本 Mod 的补丁，失败清单经 CriticalLog 上报（不受
// BOSSRUSH_DEV 门控，玩家端可见）。
//
// 约束：
//   - 纯只读校验，不 apply、不补装、不改变任何补丁行为。
//   - 全程 try/catch 包裹，自检自身异常绝不影响启动流程。
//   - 只在启动期跑一次。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace BossRush
{
    internal static class HarmonyBindingSelfCheck
    {
        private static bool hasRun = false;

        /// <summary>
        /// 启动期自检：校验所有 [HarmonyPatch] 类的目标方法是否已实际挂载本 Mod 补丁。
        /// 只读，不改变补丁状态；异常不外抛。
        /// </summary>
        internal static void RunStartupSelfCheck(Harmony harmony)
        {
            if (harmony == null || hasRun)
            {
                return;
            }

            hasRun = true;

            try
            {
                int verified = 0;
                int total = 0;
                int skipped = 0;
                var failures = new List<string>();

                foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModBehaviour).Assembly))
                {
                    if (type == null)
                    {
                        continue;
                    }

                    object[] attributes;
                    try
                    {
                        attributes = type.GetCustomAttributes(typeof(HarmonyPatch), true);
                    }
                    catch
                    {
                        continue;
                    }

                    if (attributes == null || attributes.Length == 0)
                    {
                        continue;
                    }

                    // TargetMethod/TargetMethods 动态选目标的补丁类无法静态解析，跳过（不计入分母）
                    if (HasDynamicTargetSelector(type))
                    {
                        skipped++;
                        continue;
                    }

                    MethodBase original;
                    string label;
                    if (!TryResolveTarget(type, attributes, out original, out label))
                    {
                        skipped++;
                        continue;
                    }

                    total++;

                    if (original == null)
                    {
                        failures.Add(label + " (官方目标方法不存在)");
                        continue;
                    }

                    if (IsOwnedByHarmony(original, harmony.Id))
                    {
                        verified++;
                    }
                    else
                    {
                        failures.Add(label + " (补丁未生效)");
                    }
                }

                if (failures.Count > 0)
                {
                    ModBehaviour.CriticalLog("harmony-binding-self-check",
                        "[BossRush][HarmonySelfCheck] [ERROR] 补丁绑定校验 " + verified + "/" + total
                        + " 通过，失败 " + failures.Count + " 个（官方更新后极可能是目标方法已改名/改签名）: "
                        + string.Join(" | ", failures.ToArray()));
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush][HarmonySelfCheck] 补丁绑定校验通过: "
                        + verified + "/" + total + "，动态选目标跳过 " + skipped + " 个");
                }
            }
            catch (Exception e)
            {
                // 自检自身失败不能影响启动，但要有声
                ModBehaviour.CriticalLog("harmony-binding-self-check-crash",
                    "[BossRush][HarmonySelfCheck] [WARNING] 补丁绑定自检执行异常: "
                    + (e != null ? e.Message : "unknown"));
            }
        }

        internal static void ResetStaticCaches()
        {
            hasRun = false;
        }

        /// <summary>补丁类是否用 TargetMethod/TargetMethods 动态选目标</summary>
        private static bool HasDynamicTargetSelector(Type type)
        {
            try
            {
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                return type.GetMethod("TargetMethod", flags) != null
                    || type.GetMethod("TargetMethods", flags) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>从 [HarmonyPatch] 属性解析目标方法；无法静态解析时返回 false</summary>
        private static bool TryResolveTarget(Type type, object[] attributes, out MethodBase original, out string label)
        {
            original = null;
            label = type != null ? type.FullName : "<unknown>";

            try
            {
                var infos = new List<HarmonyMethod>();
                for (int i = 0; i < attributes.Length; i++)
                {
                    HarmonyPatch attribute = attributes[i] as HarmonyPatch;
                    if (attribute != null && attribute.info != null)
                    {
                        infos.Add(attribute.info);
                    }
                }

                if (infos.Count == 0)
                {
                    return false;
                }

                HarmonyMethod merged = HarmonyMethod.Merge(infos);
                if (merged == null || merged.declaringType == null)
                {
                    return false;
                }

                Type declaringType = merged.declaringType;
                string methodName = merged.methodName;
                Type[] argumentTypes = merged.argumentTypes;
                MethodType methodType = merged.methodType.HasValue ? merged.methodType.Value : MethodType.Normal;

                label = declaringType.Name + "." + (string.IsNullOrEmpty(methodName) ? methodType.ToString() : methodName)
                    + " <- " + type.FullName;

                switch (methodType)
                {
                    case MethodType.Normal:
                        if (string.IsNullOrEmpty(methodName))
                        {
                            return false;
                        }
                        original = AccessTools.Method(declaringType, methodName, argumentTypes);
                        return true;

                    case MethodType.Getter:
                        if (string.IsNullOrEmpty(methodName))
                        {
                            return false;
                        }
                        original = AccessTools.PropertyGetter(declaringType, methodName);
                        return true;

                    case MethodType.Setter:
                        if (string.IsNullOrEmpty(methodName))
                        {
                            return false;
                        }
                        original = AccessTools.PropertySetter(declaringType, methodName);
                        return true;

                    case MethodType.Constructor:
                        original = AccessTools.Constructor(declaringType, argumentTypes);
                        return true;

                    default:
                        // StaticConstructor / Enumerator 等形态不做静态校验
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>目标方法上是否存在由本 Mod owner 施加的补丁</summary>
        private static bool IsOwnedByHarmony(MethodBase original, string owner)
        {
            try
            {
                HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(original);
                if (patchInfo == null)
                {
                    return false;
                }

                return ContainsOwner(patchInfo.Prefixes, owner)
                    || ContainsOwner(patchInfo.Postfixes, owner)
                    || ContainsOwner(patchInfo.Transpilers, owner)
                    || ContainsOwner(patchInfo.Finalizers, owner);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsOwner(IList<Patch> patches, string owner)
        {
            if (patches == null)
            {
                return false;
            }

            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] != null && patches[i].owner == owner)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
