using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        // 准入只读显式宿主；保留 Awake 前调用也经过原风险门和异常边界的语义。
        internal static bool TryStartModeD(ModBehaviour owner)
        {
            if (BossRushCoop.IsConnected && !BossRushCoop.SessionReady) return false;
            if (BossRushCoop.IsConnected && current != null && current.owner == owner && current.modeDActive)
                return current.RetryCoopModeDEntry();
            // Mode H 真实资产风险门（加法分支，设计提案 §24.3）：
            // 只在存在未终结真实资产事务或风险未知时拒绝；no-throw，
            // 新档/无 journal 时同步 ready 且不阻断，旧模式行为逐字不变。
            try
            {
                if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
                {
                    // 被拒的成因有两种：扫描本身失败（可自愈）与确有未结算押品。
                    // 先给一次重试机会，再按真实成因取文案，别把读档出错说成「你有笔账没结」。
                    owner.ShowMessage(L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey()));
                    ModBehaviour.DevLog("[BossRush] 入口被 Mode H 真实资产风险门拒绝");
                    return false;
                }
            }
            catch
            {
                // 门查询本身 no-throw；异常只表示未能判定，放行旧模式既有流程
            }

            try
            {
                // 互斥保护：Mode E 已激活时不启动 Mode D
                if (owner.IsModeEActive)
                {
                    ModBehaviour.DevLog("[ModeD] Mode E 已激活，跳过 Mode D 启动");
                    return false;
                }

                // 检查是否满足 Mode D 条件
                if (!owner.IsPlayerNaked())
                {
                    ModBehaviour.DevLog("[ModeD] 玩家不满足裸体条件，不启动 Mode D");
                    return false;
                }

                ModBehaviour.DevLog("[ModeD] 检测到裸体入场，启动 Mode D");
                owner.StartModeD();
                // 启动内部吞异常；成功与否以本实例这一次启动的结论为准，不能再恒报 true。
                ModeDRuntimeModule runtime = current;
                return runtime != null && runtime.owner == owner && runtime.lastStartSucceeded;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] TryStartModeD 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>最近一次 StartModeD 是否完成了必需的准备（开局武器已发）。</summary>
        internal bool lastStartSucceeded;

        internal void StartModeD()
        {
            if (BossRushCoop.IsConnected && modeDActive) { lastStartSucceeded = RetryCoopModeDEntry(); return; }
            ModeDRuntimeModule.Invalidate(owner);
            if (coopChannel != null && coopChannel.Request("start")) return;
            if (!BossRushCoop.SessionReady) return;
            coopEpoch = System.Guid.NewGuid().ToString("N");
            lastStartSucceeded = false;
            try
            {
                ModBehaviour.DevLog("[ModeD] 启动 Mode D 模式");

                // 清理可能从无间炼狱残留的状态：ConfigureBossRushMode 是 infiniteHellMode
                // 的唯一赋值点，玩家若先玩无间炼狱后直接进白手起家，残留的 true 会让
                // OnBossBeforeSpawnLoot_LootAndRewards 禁用 Boss 掉落箱并在路牌旁掉钞票。
                owner.ResetArenaForModeD();

                modeDCleanupPending = true;
                modeDActive = true;
                modeDWaveIndex = 0;
                modeDWaveCompletePending = false;
                modeDCurrentWaveEnemies.Clear();
                owner.ClearModeDEnemyRecoveryState();

                // 读取配置
                if (owner.ModeDConfiguredEnemiesPerWave > 0)
                {
                    modeDEnemiesPerWave = Mathf.Clamp(owner.ModeDConfiguredEnemiesPerWave, 1, 10);
                }
                ModBehaviour.DevLog("[ModeD] 每波敌人数: " + modeDEnemiesPerWave);

                // 初始化物品池
                ItemPool.InitializeModeDItemPools(ItemPool.FindTagByName);

                // 初始化敌人池
                owner.InitializeModeDEnemyPoolsForRuntime();

                // 前置构建全局掉落池（避免战斗中首次调用时卡顿）
                ItemPool.EnsureModeDGlobalItemPool();

                // 给玩家发放开局装备；连武器都没发出去时本局无法进行，按启动失败回滚。
                if (!(BossRushCoop.IsConnected ? TryCommitCoopModeDStarter() : ItemPool.GivePlayerStarterKit()))
                    throw new InvalidOperationException("starter_kit_not_delivered");
                coopDeliveredEpoch = coopEpoch + ":" + BossRushCoop.LocalAssetId;
                RefreshCoopModeDParticipants();

                // 抽取并应用本局变异词条（首波敌人由路牌触发，此处先于任何刷怪）
                owner.TryRollMutatorsForArena("ModeD");

                // 设置路牌为 Mode D 模式
                SetupSignForModeD(owner.ArenaRewardSignInteract);

                // 初始状态保持为生小鸡（EntryAndDifficulty），波次开始时再切换到加油状态

                lastStartSucceeded = true;
                owner.ShowMessage(L10n.T("白手起家模式已激活！通过路牌开始挑战！", "From Scratch mode activated! Start the challenge via the signpost!"));
                owner.ShowBigBanner(L10n.T("欢迎来到 <color=red>白手起家</color>！", "Welcome to <color=red>From Scratch</color>!"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] StartModeD 失败: " + e.Message);
                if (!lastStartSucceeded)
                {
                    // 半激活比没激活更糟：撤掉本局运行态（波次、敌人、词条），留在普通挑战并说明原因。
                    CleanupModeDRuntimeOnDestroy();
                    Debug.LogWarning("[ModeD] 白手起家启动失败，已回滚: " + e.Message);
                    try
                    {
                        owner.ShowMessage(L10n.T("白手起家没能开起来，这局按普通挑战进行；回基地后可以再试",
                            "From Scratch failed to start, so this run plays as a normal challenge. You can retry from the base"));
                    }
                    catch { /* 提示失败不影响回滚 */ }
                }
            }
        }

        internal void EndModeD()
        {
            ModeDRuntimeModule.Invalidate(owner);
            try
            {
                // 即使状态已提前关闭，也要幂等清理仍登记的实体。
                if (!modeDActive)
                {
                    CleanupModeDWaveEnemiesOnExit();
                    return;
                }

                // 先保存完成波次数，再清零
                int completedWaves = modeDWaveIndex;

                ModBehaviour.DevLog("[ModeD] 结束 Mode D 模式，完成波次: " + completedWaves);

                modeDActive = false;
                modeDWaveIndex = 0;
                CleanupModeDWaveEnemiesOnExit();
                owner.ClearModeDEnemyRecoveryState();

                // 清理变异词条（覆盖场景退出 / 玩家死亡 / 手动退出，幂等）
                owner.ClearModeDMutators("ModeD");
                modeDCleanupPending = false;

                // 使用保存的波次数显示消息
                owner.ShowMessage(L10n.T(
                    "白手起家挑战结束！共完成 " + completedWaves + " 波",
                    "From Scratch challenge ended! Completed " + completedWaves + " waves"
                ));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] EndModeD 失败: " + e.Message);
            }
        }

        private void CleanupModeDRuntimeOnDestroy()
        {
            bool cleanupHostState = modeDActive || modeDCleanupPending || modeDCurrentWaveEnemies.Count > 0;
            // 卸载只撤销本局运行态，不走结束提示、奖励或下一波调度。
            modeDActive = false;
            modeDWaveIndex = 0;
            modeDWaveCompletePending = false;
            modeDExpectedEnemiesInCurrentWave = 0;
            modeDSpawnResolvedInCurrentWave = 0;
            if (modeDAutoNextWaveCoroutine != null)
            {
                try { if (owner != null) owner.StopCoroutine(modeDAutoNextWaveCoroutine); }
                catch (Exception e) { ModBehaviour.DevLog("[ModeD] [WARNING] 销毁时停止自动下一波协程失败: " + e.Message); }
                modeDAutoNextWaveCoroutine = null;
            }
            CleanupModeDWaveEnemiesOnExit();
            if (cleanupHostState && !object.ReferenceEquals(owner, null))
            {
                try { owner.ClearModeDEnemyRecoveryState(); }
                catch (Exception e) { ModBehaviour.DevLog("[ModeD] [WARNING] 销毁时清理敌人恢复状态失败: " + e.Message); }
                try { owner.ClearModeDMutators("ModeD"); }
                catch (Exception e) { ModBehaviour.DevLog("[ModeD] [WARNING] 销毁时清理词条失败: " + e.Message); }
            }
            modeDCleanupPending = false;
        }

        /// <summary>
        /// 退出 Mode D 时确定性销毁本波角色。只清列表会遗留仍在场景中的 AI，
        /// 尤其中立预设不会被标准敌对清场路径识别。
        /// </summary>
        private void CleanupModeDWaveEnemiesOnExit()
        {
            for (int i = modeDCurrentWaveEnemies.Count - 1; i >= 0; i--)
            {
                CharacterMainControl enemy = modeDCurrentWaveEnemies[i];
                if (enemy == null) continue;
                if (!BossRushCoop.HasWorldAuthority && !coopProjectionRetiring) continue;
                try
                {
                    if (!object.ReferenceEquals(owner, null)) owner.UnregisterEnemyRecoveryForArena(enemy);
                    enemy.dropBoxOnDead = false;
                    if (enemy.gameObject != null)
                    {
                        enemy.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(enemy.gameObject);
                    }
                }
                catch (Exception cleanupException)
                {
                    ModBehaviour.DevLog("[ModeD] [WARNING] 退出时销毁本波敌人失败: index=" + i
                        + ", " + cleanupException.Message);
                }
            }
            modeDCurrentWaveEnemies.Clear();
        }

    }
}
