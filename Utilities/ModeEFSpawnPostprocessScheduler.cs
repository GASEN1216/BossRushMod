// 共用生成后处理队列。保留逐帧预算、提交门与清理顺序，由宿主在原阶段驱动。
using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;

namespace BossRush
{
    internal sealed class ModeEFSpawnPostprocessScheduler
    {
        private Func<CharacterMainControl, SharedModeEnemyEquipmentMaterializationPlan, bool> materializeEquipmentStep;
        private Action<CharacterMainControl> applyBossStatMultiplier;
        private Action<CharacterMainControl, int> registerBossLoot;
        private Action<SharedModeEnemyEquipmentMaterializationPlan> cleanupEquipmentPlan;
        private Action<CharacterMainControl> clearBossLoot;

        internal void BindServices(
            Func<CharacterMainControl, SharedModeEnemyEquipmentMaterializationPlan, bool> materializeEquipmentStep,
            Action<CharacterMainControl> applyBossStatMultiplier,
            Action<CharacterMainControl, int> registerBossLoot,
            Action<SharedModeEnemyEquipmentMaterializationPlan> cleanupEquipmentPlan,
            Action<CharacterMainControl> clearBossLoot)
        {
            this.materializeEquipmentStep = materializeEquipmentStep;
            this.applyBossStatMultiplier = applyBossStatMultiplier;
            this.registerBossLoot = registerBossLoot;
            this.cleanupEquipmentPlan = cleanupEquipmentPlan;
            this.clearBossLoot = clearBossLoot;
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
            public EnemySpawnCoreOptions options;
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

        internal void ClearModeEFSpawnPostprocessScheduler()
        {
            while (modeEFSpawnPostprocessQueue.Count > 0)
            {
                ModeEFSpawnPostprocessJob job = modeEFSpawnPostprocessQueue.Dequeue();
                CompleteModeEFSpawnPostprocessJobFailure(job, "scheduler_cleared", destroyCharacter: true);
            }
        }

        internal void TickModeEFSpawnPostprocessScheduler()
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

        // options 必须由调用方显式传入（无默认值），防止未来新增 defer 调用点时
        // 静默丢失 HoldForExternalCommit 等门控语义；options == null 逐字保持 Legacy 行为。
        internal UniTask<EnemySpawnCoreResult> ScheduleModeEFSpawnPostprocessAsync(
            CharacterMainControl character,
            EnemyPresetInfo actualPreset,
            bool isBoss,
            Vector3 position,
            Func<bool> isActiveCheck,
            SharedModeEnemyEquipmentMaterializationPlan equipmentPlan,
            bool applyBossMultiplier,
            bool skipBossRushLootTracking,
            Func<EnemySpawnContext, bool> onCommit,
            EnemySpawnCoreOptions options)
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
                options = options,
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
                    job.equipmentPlanCompleted = materializeEquipmentStep(character, job.equipmentPlan);
                    if (job.equipmentPlanCompleted)
                    {
                        job.profiler.Mark("EquipmentReady");
                    }

                    return false;
                }

                if (!job.bossMultiplierApplied)
                {
                    applyBossStatMultiplier(character);
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
                ModBehaviour.DevLog("[SpawnCore] [ERROR] ProcessModeEFSpawnPostprocessJobStep 失败: " + e.Message);
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

            // Mode G 门控（加法分支）：与同步路径（HoldForExternalCommit 冻结分支）语义一致，
            // 外部提交路径先冻结，全部槽位结案后由 run owner 批量激活；
            // job.options == null 逐字保持原行为
            if (job.options != null && job.options.HoldForExternalCommit)
            {
                if (character.Health != null) character.Health.SetInvincible(true);
                character.gameObject.SetActive(false);
            }
            else
            {
                character.gameObject.SetActive(true);
                // Mod 刷怪点可能远离玩家；官方距离休眠会每帧把它关掉并卡住波次结算。
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
            }

            // Mode G 门控（加法分支）：job.options == null 逐字保持原行为
            if (job.options == null || job.options.ApplySharedMutators)
            {
                MutatorManager.ApplyToEnemy(character);
            }

            if (job.context.isBoss && !job.skipBossRushLootTracking)
            {
                try
                {
                    int originalLootCount = 0;
                    if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                    {
                        originalLootCount = 3;
                    }

                    registerBossLoot(character, originalLootCount);
                    ModBehaviour.DevLog("[SpawnCore] 已注册 Boss 掉落追踪: " + job.context.preset.displayName
                        + " (原始掉落数量=" + originalLootCount + ")");
                }
                catch (Exception lootTrackEx)
                {
                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 注册 Boss 掉落追踪失败: " + lootTrackEx.Message);
                }
            }

            // Mode G 门控（加法分支）：HoldForExternalCommit 时跳过 Legacy 提交回调，
            // 由 Mode G 按 handle 自行提交；job.options == null 逐字保持原行为
            if (job.options == null || !job.options.HoldForExternalCommit)
            {
                if (!InvokeSpawnCoreCommitCallback(job.onCommit, job.context))
                {
                    return false;
                }
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
                    cleanupEquipmentPlan(job.equipmentPlan);
                }
            }
            catch (Exception cleanupPlanEx)
            {
                ModBehaviour.DevLog("[SpawnCore] [WARNING] 清理延后配装计划失败: " + cleanupPlanEx.Message);
            }

            if (destroyCharacter && character != null)
            {
                // 先解绑再销毁：掉落追踪表（含遗种巢与词缀熔石的 per-boss handler）
                // 以角色为键，销毁后不解绑会把已死引用留到下一次场景清理，
                // 期间仍会被各种遍历扫到。
                try
                {
                    clearBossLoot(character);
                }
                catch (Exception untrackEx)
                {
                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 解绑后处理失败角色的掉落追踪异常: "
                        + untrackEx.Message);
                }

                try
                {
                    if (character.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(character.gameObject);
                    }
                }
                catch (Exception destroyEx)
                {
                    ModBehaviour.DevLog("[SpawnCore] [WARNING] 销毁后处理失败角色异常: " + destroyEx.Message);
                }
            }

            job.profiler.Complete(reason);
            job.completionSource.TrySetResult(EnemySpawnCoreResult.Failed(reason, job.actualPreset));
        }

        internal static bool InvokeSpawnCoreCommitCallback(Func<EnemySpawnContext, bool> onCommit, EnemySpawnContext context)
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
                ModBehaviour.DevLog("[SpawnCore] [WARNING] 提交回调执行异常: " + commitEx.Message);
                return false;
            }
        }

    }
}
