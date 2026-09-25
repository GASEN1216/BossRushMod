namespace BossRush
{
    internal sealed partial class ModeFRuntimeModule : BossRushRuntimeModuleBase
    {
        // Mode F 状态
        internal bool modeFActive = false;
        internal ModeFState modeFState = new ModeFState();
        public bool IsModeFActive { get { return modeFActive; } }
        public bool IsModeFPreparationPhase
        {
            get
            {
                return modeFActive &&
                       modeFState != null &&
                       modeFState.CurrentPhase == ModeFPhase.Preparation;
            }
        }

        private ModeDRuntimeModule modeD;
        private ModeDItemPool equipment;
        private ModeEFSpawnPreparation spawnPreparation;
        private ModeERuntimeModule modeE;
        private WavesArenaRuntimeModule arena;
        private System.Func<int> getTicketTypeId;
        private System.Func<bool> useRandomBossLoot;
        private System.Func<bool> isZombieModeActive;
        private System.Func<int> getZombieRunId;
        private System.Action<int, ZombieModeRunOnlyObjectKind, UnityEngine.GameObject, UnityEngine.Object, System.Action> registerZombieRunOnly;

        internal void BindSharedServices(ModeDRuntimeModule modeD, ModeERuntimeModule modeE, WavesArenaRuntimeModule arena, ModeEFSpawnPreparation spawnPreparation,
            System.Func<int> getTicketTypeId, System.Func<bool> useRandomBossLoot,
            System.Func<bool> isZombieModeActive, System.Func<int> getZombieRunId,
            System.Action<int, ZombieModeRunOnlyObjectKind, UnityEngine.GameObject, UnityEngine.Object, System.Action> registerZombieRunOnly)
        {
            this.modeD = modeD;
            this.equipment = modeD.ItemPool;
            this.modeE = modeE;
            this.arena = arena;
            this.spawnPreparation = spawnPreparation;
            this.getTicketTypeId = getTicketTypeId;
            this.useRandomBossLoot = useRandomBossLoot;
            this.isZombieModeActive = isZombieModeActive;
            this.getZombieRunId = getZombieRunId;
            this.registerZombieRunOnly = registerZombieRunOnly;
        }

        private void InitializeModeDItemPools() { equipment.InitializeModeDItemPools(equipment.FindTagByName); }

        private ModBehaviour owner;
        public override string ModuleName
        {
            get { return "ModeF"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
        }

        public override void OnDestroy()
        {
            // Mod 卸载时 Mode F 可能还在跑：状态卡是静态 owner，这里兜底销毁（切图走 ExitModeF 那一份）。
            ModeFStatusHud.Dispose();
            ResetPlayerBountyKillLatch();
            owner = null;
        }
    }
}
