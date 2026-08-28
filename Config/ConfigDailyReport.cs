// ============================================================================
// ConfigDailyReport.cs - 鸭科夫日报入口总开关的配置接线（P0 步骤 4）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调开关走 Config + ModConfig；
//     日报**只有这一个**配置项，其余数值全部在 Integration/DailyReport/DailyReportTuning.cs。
//   - 镜像键 `BossRush_DailyReportEnabled`，默认 true
//     （报箱要玩家花 500 金自建，已是天然门槛，不必再用开关拦一道）。
//   - 唯一只读入口 IsDailyReportConfiguredEnabled()：no-throw，缺配置返回 false；
//     禁止反射私有字段，也禁止把开关缓存进 DailyReportTuning 或存档。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键（与遗种巢 / Mode H 的开关同形）。</summary>
        private const string DailyReportModConfigKeySuffix = "_DailyReportEnabled";

        private partial class BossRushConfig
        {
            /// <summary>
            /// 鸭科夫日报唯一可写入口开关，默认开启。
            /// 关闭时整个子系统 dormant：不订阅存档、不计时、不出刊、不提示。
            /// </summary>
            public bool dailyReportEnabled = true;
        }

        /// <summary>批量加载路径：从 ModConfig 读日报开关。</summary>
        private void LoadDailyReportEnabledFromModConfig(MethodInfo boolLoadMethod)
        {
            try
            {
                if (boolLoadMethod == null || config == null) return;
                string key = ModName + DailyReportModConfigKeySuffix;
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.dailyReportEnabled });
                config.dailyReportEnabled = (bool)result;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载日报开关失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中日报键时重新读取并返回 true。</summary>
        private bool TryLoadDailyReportSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;
                string key = ModName + DailyReportModConfigKeySuffix;
                if (changedKey != key) return false;

                MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.dailyReportEnabled });
                config.dailyReportEnabled = (bool)result;
                return true;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载日报开关失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>向 ModConfig 注册日报开关选项。</summary>
        private void RegisterDailyReportModConfigOption(MethodInfo addBoolMethod)
        {
            try
            {
                if (addBoolMethod == null || config == null) return;
                string label = L10n.T("鸭科夫日报：每日战绩、签到与悬赏", "Duckov Daily: Recap, Check-in and Bounties");
                string key = ModName + DailyReportModConfigKeySuffix;
                addBoolMethod.Invoke(null, new object[] { ModName, key, label, config.dailyReportEnabled });
                DevLog("[BossRush] 日报配置项注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册日报配置项失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 日报入口总开关的唯一只读入口：no-throw，缺配置返回 false。
        /// 禁止反射私有字段，也禁止把开关缓存进 DailyReportTuning 或存档。
        /// </summary>
        internal bool IsDailyReportConfiguredEnabled()
        {
            try
            {
                return config != null && config.dailyReportEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
