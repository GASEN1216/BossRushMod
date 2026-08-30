// ============================================================================
// ConfigAffixForge.cs - 词缀锻造入口总开关的配置接线
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调开关走 Config + ModConfig；
//     词缀锻造**只有这一个**配置项，其余数值全部在
//     Integration/AffixForge/AffixDefinitions.cs 的常量区。
//   - 镜像键 `BossRush_AffixForgeEnabled`，**默认 true**：词缀锻造是纯自愿的
//     基地内消耗型玩法，不改刷怪、不改掉落基线，玩家不主动去找哥布林就不会遇到它，
//     没有默认关的理由。
//   - 唯一只读入口 IsAffixForgeConfiguredEnabled()：no-throw，**缺配置返回默认值**
//     （即 true）。运行时服务与战斗归因谓词都会调它，因此这里只能是一次判空
//     加一次字段读，不做任何分配、不写日志。
//   - 关闭时整个子系统 dormant：ShutdownRuntime 退全部订阅、撤全部常驻 modifier、
//     隐藏哥布林子交互；**已经写在装备上的 AFX_ KV 一律保留不清**，
//     重新打开即刻恢复（开关热切语义，与遗种巢/日报一致）。
//   - 禁止反射私有字段，也禁止把开关缓存进 AffixDefinitions 或存档。
//
// 本文件同时登记词缀锻造占用的 TypeID（与 Config/ConfigItemIds.cs 是同一个 partial class）。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键后缀（完整键 = ModName + 它 = BossRush_AffixForgeEnabled）。</summary>
        private const string AffixForgeModConfigKeySuffix = "_AffixForgeEnabled";

        /// <summary>词缀锻造开关的默认值。字段初始值与缺配置回落值共用它，避免两处漂移。</summary>
        private const bool AffixForgeDefaultEnabled = true;

        private partial class BossRushConfig
        {
            /// <summary>
            /// 词缀锻造唯一可写入口开关，默认开启。
            /// 关闭时整个子系统 dormant：不订阅战斗事件、不挂常驻 modifier、
            /// 不出哥布林子交互；装备上已有的 AFX_ KV 保留不动。
            /// </summary>
            public bool affixForgeEnabled = AffixForgeDefaultEnabled;
        }

        /// <summary>批量加载路径：从 ModConfig 读词缀锻造开关。</summary>
        private void LoadAffixForgeEnabledFromModConfig(MethodInfo boolLoadMethod)
        {
            try
            {
                if (boolLoadMethod == null || config == null) return;
                string key = ModName + AffixForgeModConfigKeySuffix;
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.affixForgeEnabled });
                config.affixForgeEnabled = (bool)result;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载词缀锻造开关失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中词缀锻造键时重新读取并返回 true。</summary>
        private bool TryLoadAffixForgeSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;
                string key = ModName + AffixForgeModConfigKeySuffix;
                if (changedKey != key) return false;

                MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.affixForgeEnabled });
                config.affixForgeEnabled = (bool)result;
                return true;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载词缀锻造开关失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>向 ModConfig 注册词缀锻造开关选项。</summary>
        private void RegisterAffixForgeModConfigOption(MethodInfo addBoolMethod)
        {
            try
            {
                if (addBoolMethod == null || config == null) return;
                string label = L10n.T("词缀锻造：给武器护甲附加词缀", "Affix Forging: add affixes to gear");
                string key = ModName + AffixForgeModConfigKeySuffix;
                addBoolMethod.Invoke(null, new object[] { ModName, key, label, config.affixForgeEnabled });
                DevLog("[BossRush] 词缀锻造配置项注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册词缀锻造配置项失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 词缀锻造入口总开关的唯一只读入口：no-throw，缺配置返回默认值
        /// （AffixForgeDefaultEnabled）。战斗归因谓词会高频调它，
        /// 因此这里只能是一次判空加一次字段读，不做任何分配、不写日志。
        /// </summary>
        internal bool IsAffixForgeConfiguredEnabled()
        {
            try
            {
                return config != null ? config.affixForgeEnabled : AffixForgeDefaultEnabled;
            }
            catch (Exception)
            {
                return AffixForgeDefaultEnabled;
            }
        }
    }

    /// <summary>
    /// 词缀锻造占用的 TypeID。与 Config/ConfigItemIds.cs 是同一个 partial class。
    /// 台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3：严格递增、不复用、不回填。
    /// </summary>
    public static partial class BossRushItemIds
    {
        /// <summary>
        /// 词缀熔石（材料）。词缀身份写在装备自己的 AFX_ KV 上，
        /// 本 TypeID 只是锻造要花的消耗品。
        /// </summary>
        public const int AffixForgeStone = 500060;
    }
}
