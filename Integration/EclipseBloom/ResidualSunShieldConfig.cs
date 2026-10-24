namespace BossRush
{
    /// <summary>残日盾片的数值与双语说明；仅从蚀日花冠的专属掉落取得。</summary>
    public static class ResidualSunShieldConfig
    {
        public const string BaseName = "ResidualSunShield";
        public const string LogPrefix = "[ResidualSunShield]";
        public const string DisplayNameCN = "残日盾片";
        public const string DisplayNameEN = "Residual Sun Shield Shard";
        public const float GuardSeconds = 0.65f;
        public const float CooldownSeconds = 4f;
        public const float ChargeSeconds = 6f;
        public const float AbsorptionRatio = 0.7f;
        public const float MaximumAbsorption = 60f;
        public const float FrontalCosine = 0.5f;
        public const int ItemQuality = 6;
        public const int ItemValue = 24000;
        public const float ItemWeight = 0.3f;

        public const string DescriptionCN = "花冠熄灭后，最后一瓣仍记得太阳的方向。\n<color=#FFC976>【残日护身】</color>装备在图腾槽。翻滚成功开始时，在翻滚朝向的前方展开 0.65 秒护盾，挡住前方 120 度内的一次敌方直接攻击：伤害结算前减免 70%，最多阻挡 60 点。冷却 4 秒。\n<color=#FFE4AC>【余晖反击】</color>成功阻挡后，6 秒内下一次直接命中敌人追加等于阻挡量的伤害，最多 60 点；只保留一份充能。持续伤害、装备效果、爆炸与友军不触发。\n<color=#BBBBBB>卸下、死亡或切图会取消护盾与充能；穿脱不能刷新冷却，基地不触发。来源：蚀日花冠专属掉落。</color>";
        public const string DescriptionEN = "When the crown goes dark, its last petal still remembers the sun.\n<color=#FFC976>[Residual Guard]</color> Equip in a totem slot. Starting a successful dodge opens a 0.65s shield facing the dodge direction. It reduces one direct enemy hit within the frontal 120-degree arc by 70% before health loss, blocking up to 60 damage. 4s cooldown.\n<color=#FFE4AC>[Afterglow Riposte]</color> A successful block empowers your next direct hit against an enemy within 6s with bonus damage equal to the amount blocked, up to 60. Only one charge is held. Damage over time, equipment effects, explosions and allies cannot trigger it.\n<color=#BBBBBB>Unequipping, death or changing scenes cancels the guard and charge. Re-equipping cannot reset the cooldown. Inactive in the base. Source: exclusive drop from Eclipse Bloom.</color>";
    }
}
