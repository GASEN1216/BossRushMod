// ============================================================================
// ConfigBackMountain.cs - 竞技场后山入口总开关的配置接线（M0 骨架）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算；语义是同一份 partial class。
//   - 后山总开关属于**默认内容，恒为开启**：不注册进 ModConfig UI、不从 ModConfig 读、
//     默认值为 true，并由 ForceContentSystemSwitchesOn 抹平老档残留的 false。
//     策略与理由见 Config/ConfigContentSystemSwitches.cs。
//   - `BossRush_BackMountainUnlockAll` 是**旋钮**不是内容开关，照常注册进 UI：
//     打开后三个设施不再等战役章节 token，直接全部可用。默认 false。
//     它给的是「我不想跟着剧情走、直接玩后山」这个选择，属于玩家偏好。
//
// 注意：UnlockAll 键必须进 IsHandledModConfigOptionKey 白名单，
// 否则单键热更新分支永不可达。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键：跳过战役解锁的旁路旋钮。</summary>
        private const string BackMountainUnlockAllModConfigKeySuffix = "_BackMountainUnlockAll";

        private partial class BossRushConfig
        {
            /// <summary>
            /// 竞技场后山入口开关。属于默认内容，恒为开启
            /// （见 Config/ConfigContentSystemSwitches.cs 的策略说明）。
            /// 字段与 dormant 契约保留：将来若要重新放出开关，只需恢复 Register 调用。
            /// </summary>
            public bool backMountainEnabled = true;

            /// <summary>
            /// 旁路开关：无视战役章节 token，三设施直接全解锁。
            /// 只有 backMountainEnabled 为真时才有意义。默认 false。
            /// </summary>
            public bool backMountainUnlockAll = false;
        }

        /// <summary>
        /// 批量加载路径：只读旁路旋钮。
        /// **总开关不从 ModConfig 读**——它是恒开的内容开关，读回来只会捡到老档残留的 false。
        /// </summary>
        private void LoadBackMountainConfigFromModConfig(MethodInfo boolLoadMethod)
        {
            try
            {
                if (boolLoadMethod == null || config == null) return;

                string unlockAllKey = ModName + BackMountainUnlockAllModConfigKeySuffix;
                object unlockAllResult = boolLoadMethod.Invoke(null, new object[] { unlockAllKey, config.backMountainUnlockAll });
                config.backMountainUnlockAll = (bool)unlockAllResult;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载后山旁路旋钮失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中旁路旋钮时重新读取并返回 true。</summary>
        private bool TryLoadBackMountainSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;

                string unlockAllKey = ModName + BackMountainUnlockAllModConfigKeySuffix;
                if (changedKey == unlockAllKey)
                {
                    MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                    object result = boolLoadMethod.Invoke(null, new object[] { unlockAllKey, config.backMountainUnlockAll });
                    config.backMountainUnlockAll = (bool)result;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载后山开关失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 向 ModConfig 注册后山旋钮。
        /// **总开关不注册**——它是恒开的内容开关（见 ConfigContentSystemSwitches.cs）。
        /// </summary>
        private void RegisterBackMountainModConfigOptions(MethodInfo addBoolMethod)
        {
            try
            {
                if (addBoolMethod == null || config == null) return;

                string unlockAllLabel = L10n.T("竞技场后山：跳过战役解锁（调试）", "Arena Backyard: Skip Campaign Unlocks (Debug)");
                string unlockAllKey = ModName + BackMountainUnlockAllModConfigKeySuffix;
                addBoolMethod.Invoke(null, new object[] { ModName, unlockAllKey, unlockAllLabel, config.backMountainUnlockAll });

                DevLog("[BossRush] 后山旁路旋钮注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册后山旁路旋钮失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 后山入口总开关的唯一只读入口：no-throw，缺配置返回 false。
        /// </summary>
        internal bool IsBackMountainConfiguredEnabled()
        {
            try
            {
                return config != null && config.backMountainEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 旁路开关只读入口：无视战役 token 全解锁。no-throw，缺配置返回 false。
        /// 调用方必须自行先判总开关——旁路开关不隐含开启后山。
        /// </summary>
        internal bool IsBackMountainUnlockAllConfigured()
        {
            try
            {
                return config != null && config.backMountainUnlockAll;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
