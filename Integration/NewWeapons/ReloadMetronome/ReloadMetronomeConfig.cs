namespace BossRush
{
    internal static class ReloadMetronomeConfig
    {
        internal const string VendorNpcId = "sky_bellkeeper";
        internal const int ShopUnlockLevel = 5;
        internal const int ShopStock = 1;
        internal const int ShellCost = 20;
        internal const string BaseName = "ReloadMetronome";
        internal const string NameCN = "钟守节拍器";
        internal const string NameEN = "Bell Keeper's Metronome";
        internal const string DescriptionCN = "铜牌上刻着：装填有时，开火有节。\n<color=#FFB25B>【合拍装填】</color>装备在图腾槽，整匣换弹时准星下方出现节拍条。白色指针进入金色窗口时，再按一次换弹键，立即进入装填结算，正常消耗背包弹药。\n<color=#FFF1CE>【一拍定音】</color>每轮只有一次机会；按早、按晚或不按都按原速度完成，不额外惩罚。逐发装填及短于 0.75 秒的换弹不触发。切枪、卸下图腾、死亡或切图取消本轮机会。\n<color=#BBBBBB>来源：原版 Boss 急速团长 20% 额外掉落；或无声钟守好感 5 级的旧物匣，20 枚随身贝壳兑换。婚后基地同样可买，基地也能练习。</color>";
        internal const string DescriptionEN = "The brass plate reads: A time to reload, a beat to fire.\n<color=#FFB25B>[On Beat]</color> Equip in a totem slot. A timing bar appears below the crosshair during magazine reloads. Press your reload button again while the white marker is inside the gold window to finish early, using ammo from your inventory as normal.\n<color=#FFF1CE>[One Chance]</color> One attempt per reload. Early, late or skipped attempts complete at normal speed without extra penalties. Single-round reloads and reloads shorter than 0.75s are excluded. Switching weapons, unequipping, death or scene changes cancel the attempt.\n<color=#BBBBBB>Source: 20% extra drop from the vanilla boss Speedy; or the Silent Bell Keeper's shop at affinity level 5, for 20 seashells from your backpack. Available at home after marriage; works in the base for practice.</color>";

        // 固定在约六成处，窗口按秒限宽；长装填不能随时按，短装填也保留反应余地。
        internal const float MinimumReloadSeconds = 0.75f;
        internal const float WindowCenter = 0.62f;
        internal const float WindowFraction = 0.15f;
        internal const float MinimumWindowSeconds = 0.16f;
        internal const float MaximumWindowSeconds = 0.30f;
    }
}
