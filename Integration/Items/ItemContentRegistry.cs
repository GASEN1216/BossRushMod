namespace BossRush
{
    public partial class ModBehaviour
    {
        private void RegisterItemContentConfigurators()
        {
            AwenCourierTokenConfig.RegisterConfigurator();
            ColdQuenchFluidConfig.RegisterConfigurator();
            BrickStoneConfig.RegisterConfigurator();
            DiamondConfig.RegisterConfigurator();
            DiamondRingConfig.RegisterConfigurator();
            CalmingDropsConfig.RegisterConfigurator();
            PeaceCharmConfig.RegisterConfigurator();
            DingdangDrawingConfig.RegisterConfigurator();
            AchievementMedalConfig.RegisterConfigurator();
            ItemFactory.RegisterConfigurator(ReverseScaleConfig.TotemTypeId, ReverseScaleConfig.ConfigureItem);
            WildHornConfig.RegisterConfigurator();
            AwenLootSweepTokenConfig.RegisterConfigurator();
            FactionFlagConfig.RegisterConfigurators();
            RespawnItemConfig.RegisterConfigurators();
            BloodhuntTransponderConfig.RegisterConfigurator();
            FoldableCoverPackConfig.RegisterConfigurator();
            ReinforcedRoadblockPackConfig.RegisterConfigurator();
            BarbedWirePackConfig.RegisterConfigurator();
            EmergencyRepairSprayConfig.RegisterConfigurator();
            ZombieTideInvitationConfig.RegisterConfigurator();
            ZombieTideBeaconConfig.RegisterConfigurator();
            ItemFactory.RegisterConfigurator(ADVENTURE_JOURNAL_TYPE_ID, OnAdventureJournalLoaded);
            ItemFactory.RegisterConfigurator(FenHuangHalberdIds.WeaponTypeId, OnFenHuangHalberdLoaded);
            ItemFactory.RegisterConfigurator(FrostmourneIds.WeaponTypeId, OnFrostmourneLoaded);
            ItemFactory.RegisterConfigurator(PhantomWitchConfig.ReservedScytheTypeId, OnPhantomWitchScytheLoaded);
        }
    }
}
