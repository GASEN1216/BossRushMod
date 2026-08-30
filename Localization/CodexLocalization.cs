// ============================================================================
// CodexLocalization.cs - 鸭皇图鉴本地化的唯一 source of truth
// ============================================================================
// 形态照 Localization/DailyReportLocalization.cs：独立文件而不是继续膨胀
// LocalizationInjector.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 范围说明（重要）：
//   图鉴面板的文案**全部是 UI 侧内联** L10n.T(cn, en) 双语字面量——它们只被自己
//   的界面读，塞进 LocalizationManager 只会污染全局字符串表。
//   这里只注入**官方系统会主动去查表**的 key：
//     1) 物品显示名与描述（item.DisplayNameRaw = "BossRush_CodexBook"，
//        AGENTS.md 4.4：设了 DisplayNameRaw 就必须注入，否则背包里显示
//        *BossRush_CodexBook*）；
//     2) 若干可能被 Wiki / 交互提示引用的 BossRush_Codex_ 前缀键。
//
// 单点纪律：CodexBookConfig.InjectLocalization() 只是转发到本文件的
// InjectBookKeys()，物品名与面板标题永远来自同一处，不会两边漂移。
// 前缀统一 CodexTuning.LocalizationPrefix = "BossRush_Codex_"；
// 物品键例外，它是 "BossRush_CodexBook"（不带下划线分段），已随存档/物品表冻结。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>图鉴本地化键的唯一注入入口。</summary>
    public static class CodexLocalization
    {
        /// <summary>把全部图鉴键注入官方本地化表。重复调用无害（SetOverrideText 覆盖写）。</summary>
        public static void Inject()
        {
            try
            {
                Dictionary<string, string> map = new Dictionary<string, string>();
                AddCore(map);
                LocalizationHelper.InjectLocalizations(map);

                InjectBookKeys();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        /// <summary>
        /// 图鉴物品（TypeID 500061）的全部本地化键。
        /// 官方在不同路径上会用 DisplayNameRaw / Item_{id} / {id} 三种形式查表，
        /// 因此三种都注入，缺任何一种都可能在某个界面上露出星号包裹的原始 key。
        /// </summary>
        public static void InjectBookKeys()
        {
            try
            {
                string displayName = L10n.T(
                    CodexBookConfig.DISPLAY_NAME_CN, CodexBookConfig.DISPLAY_NAME_EN);
                string description = L10n.T(
                    CodexBookConfig.DESCRIPTION_CN, CodexBookConfig.DESCRIPTION_EN);
                string typeId = CodexBookConfig.TYPE_ID.ToString();

                Dictionary<string, string> map = new Dictionary<string, string>();

                map[CodexBookConfig.LOC_KEY_DISPLAY] = displayName;
                map[CodexBookConfig.LOC_KEY_DISPLAY + "_Desc"] = description;

                map["Item_" + typeId] = displayName;
                map["Item_" + typeId + "_Desc"] = description;

                map[typeId] = displayName;
                map[typeId + "_Desc"] = description;

                // 中英字面量别名：克隆兜底路径上 item.name 会是英文名，
                // 某些界面直接拿它当 key 查表
                map[CodexBookConfig.DISPLAY_NAME_CN] = displayName;
                map[CodexBookConfig.DISPLAY_NAME_EN] = displayName;

                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴物品本地化注入失败: " + e.Message);
            }
        }

        #region 私有

        /// <summary>
        /// BossRush_Codex_ 前缀键。只放"可能被本 Mod 之外的系统查表"的那几条；
        /// 面板正文一律 UI 侧内联双语，不进这张表。
        /// </summary>
        private static void AddCore(Dictionary<string, string> map)
        {
            Add(map, "Title", "鸭皇图鉴", "Duckov Codex");
            Add(map, "Interact", "查阅鸭皇图鉴", "Consult the Duckov Codex");
            Add(map, "Locked", "尚未记录", "Not recorded yet");
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[CodexTuning.LocalizationPrefix + suffix] = L10n.T(cn, en);
        }

        #endregion
    }
}
