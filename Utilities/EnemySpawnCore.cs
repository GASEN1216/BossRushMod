// ============================================================================
// EnemySpawnCore.cs - 通用敌人生成核心方法
// ============================================================================
// 模块说明：
//   提取 Mode D 和 Mode E 共用的敌人生成逻辑，消除重复代码。
//   包含预设查找、重试机制、龙裔遗族/龙王特殊处理、大兴兴标记、
//   属性标准化、装备配置、AI设置等通用流程。
//
//   各模式通过回调参数注入差异化逻辑（阵营设置、死亡注册、列表管理等）。
// ============================================================================

using System;
using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// 敌人生成后的配置回调参数
    /// </summary>
    public class EnemySpawnContext
    {
        /// <summary>生成的敌人角色</summary>
        public CharacterMainControl character;

        /// <summary>使用的预设信息</summary>
        public EnemyPresetInfo preset;

        /// <summary>是否为Boss</summary>
        public bool isBoss;

        /// <summary>生成位置</summary>
        public Vector3 position;

        /// <summary>Mode G 托管 Boss 的延迟激活/精确清理句柄；普通路径为 null。</summary>
        internal ManagedBossRuntimeHandle managedBossHandle;
    }

    internal sealed class EnemySpawnCoreResult
    {
        public bool success;
        public EnemySpawnContext context;
        public string failureReason;
        public EnemyPresetInfo actualPreset;

        public static EnemySpawnCoreResult Succeeded(EnemySpawnContext context, EnemyPresetInfo actualPreset)
        {
            return new EnemySpawnCoreResult
            {
                success = true,
                context = context,
                actualPreset = actualPreset
            };
        }

        public static EnemySpawnCoreResult Failed(string failureReason, EnemyPresetInfo actualPreset = null)
        {
            return new EnemySpawnCoreResult
            {
                success = false,
                failureReason = failureReason,
                actualPreset = actualPreset
            };
        }
    }

    /// <summary>
    /// SpawnCore 附加选项（Mode G 托管 Boss 门控，任务 #7 跨任务契约）。
    /// 不传或传 null 时行为逐字不变（完全 Legacy）。
    /// </summary>
    public sealed class EnemySpawnCoreOptions
    {
        /// <summary>
        /// 为外部提交保留：跳过 Legacy onCommit 提交回调，
        /// 由 Mode G 侧按 ManagedBossRuntimeHandle 自行提交。
        /// </summary>
        public bool HoldForExternalCommit = false;

        /// <summary>
        /// 是否应用共享 Mutator 词条（Mode G 托管 Boss 传 false）。
        /// </summary>
        public bool ApplySharedMutators = true;

        /// <summary>
        /// 失败后是否允许随机重试回退（Mode G 托管 Boss 传 false，只走调用方已确认预设）。
        /// </summary>
        public bool AllowRandomRetryFallback = true;

        /// <summary>
        /// 托管 Boss 上下文。用 object 引用避免与 Felix 的新类型（ManagedBossSpawnContext）
        /// 产生编辑冲突；Felix 侧自行强转。null = Legacy。
        /// </summary>
        public object ManagedBossContext = null;

        /// <summary>
        /// 本次生成不属于当前波次（随机事件乱入等），三个自定义 Boss 的专用生成器
        /// 据此跳过波次身份登记。
        ///
        /// 为什么必须有它：龙王/女巫的生成器无条件写波次身份容器，乱入时会把本波真 Boss
        /// 顶掉——单 Boss 档真 Boss 死亡不再推波，而乱入者被销毁后卡波自检又会读到
        /// 「无存活 Boss」而主动推波。龙裔早有同语义的 isChildProtectionSummon 先例。
        /// </summary>
        public bool SuppressWaveBossRegistration = false;
    }

    // 共享生成算法由一个实例执行；宿主只装配查询、动作与原调度入口。
    internal sealed class EnemySpawnRuntime
    {
        internal delegate UniTask<CharacterMainControl> DescendantSpawner(
            Vector3 position, bool isChildProtectionSummon, bool notifyBossRushOnFailure,
            bool deferActivationUntilNextFrame, bool isNonWaveSpawn, Func<bool> isActiveCheck);
        internal delegate UniTask<CharacterMainControl> SpecialBossSpawner(
            Vector3 position, bool notifyBossRushOnFailure,
            bool deferActivationUntilNextFrame, bool isNonWaveSpawn, Func<bool> isActiveCheck);

        private readonly ModeEFSpawnPostprocessScheduler spawnPostprocess;
        private Func<Dictionary<string, CharacterRandomPreset>> characterPresets;
        private Func<EnemyPresetInfo> GetRandomBossPreset;
        private Func<EnemyPresetInfo> GetRandomMinionPreset;
        private Func<bool> isDragonDescendantSpawned;
        private Func<bool> isDragonKingSpawned;
        private Func<EnemyPresetInfo, bool> IsDragonDescendantPreset;
        private Func<EnemyPresetInfo, bool> IsDragonKingPreset;
        private Func<EnemyPresetInfo, bool> IsPhantomWitchPreset;
        private Func<EnemyPresetInfo, bool> IsManagedBossPreset;
        private DescendantSpawner SpawnDragonDescendant;
        private SpecialBossSpawner SpawnDragonKing;
        private SpecialBossSpawner SpawnPhantomWitch;
        private Action<CharacterMainControl> CleanupCancelledDragonDescendant;
        private Action<CharacterMainControl> CleanupCancelledDragonKing;
        private Action<CharacterMainControl> CleanupFailedPhantomWitchSpawn;
        private Action<CharacterMainControl> NormalizeDamageMultiplier;
        private Action<CharacterMainControl, int, float, bool> EquipEnemyForModeD;
        private Func<CharacterMainControl, int, float, bool, SharedModeEnemyEquipmentMaterializationPlan> CreateSharedModeEnemyEquipmentMaterializationPlan;
        private Action<CharacterMainControl> ApplyBossStatMultiplier;
        private Action<CharacterMainControl, int> RegisterBossRandomLootTracking;
        private Func<EnemyPresetInfo, bool> IsDaXingXingPreset;
        private Func<HashSet<CharacterMainControl>> ownedDaXingXing;
        private Dictionary<string, CharacterRandomPreset> cachedCharacterPresets { get { return characterPresets(); } }
        private bool modeEDragonDescendantSpawned { get { return isDragonDescendantSpawned(); } }
        private bool modeEDragonKingSpawned { get { return isDragonKingSpawned(); } }
        private HashSet<CharacterMainControl> bossRushOwnedDaXingXing { get { return ownedDaXingXing(); } }

        internal EnemySpawnRuntime(ModeEFSpawnPostprocessScheduler postprocess)
        {
            spawnPostprocess = postprocess;
        }

        internal void BindPresetQueries(
            Func<Dictionary<string, CharacterRandomPreset>> characterPresets,
            Func<EnemyPresetInfo> randomBoss, Func<EnemyPresetInfo> randomMinion,
            Func<bool> descendantSpawned, Func<bool> kingSpawned)
        {
            this.characterPresets = characterPresets;
            GetRandomBossPreset = randomBoss;
            GetRandomMinionPreset = randomMinion;
            isDragonDescendantSpawned = descendantSpawned;
            isDragonKingSpawned = kingSpawned;
        }

        internal void BindSpecialBossServices(
            Func<EnemyPresetInfo, bool> descendant, Func<EnemyPresetInfo, bool> king,
            Func<EnemyPresetInfo, bool> witch, Func<EnemyPresetInfo, bool> managed,
            DescendantSpawner spawnDescendant, SpecialBossSpawner spawnKing, SpecialBossSpawner spawnWitch,
            Action<CharacterMainControl> cleanupDescendant, Action<CharacterMainControl> cleanupKing,
            Action<CharacterMainControl> cleanupWitch)
        {
            IsDragonDescendantPreset = descendant;
            IsDragonKingPreset = king;
            IsPhantomWitchPreset = witch;
            IsManagedBossPreset = managed;
            SpawnDragonDescendant = spawnDescendant;
            SpawnDragonKing = spawnKing;
            SpawnPhantomWitch = spawnWitch;
            CleanupCancelledDragonDescendant = cleanupDescendant;
            CleanupCancelledDragonKing = cleanupKing;
            CleanupFailedPhantomWitchSpawn = cleanupWitch;
        }

        internal void BindEquipmentServices(
            Action<CharacterMainControl> normalize,
            Action<CharacterMainControl, int, float, bool> equip,
            Func<CharacterMainControl, int, float, bool, SharedModeEnemyEquipmentMaterializationPlan> createPlan,
            Action<CharacterMainControl> applyMultiplier,
            Action<CharacterMainControl, int> registerLoot)
        {
            NormalizeDamageMultiplier = normalize;
            EquipEnemyForModeD = equip;
            CreateSharedModeEnemyEquipmentMaterializationPlan = createPlan;
            ApplyBossStatMultiplier = applyMultiplier;
            RegisterBossRandomLootTracking = registerLoot;
        }

        internal void BindOwnedEnemyTracking(
            Func<EnemyPresetInfo, bool> isDaXingXing,
            Func<HashSet<CharacterMainControl>> owned)
        {
            IsDaXingXingPreset = isDaXingXing;
            ownedDaXingXing = owned;
        }

        /// <summary>
        /// 通用敌人生成核心方法
        /// <para>处理预设查找、重试、龙裔遗族/龙王特殊生成、大兴兴标记、属性标准化等通用流程。</para>
        /// <para>生成成功后调用 onSpawned 回调，由调用方注入差异化逻辑（阵营设置、死亡注册等）。</para>
        /// <para>所有重试均失败时调用 onFailed 回调（可选），用于 Mode D 波次计数等兜底逻辑。</para>
        /// </summary>
        /// <param name="preset">初始预设信息</param>
        /// <param name="position">生成位置</param>
        /// <param name="isBoss">是否为Boss</param>
        /// <param name="isActiveCheck">检查模式是否仍然激活的委托（用于中途退出）</param>
        /// <param name="onSpawned">生成成功后的回调（设置阵营、注册死亡、加入列表等）</param>
        /// <param name="onFailed">所有重试均失败后的回调（可选，用于波次计数兜底等）</param>
        /// <param name="waveIndex">波次索引（Mode D 用于配装品质计算，Mode E 传 1）</param>
        /// <param name="skipDragonDescendant">Mode E 用：重试时跳过龙裔遗族预设</param>
        /// <param name="skipDragonKing">Mode E 用：重试时跳过龙王预设</param>
        /// <param name="skipBossRushLootTracking">跳过 BossRush 随机掉落追踪；独立模式复用 Boss 刷怪但自管掉落时必须开启。</param>
        /// <param name="normalizeDamageMultiplier">是否执行 Mode D 的伤害倍率归一化。</param>
        internal void SpawnEnemyCore(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned,
            Action onFailed = null,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false,
            bool applyEquipment = true,
            bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null)
        {
            SpawnEnemyCoreFireAndForgetAsync(
                preset,
                position,
                isBoss,
                isActiveCheck,
                onSpawned,
                onFailed,
                waveIndex,
                skipDragonDescendant,
                skipDragonKing,
                applyEquipment,
                applyBossMultiplier,
                directPreset,
                skipBossRushLootTracking,
                normalizeDamageMultiplier,
                deferActivationUntilNextFrame,
                onCommit).Forget();
        }

        private async UniTaskVoid SpawnEnemyCoreFireAndForgetAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned,
            Action onFailed = null,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false,
            bool applyEquipment = true,
            bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null)
        {
            EnemySpawnCoreResult result = await SpawnEnemyCoreInternalAsync(
                preset,
                position,
                isBoss,
                isActiveCheck,
                waveIndex,
                skipDragonDescendant,
                skipDragonKing,
                applyEquipment,
                applyBossMultiplier,
                directPreset,
                skipBossRushLootTracking,
                normalizeDamageMultiplier,
                deferActivationUntilNextFrame,
                onCommit);

            if (result != null && result.success)
            {
                try
                {
                    if (onSpawned != null)
                    {
                        onSpawned(result.context);
                    }
                }
                catch (Exception callbackEx)
                {
                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 生成成功回调执行异常: " + callbackEx.Message);
                    InvokeSpawnCoreFailureCallback(onFailed, "成功回调异常");
                }
                return;
            }

            string reason = result != null ? result.failureReason : "结果为空";
            InvokeSpawnCoreFailureCallback(onFailed, reason);
        }

        /// <summary>
        /// Mode G 托管 Boss 生成分流钩子（加法分支，默认 null = 对 Legacy 无任何影响）。
        /// 由 Felix 的 Utilities/ManagedBossSpawnContracts.cs 接线；
        /// 输入 (preset, position, managedContext, deferActivationUntilNextFrame)，
        /// 返回 prepared Character + handle，失败返回 null。
        /// 钩子未接线或返回 null 均 fail-closed，绝不回退 Legacy 生成器。
        /// </summary>
        internal static Func<EnemyPresetInfo, Vector3, object, bool, UniTask<ManagedBossPrepareResult>> ManagedBossSpawnDispatcher;

        internal async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false,
            bool applyEquipment = true,
            bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null,
            EnemySpawnCoreOptions options = null)
        {
            try
            {
                const int maxAttempts = 5;
                // Mode G 禁用随机回退时只跑调用方已确认预设；Legacy 仍保持 5 次重试。
                int attemptLimit = (options != null && !options.AllowRandomRetryFallback) ? 1 : maxAttempts;
                EnemyPresetInfo currentPreset = preset;

                // 记录调用方传入的原始预设，用于区分"首次使用"和"重试随机"
                EnemyPresetInfo originalPreset = preset;

                // Mode G 门控（加法分支）：托管 Boss 上下文；null = Legacy
                object managedBossContext = options != null ? options.ManagedBossContext : null;

                for (int attempt = 0; attempt < attemptLimit; attempt++)
                {
                    try
                    {
                        // 预设为空时重新随机
                        if (currentPreset == null)
                        {
                            // Mode G 门控（加法分支）：禁用随机重试回退时不重新随机，直接 fail-closed
                            if (options != null && !options.AllowRandomRetryFallback)
                            {
                                return EnemySpawnCoreResult.Failed("预设为空且禁用随机重试", preset);
                            }

                            currentPreset = isBoss ? GetRandomBossPreset() : GetRandomMinionPreset();
                        }
                        if (currentPreset == null)
                        {
                            ModBehaviour.DevLog("[SpawnCore] 预设为空, attempt=" + attempt);
                            continue;
                        }

                        // Mode E 龙限制：仅在"重试随机"时检查（currentPreset != originalPreset）
                        // 首次循环使用的是调用方已确认的预设，不应被跳过
                        // 重试时需要同时检查 skip 参数和实例字段（防止并发竞态）
                        bool isRetry = (currentPreset != originalPreset);
                        if (isRetry && (skipDragonDescendant || modeEDragonDescendantSpawned) && IsDragonDescendantPreset(currentPreset))
                        {
                            ModBehaviour.DevLog("[SpawnCore] 重试跳过龙裔遗族（已达上限）");
                            currentPreset = null;
                            continue;
                        }
                        if (isRetry && (skipDragonKing || modeEDragonKingSpawned) && IsDragonKingPreset(currentPreset))
                        {
                            ModBehaviour.DevLog("[SpawnCore] 重试跳过龙王（已达上限）");
                            currentPreset = null;
                            continue;
                        }
                        if (isRetry && IsPhantomWitchPreset(currentPreset))
                        {
                            ModBehaviour.DevLog("[SpawnCore] 重试跳过幽灵女巫（同一波次不重复）");
                            currentPreset = null;
                            continue;
                        }

                        ModBehaviour.DevLog("[SpawnCore] 生成敌人: " + currentPreset.displayName + " (isBoss=" + isBoss + ", attempt=" + attempt + ")");

                        int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                        CharacterMainControl character = null;
                        ManagedBossRuntimeHandle managedBossHandle = null;

                        // Mode G 托管 Boss 分流（加法分支）：带托管 context 时必须走托管生成契约
                        // （Felix 侧 Prepare/Activate 分离 + ManagedBossRuntimeHandle），
                        // 绝不回退 Legacy 公开生成器；未接线/失败即 fail-closed。
                        if (managedBossContext != null)
                        {
                            var managedDispatcher = ManagedBossSpawnDispatcher;
                            if (managedDispatcher == null)
                            {
                                ModBehaviour.DevLog("[SpawnCore] [ERROR] 传入托管 Boss 上下文但 ManagedBossSpawnDispatcher 未接线，fail-closed");
                                return EnemySpawnCoreResult.Failed("托管生成契约未接线", currentPreset);
                            }

                            try
                            {
                                ManagedBossPrepareResult prepared = await managedDispatcher(
                                    currentPreset, position, managedBossContext, deferActivationUntilNextFrame);
                                if (prepared != null)
                                {
                                    character = prepared.Character;
                                    managedBossHandle = prepared.Handle;
                                }
                            }
                            catch (Exception managedEx)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 托管 Boss 生成异常: " + managedEx.Message);
                                character = null;
                            }

                            if (character == null || managedBossHandle == null)
                            {
                                if (options != null && !options.AllowRandomRetryFallback)
                                {
                                    return EnemySpawnCoreResult.Failed("托管 Boss 生成失败（禁用重试）", currentPreset);
                                }

                                currentPreset = null;
                                continue;
                            }
                        }
                        // 龙裔遗族Boss：使用专用生成方法
                        else if (IsDragonDescendantPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnDragonDescendant(
                                    position,
                                    isChildProtectionSummon: false,
                                    notifyBossRushOnFailure: false,
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame,
                                    isNonWaveSpawn: options != null && options.SuppressWaveBossRegistration,
                                    isActiveCheck: isActiveCheck);
                            }
                            catch (Exception dragonEx)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 龙裔遗族生成异常: " + dragonEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }
                        // 龙王Boss：使用专用生成方法
                        else if (IsDragonKingPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnDragonKing(
                                    position,
                                    notifyBossRushOnFailure: false,
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame,
                                    isNonWaveSpawn: options != null && options.SuppressWaveBossRegistration,
                                    isActiveCheck: isActiveCheck);
                            }
                            catch (Exception kingEx)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 龙王生成异常: " + kingEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }
                        // 幽灵女巫Boss：使用专用生成方法
                        else if (IsPhantomWitchPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnPhantomWitch(
                                    position,
                                    notifyBossRushOnFailure: false,
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame,
                                    isNonWaveSpawn: options != null && options.SuppressWaveBossRegistration,
                                    isActiveCheck: isActiveCheck);
                            }
                            catch (Exception witchEx)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 幽灵女巫生成异常: " + witchEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }
                        // 普通预设：通过 CharacterRandomPreset 生成
                        else
                        {
                            CharacterRandomPreset targetPreset = directPreset;
                            if (targetPreset == null && cachedCharacterPresets != null)
                            {
                                cachedCharacterPresets.TryGetValue(currentPreset.name, out targetPreset);
                            }

                            if (targetPreset == null)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 未找到预设: " + currentPreset.name);
                                currentPreset = null;
                                continue;
                            }

                            try
                            {
                                character = await targetPreset.CreateCharacterAsync(position, Vector3.forward, relatedScene, null, false);
                            }
                            catch (Exception createEx)
                            {
                                ModBehaviour.DevLog("[SpawnCore] 生成敌人异常: " + createEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }

                        if (character == null)
                        {
                            ModBehaviour.DevLog("[SpawnCore] 生成敌人失败: " + (currentPreset != null ? currentPreset.displayName : "null"));
                            currentPreset = null;
                            continue;
                        }

                        // 判断是否为 BossRush 自定义特殊 Boss。
                        // 这些 Boss 在各自生成方法内自管装备、能力、激活与掉落追踪；
                        // SpawnCore 这里只补统一的伤害倍率归一化、Mutator 和提交回调。
                        bool isManagedBossSpawn = IsManagedBossPreset(currentPreset);

                        if (isManagedBossSpawn)
                        {
                            if (!isActiveCheck())
                            {
                                try
                                {
                                    if (character.gameObject != null)
                                    {
                                        if (IsDragonDescendantPreset(currentPreset)) CleanupCancelledDragonDescendant(character);
                                        else if (IsDragonKingPreset(currentPreset)) CleanupCancelledDragonKing(character);
                                        else CleanupFailedPhantomWitchSpawn(character);
                                    }
                                }
                                catch (Exception destroyEx)
                                {
                                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 模式结束时销毁特殊生成敌人失败: " + destroyEx.Message);
                                }

                                ModBehaviour.DevLog("[SpawnCore] 模式已结束，销毁特殊生成的敌人");
                                return EnemySpawnCoreResult.Failed("模式结束", currentPreset);
                            }

                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position,
                                managedBossHandle = managedBossHandle
                            };

                            // 标记大兴兴（防止被误清理）
                            TryTrackSpawnCoreDaXingXing(character, currentPreset);

                            if (normalizeDamageMultiplier)
                            {
                                // 统一伤害倍率（龙裔/龙王也需要）
                                NormalizeDamageMultiplier(character);
                            }

                            // 跳过 EquipEnemyForModeD（龙裔/龙王已在内部完成配装）
                            // 跳过 ApplyBossStatMultiplier（龙裔/龙王已在内部调用过）
                            // 跳过 SetActive（龙裔/龙王已在内部激活）

                            // 应用变异词条效果到特殊Boss
                            // Mode G 门控（加法分支）：托管 Boss 禁用共享 Mutator；options == null 逐字保持原行为
                            if (options == null || options.ApplySharedMutators)
                            {
                                MutatorManager.ApplyToEnemy(character);
                            }

                            // Mode G 门控（加法分支）：HoldForExternalCommit 时跳过 Legacy 提交回调，
                            // 由 Mode G 按 handle 自行提交；options == null 逐字保持原行为
                            if (options == null || !options.HoldForExternalCommit)
                            {
                                if (!ModeEFSpawnPostprocessScheduler.InvokeSpawnCoreCommitCallback(onCommit, ctx))
                                {
                                    try
                                    {
                                        if (character.gameObject != null)
                                        {
                                            UnityEngine.Object.Destroy(character.gameObject);
                                        }
                                    }
                                    catch (Exception destroyOnCommitEx)
                                    {
                                        ModBehaviour.DevLog("[SpawnCore] [WARNING] 特殊Boss提交失败后销毁异常: " + destroyOnCommitEx.Message);
                                    }

                                    return EnemySpawnCoreResult.Failed("提交回调失败", currentPreset);
                                }
                            }

                            ModBehaviour.DevLog("[SpawnCore] 敌人生成成功: " + currentPreset.displayName);
                            return EnemySpawnCoreResult.Succeeded(ctx, currentPreset);
                        }
                        else
                        {
                            // 普通敌人：保持原有流程

                            // [性能优化] 角色创建完成后让出一帧，把配装操作分散到下一帧
                            await UniTask.Yield();

                            // 模式已结束，销毁并退出
                            if (!isActiveCheck())
                            {
                                UnityEngine.Object.Destroy(character.gameObject);
                                ModBehaviour.DevLog("[SpawnCore] 模式已结束，销毁生成的敌人");
                                return EnemySpawnCoreResult.Failed("模式结束", currentPreset);
                            }

                            // 标记大兴兴（防止被误清理）
                            TryTrackSpawnCoreDaXingXing(character, currentPreset);

                            if (normalizeDamageMultiplier)
                            {
                                // 统一伤害倍率
                                NormalizeDamageMultiplier(character);
                            }

                            if (deferActivationUntilNextFrame)
                            {
                                SharedModeEnemyEquipmentMaterializationPlan equipmentPlan = applyEquipment
                                    ? CreateSharedModeEnemyEquipmentMaterializationPlan(character, waveIndex, currentPreset.baseHealth, isBoss)
                                    : null;

                                return await spawnPostprocess.ScheduleModeEFSpawnPostprocessAsync(
                                    character,
                                    currentPreset,
                                    isBoss,
                                    position,
                                    isActiveCheck,
                                    equipmentPlan,
                                    applyBossMultiplier,
                                    skipBossRushLootTracking,
                                    onCommit,
                                    options);
                            }

                            // 应用配装（Boss 保留原有头盔和护甲）
                            if (applyEquipment)
                            {
                                EquipEnemyForModeD(character, waveIndex, currentPreset.baseHealth, isBoss);
                            }

                            // 应用全局 Boss 数值倍率
                            if (applyBossMultiplier)
                            {
                                ApplyBossStatMultiplier(character);
                            }

                            // Mode G 外部提交路径先冻结，全部槽位结案后由 run owner 批量激活。
                            if (options != null && options.HoldForExternalCommit)
                            {
                                if (character.Health != null) character.Health.SetInvincible(true);
                                character.gameObject.SetActive(false);
                            }
                            else
                            {
                                character.gameObject.SetActive(true);
                                // 同分帧路径：解除官方距离休眠，否则远处刷出的怪会被每帧关掉。
                                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                            }

                            // 应用变异词条效果到新生成的敌人
                            // Mode G 门控（加法分支）：options == null 逐字保持原行为
                            if (options == null || options.ApplySharedMutators)
                            {
                                MutatorManager.ApplyToEnemy(character);
                            }

                            if (isBoss && !skipBossRushLootTracking && character != null)
                            {
                                try
                                {
                                    int originalLootCount = 0;
                                    if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                                    {
                                        // Keep the common spawn path aligned with the legacy BossRush flow.
                                        originalLootCount = 3;
                                    }

                                    RegisterBossRandomLootTracking(character, originalLootCount);
                                    ModBehaviour.DevLog("[SpawnCore] 已注册 Boss 掉落追踪: " + currentPreset.displayName
                                        + " (原始掉落数量=" + originalLootCount + ")");
                                }
                                catch (Exception lootTrackEx)
                                {
                                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 注册 Boss 掉落追踪失败: " + lootTrackEx.Message);
                                }
                            }

                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position
                            };

                            // Mode G 门控（加法分支）：HoldForExternalCommit 时跳过 Legacy 提交回调；
                            // options == null 逐字保持原行为
                            if (options == null || !options.HoldForExternalCommit)
                            {
                                if (!ModeEFSpawnPostprocessScheduler.InvokeSpawnCoreCommitCallback(onCommit, ctx))
                                {
                                    UnityEngine.Object.Destroy(character.gameObject);
                                    ModBehaviour.DevLog("[SpawnCore] 提交回调失败，销毁普通Boss: " + currentPreset.displayName);
                                    return EnemySpawnCoreResult.Failed("提交回调失败", currentPreset);
                                }
                            }

                            ModBehaviour.DevLog("[SpawnCore] 敌人生成成功: " + currentPreset.displayName);
                            return EnemySpawnCoreResult.Succeeded(ctx, currentPreset);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[SpawnCore] [ERROR] 尝试失败: " + e.Message);
                        currentPreset = null;
                    }
                }

                ModBehaviour.DevLog("[SpawnCore] [ERROR] 多次尝试仍然失败");

                // 所有重试均失败，调用失败回调（用于 Mode D 波次计数兜底等）
                return EnemySpawnCoreResult.Failed("重试耗尽", currentPreset);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SpawnCore] [ERROR] SpawnEnemyCore 异常: " + e.Message);

                // 异常情况也调用失败回调，确保调用方计数不会卡住
                return EnemySpawnCoreResult.Failed("主流程异常", preset);
            }
        }

        private void TryTrackSpawnCoreDaXingXing(CharacterMainControl character, EnemyPresetInfo preset)
        {
            try
            {
                if (!IsDaXingXingPreset(preset))
                {
                    return;
                }

                if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                {
                    bossRushOwnedDaXingXing.Add(character);
                }
            }
            catch (Exception trackEx)
            {
                string presetName = preset != null ? preset.displayName : "null";
                ModBehaviour.DevLog("[SpawnCore] [WARNING] 标记大兴兴归属失败: " + presetName + ", " + trackEx.Message);
            }
        }

        private void InvokeSpawnCoreFailureCallback(Action onFailed, string reason)
        {
            if (onFailed == null)
            {
                return;
            }

            try
            {
                onFailed.Invoke();
            }
            catch (Exception callbackEx)
            {
                ModBehaviour.DevLog("[SpawnCore] [WARNING] 失败回调执行异常 (" + reason + "): " + callbackEx.Message);
            }
        }
    }
}
