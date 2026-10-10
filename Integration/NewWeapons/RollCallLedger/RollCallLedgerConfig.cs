namespace BossRush
{
    /// <summary>点名册的玩法与文案单点；三人逐个记名，鼓励群战切换目标。</summary>
    public static class RollCallLedgerConfig
    {
        public const string BaseName = "RollCallLedger";
        public const string LogPrefix = "[RollCallLedger]";
        public const string DisplayNameCN = "教导主任的点名册";
        public const string DisplayNameEN = "Headmaster's Roll-Call Ledger";
        public const int TargetCount = 3;
        public const float WindowSeconds = 5f;
        public const float CooldownSeconds = 6f;
        public const float BonusBaseDamage = 20f;
        public const float BonusHitDamageRatio = 0.25f;
        public const int ItemQuality = 5;
        public const int ItemValue = 20000;
        public const float ItemWeight = 0.3f;
        public const string IconPath = "Assets/Items/roll_call_ledger_icon.png";

        // 每人以自己的点名一击结算，避免用轻弹点两人、重击第三人复制三份高伤。
        public const string DescriptionCN = "别只盯着一个，后排的也得点到。\n<color=#E99580>【全员到齐】</color>装备在图腾槽。5 秒内用武器直接命中 3 名不同敌人，依次记名；第三名到齐时，对仍存活的被点名敌人同时追加物理伤害：每人 20 点 + 点名该敌人那一击实际伤害的 25%。\n<color=#FFF1CE>【课堂纪律】</color>同一敌人只计一次，击杀也算点名；持续伤害、装备效果与友军不计。触发后冷却 6 秒，穿脱不能刷新冷却。卸下、死亡或切图会取消名单与待结算伤害，基地不触发。\n<color=#BBBBBB>来源：天空岛浮舟的航前杂货，好感 3 级解锁。</color>";
        public const string DescriptionEN = "The back row needs calling on, too.\n<color=#E99580>[All Present!]</color> Equip in a totem slot. Directly hit 3 different enemies with your weapons within 5s to call their names. On the third name, every surviving marked enemy takes bonus physical damage: 20 + 25% of the actual damage of the hit that marked that enemy.\n<color=#FFF1CE>[Classroom Rules]</color> Each enemy counts once; killing blows count. Damage over time, equipment effects and allies do not count. 6s cooldown after triggering; re-equipping cannot reset it. Unequipping, death or changing scenes cancels marks and pending damage. Inactive in the base.\n<color=#BBBBBB>Source: Fuzhou's Departure Supplies on Sky Island, affinity Lv.3.</color>";
    }
}
