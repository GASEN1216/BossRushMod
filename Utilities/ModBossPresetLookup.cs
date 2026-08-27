// ============================================================================
// ModBossPresetLookup.cs - 自定义 Boss preset 查找/判定的共享实现
// ============================================================================
// 背景：
//   龙皇、幽灵女巫、龙裔遗族各自在自己的 Boss 文件里写了一份
//   FindXxxPresetInfo() / IsXxxPreset() —— 三份 IsXxxPreset 的方法体逐字相同，
//   只差各自 Config 的三个常量（BossNameKey / BossNameCN / BossNameEN）；
//   两份 FindXxxPresetInfo 同理，只差一个 name key。
//
//   每加一个自定义 Boss 就再抄一遍这两段，是典型的"应该做成查表却做成了复制"。
//   这里收成一处，各 Boss 只保留一个转发方法（保持原有方法名与可见性，
//   调用点零改动）。
//
// 行为与此前逐字一致：
//   - Find：按 preset.name 精确匹配，遍历顺序不变，找不到返回 null，
//           presets 为 null 时返回 null。
//   - Matches：preset 为 null 返回 false；否则 name 命中，或 displayName
//           命中中文名 / 英文名之一即为真（三个条件的短路顺序也不变）。
//
// 分层：按 docs/架构说明/Hooks分层约定.md，跨模块复用的基础设施放 Utilities/。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    internal static class ModBossPresetLookup
    {
        /// <summary>
        /// 从 preset 池里按 name key 精确取出某个自定义 Boss 的 preset。
        /// </summary>
        internal static EnemyPresetInfo FindByNameKey(List<EnemyPresetInfo> presets, string nameKey)
        {
            if (presets == null)
            {
                return null;
            }

            foreach (var preset in presets)
            {
                if (preset != null && preset.name == nameKey)
                {
                    return preset;
                }
            }

            return null;
        }

        /// <summary>
        /// 判定 preset 是否是指定的自定义 Boss。
        /// name key 命中，或显示名命中中/英文名之一，都算命中——
        /// 官方 preset 的 name 与 displayName 不总是同步，两边都要认。
        /// </summary>
        internal static bool Matches(EnemyPresetInfo preset, string nameKey, string nameCn, string nameEn)
        {
            if (preset == null)
            {
                return false;
            }

            return preset.name == nameKey ||
                   preset.displayName == nameCn ||
                   preset.displayName == nameEn;
        }
    }
}
