// ============================================================================
// ConfigCodex.cs - 鸭皇图鉴入口总开关的配置接线
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调开关走 Config + ModConfig；
//     图鉴**只有这一个**配置项，其余数值全部在 Integration/Codex/CodexTuning.cs。
//   - 镜像键 `BossRush_CodexEnabled`，**默认 true**：图鉴是纯记录型元内容，
//     不改玩法、不发额外奖励（里程碑走既有成就系统），没有默认关的理由；
//     关掉之后整个子系统 dormant：不订阅存档、不采集击杀、不建目录、不出面板。
//   - 唯一只读入口 IsCodexConfiguredEnabled()：no-throw，**缺配置返回默认值**
//     （即 true）。这一点与日报刻意不同：日报缺配置返回 false 是因为它会主动
//     推送刊物与提示；图鉴只在玩家亲手打开时才有可见行为，早于配置初始化的
//     那几帧里按默认开启处理，最坏结果只是多记一条击杀。
//   - 禁止反射私有字段，也禁止把开关缓存进 CodexTuning 或存档。
//
// 本文件同时登记图鉴占用的 TypeID（与 Config/ConfigItemIds.cs 是同一个 partial class）。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键后缀（完整键 = ModName + 它 = BossRush_CodexEnabled）。</summary>
        private const string CodexModConfigKeySuffix = "_CodexEnabled";

        /// <summary>图鉴开关的默认值。字段初始值与缺配置回落值共用它，避免两处漂移。</summary>
        private const bool CodexDefaultEnabled = true;

        private partial class BossRushConfig
        {
            /// <summary>
            /// 鸭皇图鉴唯一可写入口开关，默认开启。
            /// 关闭时整个子系统 dormant：不订阅存档、不采集击杀、不建目录、不出面板。
            /// </summary>
            public bool codexEnabled = CodexDefaultEnabled;
        }

        /// <summary>批量加载路径：从 ModConfig 读图鉴开关。</summary>
        private void LoadCodexEnabledFromModConfig(MethodInfo boolLoadMethod)
        {
            try
            {
                if (boolLoadMethod == null || config == null) return;
                string key = ModName + CodexModConfigKeySuffix;
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.codexEnabled });
                config.codexEnabled = (bool)result;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载图鉴开关失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中图鉴键时重新读取并返回 true。</summary>
        private bool TryLoadCodexSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;
                string key = ModName + CodexModConfigKeySuffix;
                if (changedKey != key) return false;

                MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.codexEnabled });
                config.codexEnabled = (bool)result;
                return true;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载图鉴开关失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>向 ModConfig 注册图鉴开关选项。</summary>
        private void RegisterCodexModConfigOption(MethodInfo addBoolMethod)
        {
            try
            {
                if (addBoolMethod == null || config == null) return;
                string label = L10n.T("鸭皇图鉴：Boss 击杀收集图鉴", "Duckov Codex: boss kill collection");
                string key = ModName + CodexModConfigKeySuffix;
                addBoolMethod.Invoke(null, new object[] { ModName, key, label, config.codexEnabled });
                DevLog("[BossRush] 图鉴配置项注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册图鉴配置项失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 图鉴入口总开关的唯一只读入口：no-throw，缺配置返回默认值（CodexDefaultEnabled）。
        /// 采集器每次击杀都会调它，因此这里只能是一次判空 + 一次字段读，不做任何分配。
        /// </summary>
        internal bool IsCodexConfiguredEnabled()
        {
            try
            {
                return config != null ? config.codexEnabled : CodexDefaultEnabled;
            }
            catch (Exception)
            {
                return CodexDefaultEnabled;
            }
        }

        /// <summary>
        /// 无间炼狱是否激活（图鉴记录首杀模式用）。
        /// infiniteHellMode 是 ModBehaviour 另一处 partial（LootAndRewards/LootAndRewards.cs）
        /// 的私有字段，同一个类的 partial 之间可见，因此**不需要**改共享文件、
        /// 也不需要新增反射绑定。
        /// </summary>
        internal bool IsCodexInfiniteHellActive()
        {
            try
            {
                return infiniteHellMode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 图鉴占用的 TypeID。与 Config/ConfigItemIds.cs 是同一个 partial class。
    /// 台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3：严格递增、不复用、不回填。
    /// </summary>
    public static partial class BossRushItemIds
    {
        /// <summary>鸭皇图鉴（可用书）。</summary>
        public const int CodexBook = 500061;
    }
}
