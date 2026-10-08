namespace BossRush
{
    /// <summary>空仓地雷盒：把打空弹匣的时机变成一次撤退布雷机会。</summary>
    public static class EmptyMagazineMineConfig
    {
        public const string BaseName = "EmptyMagazineMine";
        public const string LogPrefix = "[EmptyMagazineMine]";
        public const string DisplayNameCN = "空仓地雷盒";
        public const string DisplayNameEN = "Empty-Mag Mine Box";
        public const string DescriptionCN = "把最后一发留给敌人，把空弹匣也留给敌人。\n<color=#FFB25B>【空仓礼物】</color>装备在图腾槽，同一轮装填中用同一把枪实际开火至少 6 次，打空弹匣时在脚下留下地雷；1.5 秒后爆炸，对 4 米内无遮挡的敌人造成 160 点物理伤害，不伤自己和友方。\n<color=#FFF1CE>【节奏】</color>冷却 6 秒。提前换弹、切枪或卸下图腾会清空开火计数；卸下图腾、死亡或切图会取消未爆地雷。基地不触发。\n<color=#BBBBBB>来源：官方 Boss 炸弹狂人 20% 额外掉落，原版地图同样可得。</color>";
        public const string DescriptionEN = "Save the last round for your enemy. Leave them the empty magazine, too.\n<color=#FFB25B>[Parting Gift]</color> Equip in a totem slot. Fire the same gun at least 6 times in one reload cycle, then empty its magazine to leave a mine at your feet. It explodes after 1.5s, dealing 160 physical damage to enemies within 4m and line of sight. You and your allies are safe.\n<color=#FFF1CE>[Timing]</color> 6s cooldown. Reloading early, switching guns or unequipping resets the shot count. Unequipping, death or changing scenes cancels an armed mine. Inactive in the base.\n<color=#BBBBBB>Source: 20% extra drop from the official boss Mad Bomber, including on vanilla maps.</color>";

        // 一轮至少六次真实开火，限制低容量枪反复换弹；弹丸数不算开火次数。
        public const int MinimumShots = 6;
        public const float CooldownSeconds = 6f;
        public const float FuseSeconds = 1.5f;
        public const float BlastRadius = 4f;
        public const float BlastDamage = 160f;
        public const int ItemQuality = 5;
        public const float ItemWeight = 0.5f;
    }
}
