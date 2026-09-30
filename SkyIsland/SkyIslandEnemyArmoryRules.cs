namespace BossRush
{
    /// <summary>一档敌人手里武器的品质带：先在 [Min, Max] 里抽，抽不到退到 [Floor, Max]，再抽不到才保留官方原装。</summary>
    internal struct SkyIslandWeaponBand
    {
        internal int Min, Max, Floor, AmmoMin;

        internal SkyIslandWeaponBand(int min, int max, int floor, int ammoMin)
        {
            Min = min; Max = max; Floor = floor; AmmoMin = ammoMin;
        }

        /// <summary>手里这把够不够格：达到本档下限的官方原装不换（连同它的原配弹药），高出上限也留着。</summary>
        internal bool Keeps(int quality) { return quality >= Min; }
    }

    /// <summary>
    /// COMPAT：天空岛敌人的武器品质表（owner 2026-09-30：岛上敌人至少品质 3，头目 / 岛主至少品质 5）。
    ///
    /// 天空岛所有敌人都从官方普通拾荒者 preset 克隆（头目 / 岛主也是，`VanillaPresetId` 只供数值参照），
    /// 官方随机装配常给一把品质 1 的斧头；Forge 只换护甲不换武器。这张表决定 `SkyIslandEnemyArmory`
    /// 在官方创建完成后把主武器与近战换成什么档次。
    ///
    /// 纯逻辑、无 Unity 依赖：隔离回归直接链接（tests/fixtures/SkyIslandEncounters）。
    /// </summary>
    internal static class SkyIslandEnemyArmoryRules
    {
        /// <summary>全岛任何敌人手里武器的最低品质。</summary>
        internal const int IslandFloor = 3;
        /// <summary>具名对手、头目、岛主、噬风手里武器的最低品质。</summary>
        internal const int BossFloor = 5;
        /// <summary>巡守从这一级起按精英档配枪（A..H 岛区 Rank 1..8，四小岛 3/4/6/7）。</summary>
        internal const int PatrolEliteRank = 5;

        /// <summary>
        /// 本 Mod 自己的物品号段（根 AGENTS.md §4.3 的 500xxx）。其它 Mod 注册的武器照发（owner 2026-09-30），
        /// 只有本 Mod 的不发：龙息、断界戟、五把新武器这类主动能力的 owner 绑在主角身上，
        /// 而且它们是 Boss 奖励 / 传说级掉落，让岛上每个头目都掉一把会冲掉原有的获取途径。
        /// </summary>
        internal const int OwnItemMin = 500001;
        internal const int OwnItemMax = 500999;

        internal static SkyIslandWeaponBand For(SkyIslandEnemyTier tier)
        {
            switch (tier)
            {
                case SkyIslandEnemyTier.Elite: return new SkyIslandWeaponBand(4, 5, IslandFloor, 3);
                case SkyIslandEnemyTier.Champion:
                case SkyIslandEnemyTier.Chief: return new SkyIslandWeaponBand(5, 6, BossFloor, 4);
                case SkyIslandEnemyTier.Storm:
                case SkyIslandEnemyTier.Lord: return new SkyIslandWeaponBand(6, 7, BossFloor, 5);
                default: return new SkyIslandWeaponBand(3, 4, IslandFloor, 2);
            }
        }

        internal static SkyIslandWeaponBand ForPatrol(int rank)
        {
            return For(rank >= PatrolEliteRank ? SkyIslandEnemyTier.Elite : SkyIslandEnemyTier.Scav);
        }

        internal static bool IsBossTier(SkyIslandEnemyTier tier)
        {
            return tier == SkyIslandEnemyTier.Champion || tier == SkyIslandEnemyTier.Chief
                || tier == SkyIslandEnemyTier.Storm || tier == SkyIslandEnemyTier.Lord;
        }

        internal static bool IsOwnModItem(int typeId)
        {
            return typeId >= OwnItemMin && typeId <= OwnItemMax;
        }

        /// <summary>弹匣装满之外背包里再带多少发：两个弹匣，最少 20 发。</summary>
        internal static int ReserveRounds(int capacity)
        {
            int reserve = capacity * 2;
            return reserve < 20 ? 20 : reserve;
        }
    }
}
