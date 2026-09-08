using System;
using System.Collections.Generic;
using HarmonyLib;

namespace BossRush
{
    // AddComponent 创建的交互体没有 prefab 序列化的空列表。
    // 只补缺失容器，保留原有交互组和完整官方 Awake（含碰撞体、位置与标记初始化）。
    [HarmonyPatch(typeof(InteractableBase), "Awake", new Type[] { })]
    internal static class InteractableAwakeGroupInitializationPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ref List<InteractableBase> ___otherInterablesInGroup)
        {
            if (___otherInterablesInGroup == null)
            {
                ___otherInterablesInGroup = new List<InteractableBase>();
            }
        }
    }
}
