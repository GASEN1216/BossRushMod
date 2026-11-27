namespace BossRush
{
    /// <summary>
    /// 止语钟罩（500220，玩家许愿第 10 项「自充能护盾」）的数值、来路与双语说明。
    /// 回充式护罩：先于血量吃敌人的伤害，挨打后停一会儿再回；无声钟守好感 5 级、30 枚随身贝壳兑换。
    /// </summary>
    public static class SilenceBellConfig
    {
        public const string BaseName = "SilenceBell";
        public const string LogPrefix = "[SilenceBell]";
        public const string DisplayNameCN = "止语钟罩";
        public const string DisplayNameEN = "Hushbell Ward";

        /// <summary>护罩上限。约等于普通敌人四五发步枪弹，扛一轮突然开火，不扛正面硬拼。</summary>
        public const float Capacity = 80f;
        /// <summary>最后一次挨打后多久开始回充。</summary>
        public const float RechargeDelaySeconds = 5f;
        /// <summary>被打碎后至少多久才开始回充（期间再挨打仍按上面的 5 秒顺延）。</summary>
        public const float BrokenDelaySeconds = 9f;
        public const float RechargePerSecond = 20f;
        /// <summary>单帧最多回充多少秒：卡顿后的长帧不一次补满。</summary>
        public const float MaxTickSeconds = 0.25f;

        public const int ItemQuality = 5;
        public const int ItemValue = 18000;
        public const float ItemWeight = 0.4f;

        internal const string VendorNpcId = ReloadMetronomeConfig.VendorNpcId;
        internal const int ShellCost = 30;

        // 头顶护罩条：官方血条锚在头盔挂点上方 0.5 米，这条再高 0.35 米，压在血条上沿。
        internal const float HudHeadClearance = 0.85f;
        /// <summary>回满后护罩条停留多久再淡出；没挨打时头顶不常驻一条。</summary>
        internal const float HudLingerSeconds = 1.2f;
        /// <summary>连续中弹时钟鸣最短间隔，冲锋枪扫过来不连成一片。</summary>
        internal const float HumIntervalSeconds = 0.35f;

        public const string DescriptionCN = "钟守在铜片上写：它不出声，替你挨打时才响。\n<color=#E0B46A>【止语】</color>装备在图腾槽，身周罩一层 80 点的铜钟护罩。敌人的攻击先打在钟上，打穿的部分才落到你身上；爆炸也算，持续伤害不算。\n<color=#FFE4AC>【余响】</color>最后一次挨打 5 秒后，护罩每秒回 20 点；被打碎后至少静 9 秒才开始回。\n<color=#BBBBBB>卸下时护罩停在原样，再戴上接着用；死亡或切图后重新满罩。只护自己，基地不触发。来源：天空岛无声钟守好感 5 级的「钟守的铜钟罩」，30 枚随身贝壳兑换；婚后基地同样可换。</color>";
        public const string DescriptionEN = "The Bell Keeper scratched on the brass: It stays silent. It only rings when it takes a hit for you.\n<color=#E0B46A>[Hush]</color> Equip in a totem slot to wrap yourself in an 80-point bronze bell ward. Enemy attacks hit the bell first; only what breaks through reaches you. Explosions count; damage over time does not.\n<color=#FFE4AC>[Echo]</color> 5 seconds after the last hit, the ward refills 20 points per second. Once shattered, it stays quiet for at least 9 seconds before refilling.\n<color=#BBBBBB>Unequipping keeps the ward as it is; equip it again to carry on. Death or changing scenes refills it. Protects only you. Inactive in the base. Source: the Bell Keeper's Bronze Ward on Sky Island at affinity Lv.5, for 30 seashells from your backpack; also available at home after marriage.</color>";
    }
}
