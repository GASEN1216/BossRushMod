// ============================================================================
// ConfigModeG.cs - 宿命回响（Mode G）局内快捷键的配置接线
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class，形态照 ConfigDailyReport.cs / ConfigPetNest.cs：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调参数走 Config + ModConfig；
//     Mode G 的玩法数值全部在 ModeG/ 各自的配置类里，这里**只有**一个按键项。
//   - 镜像键 `BossRush_ModeGAbandonHotkey`，默认 K。
//     放弃挑战是不可逆操作（船票与信物不返还、契约连胜清零），因此走确认页，
//     按键本身只负责打开确认页，不直接终局。
//   - 0 或负值视为禁用该快捷键；轮询侧（ModeG/ModeGEntry.cs）据此 O(1) 早返。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键（与成就快捷键同形，复用同一份按键选项表）。</summary>
        private const string ModeGAbandonHotkeyModConfigKeySuffix = "_ModeGAbandonHotkey";

        private partial class BossRushConfig
        {
            /// <summary>
            /// 宿命回响「放弃挑战」确认页快捷键（默认 K 键，取值对应 KeyCode 枚举）。
            /// 仅在 Mode G 战斗进行中生效；0 或负值视为禁用。
            /// </summary>
            public int modeGAbandonHotkey = (int)UnityEngine.KeyCode.K;
        }

        /// <summary>批量加载路径：从 ModConfig 读放弃挑战快捷键。</summary>
        private void LoadModeGAbandonHotkeyFromModConfig(MethodInfo intLoadMethod)
        {
            try
            {
                if (intLoadMethod == null || config == null) return;
                string key = ModName + ModeGAbandonHotkeyModConfigKeySuffix;
                object result = intLoadMethod.Invoke(
                    null, new object[] { key, config.modeGAbandonHotkey });
                config.modeGAbandonHotkey = (int)result;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载宿命回响放弃快捷键失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中放弃快捷键时重新读取并返回 true。</summary>
        private bool TryLoadModeGAbandonHotkeySingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;
                string key = ModName + ModeGAbandonHotkeyModConfigKeySuffix;
                if (changedKey != key) return false;

                MethodInfo intLoadMethod = loadMethod.MakeGenericMethod(typeof(int));
                object result = intLoadMethod.Invoke(
                    null, new object[] { key, config.modeGAbandonHotkey });
                config.modeGAbandonHotkey = (int)result;
                return true;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载宿命回响放弃快捷键失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// ModConfig 注册路径：复用成就快捷键那份按键选项表，不再造第二份。
        /// </summary>
        private void RegisterModeGAbandonHotkeyDropdown(
            MethodInfo addDropdownMethod,
            System.Collections.Generic.SortedDictionary<string, object> hotkeyOptions)
        {
            try
            {
                if (addDropdownMethod == null || hotkeyOptions == null || config == null) return;
                string label = L10n.T("宿命回响放弃挑战快捷键", "Fate Echo Abandon Hotkey");
                string key = ModName + ModeGAbandonHotkeyModConfigKeySuffix;
                addDropdownMethod.Invoke(null, new object[]
                {
                    ModName, key, label, hotkeyOptions, typeof(int), config.modeGAbandonHotkey
                });
                DevLog("[BossRush] 宿命回响放弃挑战快捷键配置项注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册宿命回响放弃快捷键配置项失败: " + ex.Message);
            }
        }
    }
}
