// ============================================================================
// BossCleanupHelpers.cs - Boss 清理通用工具
// ============================================================================
// 模块说明：
//   集中三个自定义 Boss（龙裔遗族 / 龙皇 / 幽灵女巫）共用的清理步骤。
//   当前实现：runtime preset 副本销毁。
//   未来新增 Boss 时，在各自模块调用这里的静态方法即可，避免重复拼写。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Boss 清理通用工具集。
    /// </summary>
    internal static class BossCleanupHelpers
    {
        /// <summary>
        /// 销毁 Boss 的运行时预设副本（<see cref="CharacterRandomPreset"/> 的 Instantiate 拷贝）。
        /// 只有当副本的 <c>nameKey</c> 或 <c>name</c> 与 Boss 的约定命名匹配时才销毁，
        /// 避免误伤场景里原版的共享预设。销毁后把 <c>character.characterPreset</c> 置空。
        /// 任何异常都会被吞掉并转成一条 DevLog warning。
        /// </summary>
        /// <param name="character">Boss 角色（允许为 null，直接早退）</param>
        /// <param name="nameKey">Boss 约定 nameKey（例如 <c>DragonKingConfig.BossNameKey</c>）</param>
        /// <param name="presetName">运行时副本的基名（例如 <c>"DragonKing_Preset"</c>）</param>
        /// <param name="logTag">DevLog 前缀（例如 <c>"[DragonKing]"</c>），仅在异常分支用来定位 Boss</param>
        public static void DestroyRuntimePreset(
            CharacterMainControl character,
            string nameKey,
            string presetName,
            string logTag)
        {
            if (character == null)
            {
                return;
            }

            try
            {
                CharacterRandomPreset runtimePreset = character.characterPreset;
                if (runtimePreset == null)
                {
                    return;
                }

                bool nameKeyMatches = !string.IsNullOrEmpty(nameKey) && runtimePreset.nameKey == nameKey;
                bool nameMatches = !string.IsNullOrEmpty(presetName) &&
                    runtimePreset.name != null &&
                    (runtimePreset.name == presetName ||
                     runtimePreset.name.StartsWith(presetName, StringComparison.Ordinal));

                if (nameKeyMatches || nameMatches)
                {
                    UnityEngine.Object.Destroy(runtimePreset);
                    character.characterPreset = null;
                }
            }
            catch (Exception e)
            {
                string prefix = string.IsNullOrEmpty(logTag) ? "[BossRush]" : logTag;
                ModBehaviour.DevLog(prefix + " [WARNING] 销毁运行时预设副本失败: " + e.Message);
            }
        }
    }
}
