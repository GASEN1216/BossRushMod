using System;
using ItemStatsSystem;

namespace BossRush
{
    internal static class ModeGDeathRouting
    {
        internal static bool IsWaveVictoryReady(ModeGRunState state)
        {
            return state != null && state.IsActive && state.IsFinalWave &&
                state.AreAllSlotsResolved && state.TrackedBossCount == 0;
        }

        internal static bool ShouldTriggerLastStand(
            ModeGRunState state,
            int aliveBossCount,
            int committedCount)
        {
            if (state == null || !state.IsActive || state.lastStandActive) return false;
            return aliveBossCount == 1 && committedCount > 1;
        }

        internal static int GetMinimumStartCount(int bossCount)
        {
            return bossCount > 1 ? 2 : 1;
        }

        internal static void HandlePlayerDeath(
            ModeGRuntimeModule module,
            Health health,
            DamageInfo info)
        {
            if (module == null || module.State == null || health == null) return;

            ModeGRunState state = module.State;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || !ReferenceEquals(health, player.Health)) return;

            try
            {
                if (state.IsRewarding)
                {
                    state.rewardNonceInvalidated = true;
                    ModeGRewardTransaction.InvalidateAttemptNonce();
                    ModBehaviour host = module.Host;
                    if (host != null) host.CancelModeGRewardMaterialization_LootAndRewards();
                    module.End(ModeGExitReason.RewardInterruptedByDeath);
                    return;
                }

                if (!state.IsStarting && !state.IsActive) return;
                if (!state.TryLockBattleResult(ModeGBattleResult.Defeat)) return;

                state.combatPhase = ModeGCombatPhase.Defeat;
                state.lastStandActive = false;
                ModeGRecapPanel.NemesisAttribution attribution = AttributeDefeatToNemesis(module, state, player, info);

                try
                {
                    ModeGProfilePersistence.RecordRun(
                        ModeGBattleResult.Defeat,
                        state.waveEpoch + 1,
                        module.RunElapsedSeconds,
                        module.TotalBossKills,
                        false,
                        "defeat_" + state.runId.ToString("x"));
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] defeat profile write failed: " + e.Message);
                }

                if (module.Host != null)
                {
                    // 失败横幅（§15）：败北 · 第 X/9 波 · Resolve Y/11 · 下局宿敌预告
                    string banner = ModeGRecapPanel.ComposeDefeatBanner(
                        state.waveEpoch + 1,
                        module.Adaptive != null ? module.Adaptive.TotalResolve : 0,
                        attribution);
                    if (!string.IsNullOrEmpty(banner)) module.Host.ShowBigBanner(banner);
                }

                // 失败 recap（near-miss 呈现；自动倒计时关闭，不阻塞官方死亡流程）
                try
                {
                    ModeGRecapPanel.Show(module, ModeGBattleResult.Defeat,
                        ModeGRecapPanel.ComposeNemesisPreviewLine(attribution));
                }
                catch { /* 呈现失败不阻塞结算 */ }

