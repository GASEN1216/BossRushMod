// ============================================================================
// RandomEventsLocalization.cs - 局内随机事件「鸭生无常」本地化的唯一注入口
// ============================================================================
// 形态照 Localization/PetNestLocalization.cs：独立文件而不是继续膨胀
// LocalizationInjector.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 为什么本模块此前没有这个文件：随机事件的绝大多数文案走内联
// `L10n.T("中文", "English")`，不经本地化表，所以一直不需要注入。
// 唯一的例外是随机事件商人的交互名——它写进官方
// `InteractableBase._overrideInteractNameKey`，那是**按 key 查表**的字段，
// 内联文案在这里用不上。该 key 从未被注入，玩家看到的是带星号的原始 key
// `*BossRush_RandomEvent_MerchantShop*`（AGENTS.md 4.4）。
//
// 前缀统一 RandomEventsTuning.LocalizationPrefix = "BossRush_RandomEvent_"。
// 今后随机事件若再新增走查表的键（交互名、物品 DisplayNameRaw 等），都加在这里。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>随机事件全部 `BossRush_RandomEvent_` 键的注入入口。</summary>
    public static class RandomEventsLocalization
    {
        /// <summary>把全部随机事件键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            // 随机事件商人的交互提示。写入点：
            // RandomEvents/RandomEventEffectsBridge_Spawn.cs 的
            // RandomEventMerchantShopInteractable.Setup（经 _overrideInteractNameKey）。
            Add(map, "MerchantShop", "神秘商人", "Mysterious Trader");

            LocalizationHelper.InjectLocalizations(map);
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[RandomEventsTuning.LocalizationPrefix + suffix] = L10n.T(cn, en);
        }
    }
}
