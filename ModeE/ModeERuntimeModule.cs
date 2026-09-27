namespace BossRush
{
    internal sealed partial class ModeERuntimeModule : BossRushRuntimeModuleBase
    {
        private ModeEFEnemyRegistry enemyRegistry;
        internal void BindEnemyRegistry(ModeEFEnemyRegistry registry)
        {
            enemyRegistry = registry;
        }

        private ModeEFEnemySpawnRuntime spawnRuntime;

        internal void BindEnemySpawnRuntime(ModeEFEnemySpawnRuntime runtime)
        {
            spawnRuntime = runtime;
        }

        private ModeEFVirtualSpawnerRegistry virtualSpawnerRegistry;

        internal void BindVirtualSpawnerRegistry(ModeEFVirtualSpawnerRegistry registry)
        {
            virtualSpawnerRegistry = registry;
        }

        private ModBehaviour modeEHost;
        private bool modeERuntimeDestroyed;
        private bool modeECleanupPending;
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
            modeERuntimeDestroyed = false;
        }

        public override void OnDestroy()
        {
            if (modeERuntimeDestroyed) return;
            modeERuntimeDestroyed = true;
            InvalidateModeESession();
            // 场景预热早于 BeginSession，也归此 owner 回收，不能借机清其它模式的状态。
            StopModeEStartupWarmupIfPending();
            if (!object.ReferenceEquals(modeEHost, null))
            {
                EndModeE(false);
                DestroyModeEShellRuntimeState();
            }
            // 静态缓存兜底清理：Mode E 商人相关缓存
            ResetModeEMerchantStaticCaches();
            modeEHost = null;
        }
    }
}
