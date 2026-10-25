namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private static ModeDRuntimeModule current;
        private int generation;
        private bool modeDCleanupPending;
        internal bool IsActive { get { return modeDActive; } }
        internal System.Collections.Generic.IReadOnlyList<EnemyPresetInfo> MinionPresets { get { return modeDMinionPool; } }
        internal static System.Collections.Generic.IReadOnlyDictionary<string, CharacterRandomPreset> CharacterPresets { get { return cachedCharacterPresets; } }
        internal bool modeDActive;
        internal int modeDWaveIndex;
        internal readonly System.Collections.Generic.List<CharacterMainControl> modeDCurrentWaveEnemies =
            new System.Collections.Generic.List<CharacterMainControl>();
        internal readonly System.Collections.Generic.List<EnemyPresetInfo> modeDMinionPool =
            new System.Collections.Generic.List<EnemyPresetInfo>();
        internal System.Collections.Generic.List<EnemyPresetInfo> modeDBossPool;
        internal bool modeDEnemyPoolsInitialized;
        internal bool modeDWaveCompletePending;
        internal int modeDExpectedEnemiesInCurrentWave;
        internal int modeDSpawnResolvedInCurrentWave;
        internal UnityEngine.Coroutine modeDAutoNextWaveCoroutine;
        internal int modeDEnemiesPerWave = 3;
        internal readonly ModeDItemPool ItemPool = new ModeDItemPool();
        internal static System.Collections.Generic.Dictionary<string, CharacterRandomPreset> cachedCharacterPresets;
        internal static readonly System.Collections.Generic.List<EnemyPresetInfo> presetFilterCache =
            new System.Collections.Generic.List<EnemyPresetInfo>();
        internal static readonly System.Collections.Generic.List<EnemyPresetInfo> presetFilterCache2 =
            new System.Collections.Generic.List<EnemyPresetInfo>();

        internal static void ResetStaticCaches()
        {
            if (cachedCharacterPresets != null)
            {
                cachedCharacterPresets.Clear();
                cachedCharacterPresets = null;
            }

            presetFilterCache.Clear();
            presetFilterCache2.Clear();
        }

        internal static void Invalidate(ModBehaviour expectedOwner)
        {
            if (current != null && current.owner == expectedOwner) current.generation++;
        }

        internal static System.Func<bool> CaptureValidity(ModBehaviour expectedOwner, bool beginWave)
        {
            ModeDRuntimeModule runtime = current;
            if (runtime == null || runtime.owner != expectedOwner) return () => false;
            if (beginWave) runtime.generation++;
            int capturedGeneration = runtime.generation;
            int wave = runtime.modeDWaveIndex;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            // 死亡广播可能被其他订阅者截断；在 EndModeD 到来前也不能继续刷怪或自动开波。
            // 绑定原主角，防止同场景角色替换后把旧队列交给新角色。
            CharacterMainControl player = CharacterMainControl.Main;
            return () => runtime.owner != null && runtime.owner == expectedOwner && current == runtime
                && runtime.generation == capturedGeneration && runtime.modeDActive
                && runtime.modeDWaveIndex == wave
                && !SceneLoader.IsSceneLoading
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == scene
                && player != null && CharacterMainControl.Main == player
                && player.Health != null && !player.Health.IsDead;
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            generation++;
        }

        public override string ModuleName
        {
            get { return "ModeD"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            current = this;
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null)
            {
                return;
            }

            TickModeDIntegrity(deltaTime);
        }

        public override void OnDestroy()
        {
            generation++;
            CleanupModeDRuntimeOnDestroy();
            if (current == this) current = null;
            owner = null;
        }
    }
}
