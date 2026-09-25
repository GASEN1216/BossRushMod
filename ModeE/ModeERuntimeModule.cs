namespace BossRush
{
    internal sealed partial class ModeERuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour modeEHost;
        private float modeEIntegrityTimer;
        private ModeDRuntimeModule modeD;
        private ModeDItemPool equipment;
        private ModeEFSpawnPreparation spawnPreparation;
        private ModeEFMerchantCatalog merchantCatalog;
        private WavesArenaRuntimeModule arena;

        internal void BindSharedServices(ModeDRuntimeModule modeD, WavesArenaRuntimeModule arena, ModeEFSpawnPreparation spawnPreparation, ModeEFMerchantCatalog merchantCatalog)
        {
            this.modeD = modeD;
            this.equipment = modeD.ItemPool;
            this.arena = arena;
            this.spawnPreparation = spawnPreparation;
            this.merchantCatalog = merchantCatalog;
            BindMerchantRuntime();
        }

        private void InitializeModeDItemPools() { equipment.InitializeModeDItemPools(equipment.FindTagByName); }
        private void InitializeModeDEnemyPools() { modeD.InitializeModeDEnemyPools(arena.EnemyPresets, arena.GetLocalizedCharacterName); }


        public override string ModuleName
        {
            get { return "ModeE"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.modeEHost = owner;
        }

        public override void OnDestroy()
        {
            if (modeEHost != null)
            {
                DestroyModeEShellRuntimeState();
            }
            // 静态缓存兜底清理：Mode E 商人相关缓存
            ResetModeEMerchantStaticCaches();
            modeEHost = null;
        }
    }
}
