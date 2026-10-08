namespace BossRush
{
    /// <summary>装备 bundle 配置器的装配点；执行顺序沿用原 EquipmentFactory 路径。</summary>
    internal static class EquipmentConfiguratorBootstrap
    {
        internal static void RegisterAll()
        {
            DragonKingBossGunConfig.RegisterEquipmentConfigurator();
            DragonSetConfig.RegisterEquipmentConfigurator();
            DragonKingSetConfig.RegisterEquipmentConfigurator();
            FrostThunderSetConfig.RegisterEquipmentConfigurator();
            SkyIslandBossGearConfig.RegisterEquipmentConfigurator();
            FlightTotemConfig.RegisterEquipmentConfigurator();
            ReverseScaleConfig.RegisterEquipmentConfigurator();
            DragonBreathWeaponConfig.RegisterEquipmentConfigurator();
            ViperDaggerWeaponConfig.RegisterEquipmentConfigurator();
            SummonStaffWeaponConfig.RegisterEquipmentConfigurator();
            EnergyShieldWeaponConfig.RegisterEquipmentConfigurator();
            FrostSpearWeaponConfig.RegisterEquipmentConfigurator();
            ThunderRingWeaponConfig.RegisterEquipmentConfigurator();
            EmptyMagazineMineWeaponConfig.RegisterEquipmentConfigurator();
        }
    }
}
