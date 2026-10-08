using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>噬魂挽歌的物品文本；启动、切换语言和按需加载共用同一入口。</summary>
    internal static class PhantomWitchScytheLocalization
    {
        private static readonly PhantomWitchScytheConfig Config = new PhantomWitchScytheConfig();

        internal static void Inject(Item item = null)
        {
            if (item != null && item.TypeID != PhantomWitchScytheIds.WeaponTypeId) return;
            try
            {
                string name = L10n.T(Config.DisplayNameCN, Config.DisplayNameEN);
                string description = L10n.T(Config.DescriptionCN, Config.DescriptionEN);
                InjectKey("Item_" + PhantomWitchScytheIds.WeaponTypeId, name, description);
                InjectKey("phantom_witch_scythe", name, description);
                InjectKey(PhantomWitchConfig.ScytheNameCN, name, description);
                InjectKey(PhantomWitchConfig.ScytheNameEN, name, description);
                InjectKey(PhantomWitchScytheIds.WeaponPrefabName, name, description);
                InjectKey(PhantomWitchScytheIds.WeaponPrefabName.ToLowerInvariant(), name, description);
                // 兼容玩家反馈中的旧拼写；不改已发布的 prefab 名称和物品身份。
                InjectKey("phantomscyth_melee_item", name, description);
                if (item != null) InjectKey(item.DisplayNameRaw, name, description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 本地化注入失败: " + e.Message);
            }
        }

        private static void InjectKey(string key, string name, string description)
        {
            if (string.IsNullOrEmpty(key)) return;
            LocalizationHelper.InjectLocalization(key, name);
            LocalizationHelper.InjectLocalization(key + "_Desc", description);
        }
    }
}
