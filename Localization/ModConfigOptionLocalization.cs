// ============================================================================
// ModConfigOptionLocalization.cs - ModConfig 下拉项标题的本地化键（Options_<配置键>）
// ============================================================================
// 为什么要注入（2026-11-24 owner 实测「配置里都没有本地化键」）：
//   ModConfig 的下拉项（含 AddBoolDropdownList 的布尔开关）实例化的是官方 OptionsUIEntry_Dropdown。
//   ModConfig 只在创建时把说明文字 SetText 一次；官方组件在 Awake、OnValidate 和每次
//   LocalizationManager.OnSetLanguage 时，都把标题重设为 ("Options_" + provider.Key).ToPlainText()。
//   这个键没人注入时官方返回 "*Options_BossRush_xxx*"，于是一刷新语言，整排标题都成了带星号的键名。
//   滑块走 ModConfig 自己的 OptionsUIEntry_Slider_Mod，它的 RefreshLable 是空操作，标题一直是说明文字，不需要键。
//
// 单一来源：注册点照旧写中英文，只是改为经 Label(配置键, 中文, 英文) 取文字。
//   Label 返回当前语言的文字给 ModConfig，同时把 Options_<配置键> 注入官方表并登记下来。
//   Inject() 挂在 InjectLocalization_Extra_Integration()，宿主在切语言时会重调整条注入链，
//   于是登记过的标题按新语言重注入（AGENTS 4.4：语言在取用时解析）。
//   宿主的切语言回调订阅得比任何下拉项都早，先注入、后由官方组件读，读到的就是新语言。
// 清理：登记表随 IntegrationRuntimeModule.OnDestroy 清空（ResetStaticCaches），下次装载重新登记。
// 由 tests/ModConfigOptionLabelLocalizationGuard.py 守卫：每个下拉项注册点的标题都必须经 Label。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    internal static class ModConfigOptionLocalization
    {
        /// <summary>官方 OptionsUIEntry_Dropdown.LabelKey 的前缀（"Options_" + provider.Key）。</summary>
        internal const string OfficialLabelKeyPrefix = "Options_";

        /// <summary>配置键 → { 中文, 英文 }。只在注册点写入，切语言时整表重注入。</summary>
        private static readonly Dictionary<string, string[]> registeredLabels = new Dictionary<string, string[]>();

        /// <summary>
        /// 下拉项注册点取标题：返回当前语言的文字（传给 ModConfig 当说明文字），
        /// 并把官方查表用的 Options_&lt;配置键&gt; 注入成同一句话。
        /// </summary>
        internal static string Label(string optionKey, string zh, string en)
        {
            string text = L10n.T(zh, en);
            if (string.IsNullOrEmpty(optionKey)) return text;
            registeredLabels[optionKey] = new[] { zh, en };
            LocalizationHelper.InjectLocalization(OfficialLabelKeyPrefix + optionKey, text);
            return text;
        }

        /// <summary>按当前语言重注入全部登记过的标题；挂在本地化注入链上，启动与切语言时都会调。</summary>
        internal static void Inject()
        {
            foreach (KeyValuePair<string, string[]> pair in registeredLabels)
            {
                LocalizationHelper.InjectLocalization(OfficialLabelKeyPrefix + pair.Key, L10n.T(pair.Value[0], pair.Value[1]));
            }
        }

        /// <summary>宿主销毁时清空登记表（官方覆盖表里的文字留着无害，下次装载会重新登记并覆盖）。</summary>
        internal static void ResetStaticCaches()
        {
            registeredLabels.Clear();
        }

#if BOSSRUSH_DEV
        /// <summary>F3 核对用（MODCONFIG_OPTION_LABELS）：登记过的配置键与中英文的快照。</summary>
        internal static List<KeyValuePair<string, string[]>> SnapshotForValidation()
        {
            return new List<KeyValuePair<string, string[]>>(registeredLabels);
        }
#endif
    }
}