                module.End(ModeGExitReason.PlayerDeath);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] player death route failed: " + e.Message);
                try { module.End(ModeGExitReason.TechnicalIntegrityLoss); } catch { }
            }
        }

        /// <summary>
        /// 失败宿敌归因：击杀者为已登记 Boss 时写入/升级宿敌记录。
        /// 返回归因结果供失败横幅/recap 预告（Rank 读取内存提交后的持久值，§15）。
        /// </summary>
        private static ModeGRecapPanel.NemesisAttribution AttributeDefeatToNemesis(
            ModeGRuntimeModule module,
            ModeGRunState state,
            CharacterMainControl player,
            DamageInfo info)
        {
            ModeGRecapPanel.NemesisAttribution outcome = new ModeGRecapPanel.NemesisAttribution();
            try
            {
                CharacterMainControl attacker = info.fromCharacter;
                string killerKey;
                if (attacker == null || attacker == player ||
                    !module.TryGetRegisteredBossPresetKey(attacker, out killerKey) ||
                    string.IsNullOrEmpty(killerKey))
                {
                    return outcome; // 未形成新宿敌
                }

                if (ModeGNemesisPersistence.IsStoreFaulted)
                {
                    // 宿敌写屏障生效：击杀者已知，记录未变更（不得声称已写入）
                    outcome.storeBlocked = true;
                    outcome.bossKey = killerKey;
                    return outcome;
                }

                ModeGNemesisPersistence.NemesisRecordDto current = ModeGNemesisPersistence.LoadOrInit();
                if (current == null) return outcome;

                bool sameBoss = string.Equals(current.bossPresetKey, killerKey, StringComparison.Ordinal);
                ModeGNemesisPersistence.NemesisRecordDto copy = new ModeGNemesisPersistence.NemesisRecordDto
                {
                    schemaVersion = current.schemaVersion,
                    bossPresetKey = killerKey,
                    rank = sameBoss
                        ? ModeGNemesisPersistence.ClampRankUp(current.rank, current.rank)
                        : 1,
                    temperamentId = current.temperamentId,
                    defeatsByPlayer = current.defeatsByPlayer,
                    defeatsOfPlayer = current.defeatsOfPlayer + 1,
                    lastUpdatedTicks = current.lastUpdatedTicks,
                    originRunId = state.runId,
                    tombstone = false
                };
                if (ModeGNemesisPersistence.Store(copy))
                {
                    outcome.written = true;
                    outcome.bossKey = killerKey;
                    outcome.rank = copy.rank;
                }
                else
                {
                    // Store 拒绝（新进入 StoreFaulted）：按版本保护路径呈现
                    outcome.storeBlocked = true;
                    outcome.bossKey = killerKey;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] nemesis attribution failed: " + e.Message);
            }
            return outcome;
        }

        internal static void HandleVictory(ModeGRuntimeModule module)
        {
            if (module == null || !IsWaveVictoryReady(module.State)) return;

            ModeGRunState state = module.State;
            try
            {
                if (!state.TryLockBattleResult(ModeGBattleResult.Victory)) return;
                state.combatPhase = ModeGCombatPhase.Victory;
                state.lastStandActive = false;
                state.intermissionActive = false;

                try
                {
                    ModeGProfilePersistence.RecordRun(
                        ModeGBattleResult.Victory,
                        ModeGWavePlan.WaveCount,
                        module.RunElapsedSeconds,
                        module.TotalBossKills,
                        module.IsNemesisDefeatedThisRun,
                        "victory_" + state.runId.ToString("x"));
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] victory profile write failed: " + e.Message);
                }

                if (state.fateContractId >= 0 &&
                    ModeGFateContract.Evaluate(state.fateContractId, module.BuildContractProgress()))
                {
                    ModeGProfilePersistence.IncrementContractStreak();
                }

                if (!state.TryAdvanceLifecycle(ModeGLifecyclePhase.Rewarding))
                {
                    module.End(ModeGExitReason.TechnicalIntegrityLoss);
                    return;
                }

                if (module.Telemetry != null) module.Telemetry.UnsubscribeCombat();
                SubmitVictoryReward(module, state);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] victory route failed: " + e.Message);
                try { module.End(ModeGExitReason.TechnicalIntegrityLoss); } catch { }
            }
        }

        private static void SubmitVictoryReward(ModeGRuntimeModule module, ModeGRunState state)
        {
            ModBehaviour host = module.Host;
            CharacterMainControl player = CharacterMainControl.Main;
            Inventory inventory = player != null && player.CharacterItem != null
                ? player.CharacterItem.Inventory
                : null;
            if (host == null || inventory == null)
            {
                module.End(ModeGExitReason.RewardAbandoned);
                return;
            }

            int resolve = module.Adaptive != null ? module.Adaptive.TotalResolve : 0;
            var plan = ModeGRewardTransaction.BuildSlotPlan(
                state.runSeed,
                resolve,
                host.GetModeGRewardCandidates());
            if (plan == null || plan.Count == 0 ||
                !ModeGRewardTransaction.Execute(state, host, inventory, plan))
            {
                module.End(ModeGExitReason.RewardAbandoned);
                return;
            }

            host.ShowBigBanner(L10n.T(
                "<color=#B8860B>宿命已改写</color> 九波胜利",
                "<color=#B8860B>Fate Rewritten</color> Nine Waves Cleared"));

            // 胜利 recap（near-miss 呈现；profile 已含本局记账，自动倒计时关闭）
            try
            {
                ModeGRecapPanel.Show(module, ModeGBattleResult.Victory, string.Empty);
            }
            catch { /* 呈现失败不阻塞结算 */ }

            module.End(ModeGExitReason.Victory);
        }
    }
}
