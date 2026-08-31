// ============================================================================
// ConfigRandomEvents.cs - 局内随机事件「鸭生无常」的配置接线（方案二 步骤 6）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调开关走 Config + ModConfig；
//     随机事件**只有这两个**配置项，其余数值全部在 RandomEvents/RandomEventsTuning.cs。
//   - 镜像键 `BossRush_RandomEventsEnabled`（bool，默认 true，随本批交付即可玩）
//     与 `BossRush_RandomEventsFrequency`（int 档位 1~3，默认 2，只影响单局事件上限）。
//   - 唯一只读入口 IsRandomEventsConfiguredEnabled() / GetRandomEventsFrequencyTier()：
//     均 no-throw，缺配置分别返回 false / 默认档；
//     禁止反射私有字段，也禁止把开关缓存进 RandomEventsTuning 或存档。
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        #region 键与默认值

        /// <summary>随机事件总开关的 ModConfig 镜像键后缀。
        /// 已退役：总开关不再注册进 UI，本常量只作键名台账，防止将来被同名复用。</summary>
        private const string RandomEventsEnabledModConfigKeySuffix = "_RandomEventsEnabled";

        /// <summary>频率档 ModConfig 镜像键后缀。</summary>
        private const string RandomEventsFrequencyModConfigKeySuffix = "_RandomEventsFrequency";

        /// <summary>频率档合法下限（低频：单局 2 次）。</summary>
        private const int RandomEventsFrequencyTierMin = 1;

        /// <summary>频率档合法上限（高频：单局 5 次）。</summary>
        private const int RandomEventsFrequencyTierMax = 3;

        /// <summary>频率档默认值（中频：单局 3 次）。</summary>
        private const int RandomEventsFrequencyTierDefault = 2;

        private partial class BossRushConfig
        {
            /// <summary>
            /// 随机事件唯一可写入口开关，默认开启。
            /// 关闭时整个子系统 dormant：不建调度器、不计时、不触发、不建 HUD。
            /// </summary>
            public bool randomEventsEnabled = true;

            /// <summary>
            /// 频率档 1~3（1 低 / 2 中 / 3 高），默认 2。
            /// **只影响单局事件上限**（2/3/5），不改冷却区间，也不改并发上限（恒 1）。
            /// </summary>
            public int randomEventsFrequency = 2;
        }

        #endregion

        #region ModConfig 接线

        /// <summary>批量加载路径：从 ModConfig 读随机事件频率档。
        /// 总开关不读——它恒为开启，不暴露给玩家。</summary>
        private void LoadRandomEventsConfigFromModConfig(MethodInfo intLoadMethod)
        {
            try
            {
                if (config == null) return;

                if (intLoadMethod != null)
                {
                    string freqKey = ModName + RandomEventsFrequencyModConfigKeySuffix;
                    object freqResult = intLoadMethod.Invoke(
                        null, new object[] { freqKey, config.randomEventsFrequency });
                    config.randomEventsFrequency = ClampRandomEventsFrequencyTier((int)freqResult);
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载随机事件配置失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中随机事件频率键时重新读取并返回 true。
        /// 总开关不在此列——它不注册进 UI，永远收不到变更事件。</summary>
        private bool TryLoadRandomEventsSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;

                string freqKey = ModName + RandomEventsFrequencyModConfigKeySuffix;
                if (changedKey == freqKey)
                {
                    MethodInfo intLoadMethod = loadMethod.MakeGenericMethod(typeof(int));
                    object freqResult = intLoadMethod.Invoke(
                        null, new object[] { freqKey, config.randomEventsFrequency });
                    config.randomEventsFrequency = ClampRandomEventsFrequencyTier((int)freqResult);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载随机事件配置失败: " + changedKey + ", " + ex.Message);
                return false;
            }
        }

        /// <summary>向 ModConfig 注册随机事件的频率档选项。
        /// 总开关不注册——随机事件属于默认内容，恒为开启。</summary>
        private void RegisterRandomEventsModConfigOptions(MethodInfo addSliderMethod)
        {
            try
            {
                if (config == null) return;

                if (addSliderMethod != null)
                {
                    int freqValue = ClampRandomEventsFrequencyTier(config.randomEventsFrequency);
                    config.randomEventsFrequency = freqValue;

                    string freqLabel = L10n.T(
                        "随机事件频率（1 低 / 2 中 / 3 高）",
                        "Random event frequency (1 low / 2 mid / 3 high)");
                    string freqKey = ModName + RandomEventsFrequencyModConfigKeySuffix;
                    addSliderMethod.Invoke(null, new object[] {
                        ModName, freqKey, freqLabel, typeof(int), freqValue,
                        new Vector2((float)RandomEventsFrequencyTierMin, (float)RandomEventsFrequencyTierMax) });
                    DevLog("[BossRush] 随机事件频率配置项注册成功");
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册随机事件配置项失败: " + ex.Message);
            }
        }

        #endregion

        #region 只读入口

        /// <summary>
        /// 随机事件入口总开关的唯一只读入口：no-throw，缺配置返回 false。
        /// 禁止反射私有字段，也禁止把开关缓存进 RandomEventsTuning 或存档。
        /// </summary>
        internal bool IsRandomEventsConfiguredEnabled()
        {
            try
            {
                return config != null && config.randomEventsEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 频率档的唯一只读入口：no-throw，clamp 到 1~3，缺配置或异常返回默认档 2。
        /// </summary>
        internal int GetRandomEventsFrequencyTier()
        {
            try
            {
                if (config == null) return RandomEventsFrequencyTierDefault;
                return ClampRandomEventsFrequencyTier(config.randomEventsFrequency);
            }
            catch (Exception)
            {
                return RandomEventsFrequencyTierDefault;
            }
        }

        /// <summary>把任意输入夹到合法频率档区间。</summary>
        private static int ClampRandomEventsFrequencyTier(int tier)
        {
            if (tier < RandomEventsFrequencyTierMin) return RandomEventsFrequencyTierMin;
            if (tier > RandomEventsFrequencyTierMax) return RandomEventsFrequencyTierMax;
            return tier;
        }

        #endregion
    }
}
