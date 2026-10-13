namespace BossRush
{
    /// <summary>甩锅许可证的数值与双语说明；炸弹跟随携带者的直接攻击转移。</summary>
    public static class PassTheBombConfig
    {
        public const string BaseName = "PassTheBomb";
        public const string LogPrefix = "[PassTheBomb]";
        public const string DisplayNameCN = "甩锅许可证";
        public const string DisplayNameEN = "License to Pass the Buck";
        public const float FuseSeconds = 5f;
        public const float CooldownSeconds = 12f;
        public const float BlastRadius = 3f;
        public const float BlastDamage = 160f;
        public const float PlayerBlastDamage = 40f;
        public const int ItemQuality = 5;
        public const float ItemWeight = 0.3f;
        public const int ItemValue = 18000;
        public const string IconPath = "Assets/Icons/pass_the_bomb.png";

        public const string DescriptionCN = "本证只管转交，不管谁签收。\n<color=#FFB25B>【这锅归你】</color>装备在图腾槽。直接命中敌人后，在对方头顶挂一颗 5 秒炸弹。当前携带者下一次直接打中另一角色，就把炸弹转给对方；也能甩回你头上，转移不重置倒计时。\n<color=#FFF1CE>【谁拿谁负责】</color>爆炸伤害携带者，以及 3 米内无遮挡的玩家和敌人；玩家承受 40 点基础物理伤害，其他角色为 160 点。友方只有自己携带炸弹时才会受伤。\n<color=#BBBBBB>冷却 12 秒，同时最多一颗。持续伤害、反伤与炸弹伤害不传递。携带者死亡会原地留弹；卸下图腾、主角死亡或切图会取消待爆炸弹，基地不触发。来源：天空岛浮舟的航前杂货，好感 3 级解锁。</color>";
        public const string DescriptionEN = "This license authorizes forwarding, not accepting responsibility.\n<color=#FFB25B>[Your Problem Now]</color> Equip in a totem slot. Directly hitting an enemy plants a 5s bomb above their head. When its current carrier next directly hits another character, the bomb passes to that target. It can come back to you; passing it never resets the fuse.\n<color=#FFF1CE>[Handle with Care]</color> The blast hits its carrier, plus you and enemies within 3m and line of sight. Base physical damage is 40 to you and 160 to other characters. Allies are only hurt when carrying the bomb themselves.\n<color=#BBBBBB>12s cooldown; only one bomb at a time. Damage over time, reflected damage and bomb damage cannot pass it. A dead carrier leaves the bomb on the ground. Unequipping, player death or changing scenes cancels it. Inactive in the base. Source: Fuzhou's Departure Supplies on Sky Island, affinity Lv.3.</color>";
    }
}
