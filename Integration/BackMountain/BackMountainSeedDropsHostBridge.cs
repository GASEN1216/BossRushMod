using ItemStatsSystem;

namespace BossRush
{
    /// <summary>奖励系统的原种子入口，转交已登记的后山模块。</summary>
    public partial class ModBehaviour
    {
        private void TryAddBackMountainSeedLoot(Inventory inv, CharacterMainControl bossMain) { backMountainRuntime.TryAddBackMountainSeedLoot(inv, bossMain); }
        private void TryAddBackMountainSeedToCharacterItem(CharacterMainControl bossMain) { backMountainRuntime.TryAddBackMountainSeedToCharacterItem(bossMain); }
        private void TryDropBackMountainSeedIntoWorld(CharacterMainControl bossMain) { backMountainRuntime.TryDropBackMountainSeedIntoWorld(bossMain); }
        internal bool IsBackMountainDragonDescendantBoss(CharacterMainControl bossMain) { return IsDragonDescendantBoss(bossMain); }
        internal bool IsBackMountainDragonKingBoss(CharacterMainControl bossMain) { return IsDragonKingBoss(bossMain); }
    }
}
