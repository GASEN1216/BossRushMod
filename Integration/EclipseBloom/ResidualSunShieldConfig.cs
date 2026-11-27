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

        public const string DescriptionCN = "花冠熄灭后，最后一瓣仍记得太阳的方向。\n<color=#FFC976>【残日护身】</color>图腾槽生效。翻滚瞬间在朝向展开 0.65 秒护盾，将前方 120 度内一次直接命中减伤 70%（上限 60 点，冷却 4 秒）。\n<color=#FFE4AC>【余晖反击】</color>成功阻挡后，6 秒内下次直接命中附加等额反击伤害（上限 60 点，不叠加）；持续伤害、爆炸与友军不触发。\n<color=#BBBBBB>卸下、死亡或切图清除状态，无法刷新冷却，基地不生效。来源：蚀日花冠专属掉落。</color>";
        public const string DescriptionEN = "When the crown goes dark, its last petal still remembers the sun.\n<color=#FFC976>[Residual Guard]</color> Totem slot. On dodging, raise a 0.65s frontal shield (120-deg arc) reducing one direct hit by 70% (up to 60 blocked; 4s CD).\n<color=#FFE4AC>[Afterglow Riposte]</color> Blocking empowers your next direct hit within 6s with bonus damage equal to the amount blocked (up to 60; holds 1 charge). DoT, explosions, gear effects, and allies do not trigger it.\n<color=#BBBBBB>Unequipping, death, or changing scenes clears the state without resetting cooldown. Inactive in base. Drop: Eclipse Bloom.</color>";
    }
}
