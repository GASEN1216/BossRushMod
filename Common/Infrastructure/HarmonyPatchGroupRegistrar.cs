using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Harmony Patch 分组注册入口
    /// 职责：维护分组列表、输出分组日志
    /// 本轮不改变 Patch apply 方式，仍委托 harmony.PatchAll(assembly)
    /// </summary>
    internal static class HarmonyPatchGroupRegistrar
    {
        private static readonly List<IHarmonyPatchGroup> _groups = new List<IHarmonyPatchGroup>();

        /// <summary>注册一个 Patch 分组（null 安全）</summary>
        public static void Register(IHarmonyPatchGroup group)
        {
            if (group == null)
            {
                return;
            }

            string groupName = group.GroupName;
            for (int i = 0; i < _groups.Count; i++)
            {
                IHarmonyPatchGroup existing = _groups[i];
                if (existing == null)
                {
                    continue;
                }

                if (existing.GetType() == group.GetType()
                    || (!string.IsNullOrEmpty(groupName) && existing.GroupName == groupName))
                {
                    return;
                }
            }

            _groups.Add(group);
        }

        /// <summary>清空已注册分组元数据，用于模组重载/销毁兜底</summary>
        public static void Clear()
        {
            _groups.Clear();
        }

        /// <summary>
        /// 输出所有已注册分组的日志
        /// 日志格式: [BossRush][HarmonyGroup] {GroupName} (enabled={IsEnabled})
        /// </summary>
        public static void LogRegisteredGroups()
        {
            foreach (var g in _groups)
            {
                ModBehaviour.DevLog(
                    "[BossRush][HarmonyGroup] " + g.GroupName + " (enabled=" + g.IsEnabled + ")");
            }
        }

        /// <summary>获取所有已注册的分组（只读）</summary>
        public static IReadOnlyList<IHarmonyPatchGroup> All => _groups;
    }
}
