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
    /// 通用敌人生成核心方法（Mode D / Mode E 共用）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private sealed class ModeEFSpawnProfiler
        {
            private readonly bool enabled;
            private readonly string scope;
            private readonly float startTime;
            private float lastCheckpointTime;
            private bool completed;

            public ModeEFSpawnProfiler(string scope, string detail = null)
            {
                enabled = DevModeEnabled && ModeEFSpawnProfilingEnabled;
                if (!enabled)
                {
                    return;
                }

                this.scope = string.IsNullOrEmpty(detail) ? scope : scope + " [" + detail + "]";
                startTime = Time.realtimeSinceStartup;
                lastCheckpointTime = startTime;
                DevLog("[ModeE/F] [Profile] " + this.scope + " begin");
            }

            public void Mark(string stageName)
            {
                if (!enabled || completed)
                {
                    return;
                }

                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE/F] [Profile] " + scope + " | " + stageName + ": +" +
                    ((now - lastCheckpointTime) * 1000f).ToString("F1") + " ms");
                lastCheckpointTime = now;
            }

            public void Complete(string status = "completed")
            {
                if (!enabled || completed)
                {
                    return;
                }

                completed = true;
                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE/F] [Profile] " + scope + " | " + status + " | total=" +
                    ((now - startTime) * 1000f).ToString("F1") + " ms");
            }
        }

        private sealed class ModeEFSpawnPostprocessJob
        {
            public EnemySpawnContext context;
            public EnemyPresetInfo actualPreset;
            public Func<bool> isActiveCheck;
            public SharedModeEnemyEquipmentMaterializationPlan equipmentPlan;
            public bool equipmentPlanCompleted;
            public bool bossMultiplierApplied;
            public bool applyBossMultiplier;
            public bool skipBossRushLootTracking;
            public Func<EnemySpawnContext, bool> onCommit;
            public UniTaskCompletionSource<EnemySpawnCoreResult> completionSource;
            public ModeEFSpawnProfiler profiler;
            public int queuedFrame;
            public int deadlineFrame;
        }

        private const int MODE_EF_SPAWN_POSTPROCESS_SOFT_DEADLINE_FRAMES = 60;
        private const int MODE_EF_SPAWN_POSTPROCESS_FINAL_SPRINT_FRAMES = 5;
        private const float MODE_EF_SPAWN_POSTPROCESS_FRAME_BUDGET_MS = 1000f / 60f;
        private const float MODE_EF_SPAWN_POSTPROCESS_SPRINT_FRAME_BUDGET_MS = 1000f / 30f;
        private const int MODE_EF_SPAWN_POSTPROCESS_BASE_JOB_STEPS = 1;
        private const int MODE_EF_SPAWN_POSTPROCESS_SPRINT_JOB_STEPS = 3;
        private const int MODE_EF_SPAWN_POSTPROCESS_MAX_STEPS_PER_TICK = 8;
        private const int MODE_EF_SPAWN_POSTPROCESS_SPRINT_MAX_STEPS_PER_TICK = 16;
        private readonly Queue<ModeEFSpawnPostprocessJob> modeEFSpawnPostprocessQueue
            = new Queue<ModeEFSpawnPostprocessJob>();

        /// <summary>
        /// 确保 cachedCharacterPresets 字典已经构建（审查 §1.1）。
        /// ZombieMode 入口路径调用此方法以避免依赖 Mode D 先初始化；
        /// 调用一次为 O(N) 扫描，缓存后再次调用直接返回。
        /// </summary>
        internal void EnsureCharacterPresetsCacheReady()
        {
            if (cachedCharacterPresets != null && cachedCharacterPresets.Count > 0)
            {
                return;
            }

            try
            {
                CharacterRandomPreset[] allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets == null || allPresets.Length == 0)
                {
                    return;
                }

                if (cachedCharacterPresets == null)
                {
                    cachedCharacterPresets = new System.Collections.Generic.Dictionary<string, CharacterRandomPreset>();
                }

                for (int i = 0; i < allPresets.Length; i++)
                {
                    CharacterRandomPreset preset = allPresets[i];
                    if (preset == null || string.IsNullOrEmpty(preset.nameKey)) continue;
                    if (IsRuntimeCharacterPresetClone(preset)) continue;
                    if (!cachedCharacterPresets.ContainsKey(preset.nameKey))
                    {
                        cachedCharacterPresets[preset.nameKey] = preset;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[SpawnCore] EnsureCharacterPresetsCacheReady 失败: " + e.Message);
            }
        }

        private void ClearModeEFSpawnPostprocessScheduler()
        {
            while (modeEFSpawnPostprocessQueue.Count > 0)
            {
                ModeEFSpawnPostprocessJob job = modeEFSpawnPostprocessQueue.Dequeue();
                CompleteModeEFSpawnPostprocessJobFailure(job, "scheduler_cleared", destroyCharacter: true);
            }
        }

        private void TickModeEFSpawnPostprocessScheduler()
        {
            if (modeEFSpawnPostprocessQueue.Count <= 0)
            {
                return;
            }

            int currentFrame = Time.frameCount;
            bool hasSprintPressure = HasModeEFSpawnPostprocessSprintPressure(currentFrame);
            float frameBudgetMs = hasSprintPressure
                ? MODE_EF_SPAWN_POSTPROCESS_SPRINT_FRAME_BUDGET_MS
                : MODE_EF_SPAWN_POSTPROCESS_FRAME_BUDGET_MS;
            int maxStepsThisTick = hasSprintPressure
                ? MODE_EF_SPAWN_POSTPROCESS_SPRINT_MAX_STEPS_PER_TICK
                : MODE_EF_SPAWN_POSTPROCESS_MAX_STEPS_PER_TICK;
            float frameStart = Time.realtimeSinceStartup;
            int steps = 0;
            while (modeEFSpawnPostprocessQueue.Count > 0 &&
                   steps < maxStepsThisTick)
            {
                float elapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                if (elapsedMs >= frameBudgetMs && steps > 0)
                {
                    break;
                }

                ModeEFSpawnPostprocessJob job = modeEFSpawnPostprocessQueue.Dequeue();
                bool completed = false;
                int jobStepBudget = GetModeEFSpawnPostprocessJobStepBudget(job, currentFrame);
                for (int jobStep = 0; jobStep < jobStepBudget; jobStep++)
                {
                    completed = ProcessModeEFSpawnPostprocessJobStep(job);
                    steps++;
                    if (completed || steps >= maxStepsThisTick)
                    {
                        break;
                    }

                    float innerElapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                    if (innerElapsedMs >= frameBudgetMs)
                    {
                        break;
                    }
                }

                if (!completed)
                {
                    modeEFSpawnPostprocessQueue.Enqueue(job);
                }
            }
        }

        private bool HasModeEFSpawnPostprocessSprintPressure(int currentFrame)
        {
            foreach (ModeEFSpawnPostprocessJob queuedJob in modeEFSpawnPostprocessQueue)
            {
                if (IsModeEFSpawnPostprocessJobInFinalSprint(queuedJob, currentFrame))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsModeEFSpawnPostprocessJobInFinalSprint(
            ModeEFSpawnPostprocessJob job,
            int currentFrame)
        {
            return job != null &&
                currentFrame >= (job.deadlineFrame - MODE_EF_SPAWN_POSTPROCESS_FINAL_SPRINT_FRAMES);
        }

        private static int GetModeEFSpawnPostprocessJobStepBudget(
            ModeEFSpawnPostprocessJob job,
            int currentFrame)
        {
            return IsModeEFSpawnPostprocessJobInFinalSprint(job, currentFrame)
                ? MODE_EF_SPAWN_POSTPROCESS_SPRINT_JOB_STEPS
                : MODE_EF_SPAWN_POSTPROCESS_BASE_JOB_STEPS;
        }

        private UniTask<EnemySpawnCoreResult> ScheduleModeEFSpawnPostprocessAsync(
            CharacterMainControl character,
            EnemyPresetInfo actualPreset,
            bool isBoss,
            Vector3 position,
            Func<bool> isActiveCheck,
            SharedModeEnemyEquipmentMaterializationPlan equipmentPlan,
            bool applyBossMultiplier,
            bool skipBossRushLootTracking,
            Func<EnemySpawnContext, bool> onCommit)
        {
            EnemySpawnContext ctx = new EnemySpawnContext
            {
                character = character,
                preset = actualPreset,
                isBoss = isBoss,
                position = position
            };

            string detail = actualPreset != null ? actualPreset.displayName : "unknown";
            ModeEFSpawnProfiler profiler = new ModeEFSpawnProfiler("ModeEFSpawnPostprocess", detail);
            profiler.Mark("Queued");
            int queuedFrame = Time.frameCount;

            UniTaskCompletionSource<EnemySpawnCoreResult> completionSource =
                new UniTaskCompletionSource<EnemySpawnCoreResult>();
            modeEFSpawnPostprocessQueue.Enqueue(new ModeEFSpawnPostprocessJob
            {
                context = ctx,
                actualPreset = actualPreset,
                isActiveCheck = isActiveCheck,
                equipmentPlan = equipmentPlan,
                equipmentPlanCompleted = equipmentPlan == null,
                bossMultiplierApplied = !applyBossMultiplier,
                applyBossMultiplier = applyBossMultiplier,
                skipBossRushLootTracking = skipBossRushLootTracking,
                onCommit = onCommit,
                completionSource = completionSource,
                profiler = profiler,
                queuedFrame = queuedFrame,
                deadlineFrame = queuedFrame + MODE_EF_SPAWN_POSTPROCESS_SOFT_DEADLINE_FRAMES,
            });
            return completionSource.Task;
        }

        private bool ProcessModeEFSpawnPostprocessJobStep(ModeEFSpawnPostprocessJob job)
        {
            try
            {
                if (job == null || job.context == null)
                {
                    return true;
                }

                CharacterMainControl character = job.context.character;
                if (character == null || character.gameObject == null)
                {
                    CompleteModeEFSpawnPostprocessJobFailure(job, "character_missing", destroyCharacter: false);
                    return true;
                }

                if (job.isActiveCheck != null && !job.isActiveCheck())
                {
                    CompleteModeEFSpawnPostprocessJobFailure(job, "mode_ended", destroyCharacter: true);
                    return true;
                }

                if (!job.equipmentPlanCompleted)
                {
                    job.equipmentPlanCompleted = MaterializeNextSharedModeEnemyEquipmentPlanStep(character, job.equipmentPlan);
                    if (job.equipmentPlanCompleted)
                    {
                        job.profiler.Mark("EquipmentReady");
                    }

                    return false;
                }

                if (!job.bossMultiplierApplied)
                {
                    ApplyBossStatMultiplier(character);
                    job.bossMultiplierApplied = true;
                    job.profiler.Mark("BossMultiplier");
                    return false;
                }

                if (!FinalizeModeEFSpawnPostprocessJob(job))
                {
                    CompleteModeEFSpawnPostprocessJobFailure(job, "commit_failed", destroyCharacter: true);
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[SpawnCore] [ERROR] ProcessModeEFSpawnPostprocessJobStep 失败: " + e.Message);
                CompleteModeEFSpawnPostprocessJobFailure(job, "postprocess_exception", destroyCharacter: true);
                return true;
            }
        }

        private bool FinalizeModeEFSpawnPostprocessJob(ModeEFSpawnPostprocessJob job)
        {
            CharacterMainControl character = job.context.character;
            if (character == null || character.gameObject == null)
            {
                return false;
            }

            character.gameObject.SetActive(true);
            MutatorManager.ApplyToEnemy(character);

            if (job.context.isBoss && !job.skipBossRushLootTracking)
            {
                try
                {
                    int originalLootCount = 0;
                    if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                    {
                        originalLootCount = 3;
                    }

                    RegisterBossRandomLootTracking(character, originalLootCount);
                    DevLog("[SpawnCore] 已注册 Boss 掉落追踪: " + job.context.preset.displayName
                        + " (原始掉落数量=" + originalLootCount + ")");
                }
                catch (Exception lootTrackEx)
                {
                    DevLog("[SpawnCore] [WARNING] 注册 Boss 掉落追踪失败: " + lootTrackEx.Message);
                }
            }

            if (!InvokeSpawnCoreCommitCallback(job.onCommit, job.context))
            {
                return false;
            }

            job.profiler.Mark("Commit");
            job.profiler.Complete("success");
            job.completionSource.TrySetResult(EnemySpawnCoreResult.Succeeded(job.context, job.actualPreset));
            return true;
        }

        private void CompleteModeEFSpawnPostprocessJobFailure(
            ModeEFSpawnPostprocessJob job,
            string reason,
            bool destroyCharacter)
        {
            if (job == null)
            {
                return;
            }

            CharacterMainControl character = null;
            try { character = job.context != null ? job.context.character : null; } catch {}

            try
            {
                if (job.equipmentPlan != null)
                {
                    CleanupSharedModeEnemyEquipmentMaterializationPlan(job.equipmentPlan);
                }
            }
            catch (Exception cleanupPlanEx)
            {
                DevLog("[SpawnCore] [WARNING] 清理延后配装计划失败: " + cleanupPlanEx.Message);
            }

            if (destroyCharacter && character != null)
            {
                try
                {
                    if (character.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(character.gameObject);
                    }
                }
                catch (Exception destroyEx)
                {
                    DevLog("[SpawnCore] [WARNING] 销毁后处理失败角色异常: " + destroyEx.Message);
                }
            }

            job.profiler.Complete(reason);
            job.completionSource.TrySetResult(EnemySpawnCoreResult.Failed(reason, job.actualPreset));
        }

        private bool InvokeSpawnCoreCommitCallback(Func<EnemySpawnContext, bool> onCommit, EnemySpawnContext context)
        {
            if (onCommit == null)
            {
                return true;
            }

            try
            {
                return onCommit.Invoke(context);
            }
            catch (Exception commitEx)
            {
                DevLog("[SpawnCore] [WARNING] 提交回调执行异常: " + commitEx.Message);
                return false;
            }
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
        private void SpawnEnemyCore(
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
                    DevLog("[SpawnCore] [WARNING] 生成成功回调执行异常: " + callbackEx.Message);
                    InvokeSpawnCoreFailureCallback(onFailed, "成功回调异常");
                }
                return;
            }

            string reason = result != null ? result.failureReason : "结果为空";
            InvokeSpawnCoreFailureCallback(onFailed, reason);
        }

        private async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(
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
            Func<EnemySpawnContext, bool> onCommit = null)
        {
            try
            {
                const int maxAttempts = 5;
                EnemyPresetInfo currentPreset = preset;

                // 记录调用方传入的原始预设，用于区分"首次使用"和"重试随机"
                EnemyPresetInfo originalPreset = preset;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        // 预设为空时重新随机
                        if (currentPreset == null)
                        {
                            currentPreset = isBoss ? GetRandomBossPreset() : GetRandomMinionPreset();
                        }
                        if (currentPreset == null)
                        {
                            DevLog("[SpawnCore] 预设为空, attempt=" + attempt);
                            continue;
                        }

                        // Mode E 龙限制：仅在"重试随机"时检查（currentPreset != originalPreset）
                        // 首次循环使用的是调用方已确认的预设，不应被跳过
                        // 重试时需要同时检查 skip 参数和实例字段（防止并发竞态）
                        bool isRetry = (currentPreset != originalPreset);
                        if (isRetry && (skipDragonDescendant || modeEDragonDescendantSpawned) && IsDragonDescendantPreset(currentPreset))
                        {
                            DevLog("[SpawnCore] 重试跳过龙裔遗族（已达上限）");
                            currentPreset = null;
                            continue;
                        }
                        if (isRetry && (skipDragonKing || modeEDragonKingSpawned) && IsDragonKingPreset(currentPreset))
                        {
                            DevLog("[SpawnCore] 重试跳过龙王（已达上限）");
                            currentPreset = null;
                            continue;
                        }
                        if (isRetry && IsPhantomWitchPreset(currentPreset))
                        {
                            DevLog("[SpawnCore] 重试跳过幽灵女巫（同一波次不重复）");
                            currentPreset = null;
                            continue;
                        }

                        DevLog("[SpawnCore] 生成敌人: " + currentPreset.displayName + " (isBoss=" + isBoss + ", attempt=" + attempt + ")");

                        int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                        CharacterMainControl character = null;

                        // 龙裔遗族Boss：使用专用生成方法
                        if (IsDragonDescendantPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnDragonDescendant(
                                    position,
                                    isChildProtectionSummon: false,
                                    notifyBossRushOnFailure: false,
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame);
                            }
                            catch (Exception dragonEx)
                            {
                                DevLog("[SpawnCore] 龙裔遗族生成异常: " + dragonEx.Message);
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
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame);
                            }
                            catch (Exception kingEx)
                            {
                                DevLog("[SpawnCore] 龙王生成异常: " + kingEx.Message);
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
                                    deferActivationUntilNextFrame: deferActivationUntilNextFrame);
                            }
                            catch (Exception witchEx)
                            {
                                DevLog("[SpawnCore] 幽灵女巫生成异常: " + witchEx.Message);
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
                                DevLog("[SpawnCore] 未找到预设: " + currentPreset.name);
                                currentPreset = null;
                                continue;
                            }

                            try
                            {
                                character = await targetPreset.CreateCharacterAsync(position, Vector3.forward, relatedScene, null, false);
                            }
                            catch (Exception createEx)
                            {
                                DevLog("[SpawnCore] 生成敌人异常: " + createEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }

                        if (character == null)
                        {
                            DevLog("[SpawnCore] 生成敌人失败: " + (currentPreset != null ? currentPreset.displayName : "null"));
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
                                        UnityEngine.Object.Destroy(character.gameObject);
                                    }
                                }
                                catch (Exception destroyEx)
                                {
                                    DevLog("[SpawnCore] [WARNING] 模式结束时销毁特殊生成敌人失败: " + destroyEx.Message);
                                }

                                DevLog("[SpawnCore] 模式已结束，销毁特殊生成的敌人");
                                return EnemySpawnCoreResult.Failed("模式结束", currentPreset);
                            }

                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position
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
                            MutatorManager.ApplyToEnemy(character);

                            if (!InvokeSpawnCoreCommitCallback(onCommit, ctx))
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
                                    DevLog("[SpawnCore] [WARNING] 特殊Boss提交失败后销毁异常: " + destroyOnCommitEx.Message);
                                }

                                return EnemySpawnCoreResult.Failed("提交回调失败", currentPreset);
                            }

                            DevLog("[SpawnCore] 敌人生成成功: " + currentPreset.displayName);
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
                                DevLog("[SpawnCore] 模式已结束，销毁生成的敌人");
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

                                return await ScheduleModeEFSpawnPostprocessAsync(
                                    character,
                                    currentPreset,
                                    isBoss,
                                    position,
                                    isActiveCheck,
                                    equipmentPlan,
                                    applyBossMultiplier,
                                    skipBossRushLootTracking,
                                    onCommit);
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

                            // 激活敌人
                            character.gameObject.SetActive(true);

                            // 应用变异词条效果到新生成的敌人
                            MutatorManager.ApplyToEnemy(character);

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
                                    DevLog("[SpawnCore] 已注册 Boss 掉落追踪: " + currentPreset.displayName
                                        + " (原始掉落数量=" + originalLootCount + ")");
                                }
                                catch (Exception lootTrackEx)
                                {
                                    DevLog("[SpawnCore] [WARNING] 注册 Boss 掉落追踪失败: " + lootTrackEx.Message);
                                }
                            }

                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position
                            };

                            if (!InvokeSpawnCoreCommitCallback(onCommit, ctx))
                            {
                                UnityEngine.Object.Destroy(character.gameObject);
                                DevLog("[SpawnCore] 提交回调失败，销毁普通Boss: " + currentPreset.displayName);
                                return EnemySpawnCoreResult.Failed("提交回调失败", currentPreset);
                            }

                            DevLog("[SpawnCore] 敌人生成成功: " + currentPreset.displayName);
                            return EnemySpawnCoreResult.Succeeded(ctx, currentPreset);
                        }
                    }
                    catch (Exception e)
                    {
                        DevLog("[SpawnCore] [ERROR] 尝试失败: " + e.Message);
                        currentPreset = null;
                    }
                }

                DevLog("[SpawnCore] [ERROR] 多次尝试仍然失败");

                // 所有重试均失败，调用失败回调（用于 Mode D 波次计数兜底等）
                return EnemySpawnCoreResult.Failed("重试耗尽", currentPreset);
            }
            catch (Exception e)
            {
                DevLog("[SpawnCore] [ERROR] SpawnEnemyCore 异常: " + e.Message);

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
                DevLog("[SpawnCore] [WARNING] 标记大兴兴归属失败: " + presetName + ", " + trackEx.Message);
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
                DevLog("[SpawnCore] [WARNING] 失败回调执行异常 (" + reason + "): " + callbackEx.Message);
            }
        }
    }
}
