using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode G run-scoped 生成事务（规格 §13.1/§13.4 重写版）。
    ///
    /// 硬约束：
    /// - 每槽恰好结案一次（ResolveSlotOnce 唯一写入口），至多尝试 2 次（MaxAttemptsPerSlot）；
    /// - TryCommit 先 RegisterTrackedBoss 再 ResolveSlotOnce，失败回滚登记；
    /// - committed Character -> presetKey 映射（宿敌归因/遥测用，exact 引用身份）；
    /// - §13.1 Mode G 固定 options：AllowRandomRetryFallback=false、ApplySharedMutators=false；
    ///   official 路径 HoldForExternalCommit=false（Legacy onCommit 提交），
    ///   managed 路径 HoldForExternalCommit=true（托管 handle 自行提交）；
    /// - spawnLeasesInvalidated 后全部 lease fail-closed。
    /// </summary>
    public sealed class ModeGSpawnTransaction
    {
        #region Lease / Journal DTO

        public struct SpawnAttemptLease
        {
            public readonly int slotIndex;
            public readonly int attemptNumber;
            public readonly string presetKey;
            public readonly ModeGSlotOutcome outcome;

            public SpawnAttemptLease(int slotIndex, int attemptNumber, string presetKey, ModeGSlotOutcome outcome)
            {
                this.slotIndex = slotIndex;
                this.attemptNumber = attemptNumber;
                this.presetKey = presetKey;
                this.outcome = outcome;
            }
        }

        public struct CommitJournalEntry
        {
            public readonly int slotIndex;
            public readonly int instanceId;
            public readonly string presetKey;
            public readonly long commitTicks;

            public CommitJournalEntry(int slotIndex, int instanceId, string presetKey, long commitTicks)
            {
                this.slotIndex = slotIndex;
                this.instanceId = instanceId;
                this.presetKey = presetKey;
                this.commitTicks = commitTicks;
            }
        }

        #endregion

        /// <summary>每槽最大尝试次数（§13.4 冻结）</summary>
        public const int MaxAttemptsPerSlot = 2;

        private readonly ModeGRunState _state;
        private readonly Dictionary<int, int> _slotAttempts = new Dictionary<int, int>();
        private readonly HashSet<int> _committedSlots = new HashSet<int>();
        private readonly HashSet<Health> _activeBosses = new HashSet<Health>();
        private readonly Dictionary<CharacterMainControl, string> _committedKeyByCharacter
            = new Dictionary<CharacterMainControl, string>();
        private readonly Dictionary<Health, string> _committedKeyByHealth
            = new Dictionary<Health, string>();
        private readonly List<CommitJournalEntry> _journal = new List<CommitJournalEntry>();

        public ModeGSpawnTransaction(ModeGRunState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            _state = state;
        }

        #region Lease / Commit

        /// <summary>
        /// 尝试获取槽位生成 lease。已失效/已提交/尝试耗尽时返回 Exhausted outcome。
        /// </summary>
        public SpawnAttemptLease TryAcquireLease(int slotIndex, string presetKey)
        {
            if (_state.spawnLeasesInvalidated || _committedSlots.Contains(slotIndex))
            {
                return new SpawnAttemptLease(slotIndex, 0, presetKey, ModeGSlotOutcome.Exhausted);
            }

            int attempts;
            _slotAttempts.TryGetValue(slotIndex, out attempts);
            if (attempts >= MaxAttemptsPerSlot)
            {
                return new SpawnAttemptLease(slotIndex, attempts, presetKey, ModeGSlotOutcome.Exhausted);
            }

            attempts++;
            _slotAttempts[slotIndex] = attempts;
            return new SpawnAttemptLease(slotIndex, attempts, presetKey, ModeGSlotOutcome.Pending);
        }

        /// <summary>
        /// 提交已生成 Boss：RegisterTrackedBoss -> ResolveSlotOnce(Committed)；失败回滚登记。
        /// </summary>
        public bool TryCommit(int slotIndex, string presetKey, CharacterMainControl character)
        {
            if (character == null || character.Health == null || _committedSlots.Contains(slotIndex)) return false;
            Health health = character.Health;
            if (!_state.RegisterTrackedBoss(health, character)) return false;

            if (!_state.ResolveSlotOnce(slotIndex, ModeGSlotOutcome.Committed))
            {
                _state.UnregisterTrackedBoss(health);
                return false;
            }

            _committedSlots.Add(slotIndex);
            _activeBosses.Add(health);
            _committedKeyByCharacter[character] = presetKey ?? string.Empty;
            _committedKeyByHealth[health] = presetKey ?? string.Empty;
            _journal.Add(new CommitJournalEntry(slotIndex, character.GetInstanceID(), presetKey, DateTime.UtcNow.Ticks));
            return true;
        }

        /// <summary>
        /// 槽位两次尝试耗尽结案。
        /// </summary>
        public bool MarkExhausted(int slotIndex)
        {
            return _state.ResolveSlotOnce(slotIndex, ModeGSlotOutcome.Exhausted);
        }

        /// <summary>
        /// Boss 死亡结案（去重；exact Health 引用身份）。
        /// </summary>
        public bool MarkKilled(Health health)
        {
            if (health == null || !_activeBosses.Remove(health)) return false;
            _state.UnregisterTrackedBoss(health);
            // 注意：不清理 _committedKeyByHealth —— 死亡回调链内仍需 Health->key 归因，
            // 映射保留至 ResetForNextWave（每波 Boss 数量极小，无泄漏风险）。
            return true;
        }

        /// <summary>
        /// 已提交 Boss 的冻结 preset key 查询（Health 引用身份；死亡后仍可用于归因，
        /// 因 OnDead 回调先于注销时机不保证，故保留至 MarkKilled/波重置清理）。
        /// </summary>
        public bool TryGetCommittedKeyByHealth(Health health, out string presetKey)
        {
            presetKey = null;
            if (health == null) return false;
            return _committedKeyByHealth.TryGetValue(health, out presetKey);
        }

        /// <summary>
        /// 已提交 Character 的冻结 preset key 查询（宿敌归因用）。exact 引用身份。
        /// </summary>
        public bool TryGetCommittedKey(CharacterMainControl character, out string presetKey)
        {
            presetKey = null;
            if (character == null) return false;
            return _committedKeyByCharacter.TryGetValue(character, out presetKey);
        }

        public int ActiveBossCount { get { return _activeBosses.Count; } }
        public int CommittedSlotCount { get { return _committedSlots.Count; } }
        public bool IsWaveSettled { get { return _state.AreAllSlotsResolved; } }
        public IReadOnlyList<CommitJournalEntry> Journal { get { return _journal; } }

        public void CollectActiveBossHealth(List<Health> sink)
        {
            if (sink == null) return;
            foreach (Health health in _activeBosses) sink.Add(health);
        }

        public void ResetForNextWave()
        {
            _journal.Clear();
            _slotAttempts.Clear();
            _committedSlots.Clear();
            _activeBosses.Clear();
            _committedKeyByCharacter.Clear();
            _committedKeyByHealth.Clear();
        }

        public void Clear()
        {
            ResetForNextWave();
        }

        #endregion

        #region SpawnCore Options（§13.1 Mode G 固定 options）

        /// <summary>
        /// 构建 Mode G official 路径 options：
        /// AllowRandomRetryFallback=false（只走调用方已确认预设）、ApplySharedMutators=false、
        /// HoldForExternalCommit=true（Mode G slot 事务提交后批量激活）、ManagedBossContext=null。
        /// </summary>
        public static EnemySpawnCoreOptions CreateOfficialSpawnOptions()
        {
            return new EnemySpawnCoreOptions
            {
                HoldForExternalCommit = true,
                ApplySharedMutators = false,
                AllowRandomRetryFallback = false,
                ManagedBossContext = null
            };
        }

        /// <summary>
        /// 构建 Mode G managed 路径 options：
        /// HoldForExternalCommit=true（托管 handle 自行提交）、ApplySharedMutators=false、
        /// AllowRandomRetryFallback=false（fail-closed）、ManagedBossContext=ctx。
        /// </summary>
        internal static EnemySpawnCoreOptions CreateManagedSpawnOptions(ManagedBossSpawnContext ctx)
        {
            return new EnemySpawnCoreOptions
            {
                HoldForExternalCommit = true,
                ApplySharedMutators = false,
                AllowRandomRetryFallback = false,
                ManagedBossContext = ctx
            };
        }

        #endregion
    }

    /// <summary>
    /// Mode G 生成桥接（partial ModBehaviour：SpawnCore 私有入口、preset 查找、奖励候选）。
    /// 全部为加法成员，不改 Jimmy 的 Utilities/EnemySpawnCore.cs。
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Official Spawn Bridge（§13.1）

        /// <summary>
        /// Mode G official Boss 生成桥接（固定 options；applyEquipment=false、
        /// applyBossMultiplier=true、skipBossRushLootTracking=true、normalizeDamageMultiplier=false、
        /// deferActivationUntilNextFrame=false）。
        /// </summary>
        internal async UniTask<ManagedBossPrepareResult> SpawnModeGOfficialBossAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            int waveNumber,
            Func<EnemySpawnContext, bool> onCommit)
        {
            if (preset == null) return null;
            CharacterRandomPreset stagingPreset = null;
            CharacterRandomPreset originalPreset = null;
            CharacterMainControl character = null;
            ModeGRunState state = ModeGRunContext.Current;
            bool stagingPresetRegistered = false;
            bool stagingBossRegistered = false;
            bool preparedSuccessfully = false;
            try
            {
                if (state == null) return null;
                EnsureCharacterPresetsCacheReady();
                if (cachedCharacterPresets == null
                    || !cachedCharacterPresets.TryGetValue(preset.name, out originalPreset)
                    || originalPreset == null) return null;

                stagingPreset = UnityEngine.Object.Instantiate(originalPreset);
                stagingPreset.name = "ModeG_Official_Staging_" + preset.name;
                stagingPreset.team = Teams.middle;
                stagingPreset.dropBoxOnDead = false;
                stagingPreset.setActiveByPlayerDistance = false;
                stagingPreset.canDieIfNotRaidMap = true;
                stagingPreset.exp = 0;
                stagingPresetRegistered = state.RegisterStagingPreset(stagingPreset);
                if (!stagingPresetRegistered) return null;

                EnemySpawnCoreResult result = await SpawnEnemyCoreInternalAsync(
                    preset,
                    position,
                    true,
                    () => ModeGRuntimeGates.IsModeGRunInProgress,
                    waveNumber,
                    skipDragonDescendant: false,
                    skipDragonKing: false,
                    applyEquipment: false,
                    applyBossMultiplier: true,
                    directPreset: stagingPreset,
                    skipBossRushLootTracking: true,
                    normalizeDamageMultiplier: false,
                    deferActivationUntilNextFrame: false,
                    onCommit: onCommit,
                    options: ModeGSpawnTransaction.CreateOfficialSpawnOptions());
                if (result == null || !result.success || result.context == null
                    || result.context.character == null) return null;

                character = result.context.character;
                if (character.Health == null || character.Health.IsDead) return null;
                stagingBossRegistered = state.RegisterStagingBoss(character.Health, character);
                if (!stagingBossRegistered || !character.Health.CanDieIfNotRaidMap
                    || HasModeGPlayerAuthoredBuff(character)) return null;
                character.Health.SetInvincible(true);
                character.gameObject.SetActive(false);
                if (character.gameObject.activeSelf || !character.Health.Invincible) return null;
                character.characterPreset = originalPreset;
                if (character.CharacterItem != null)
                    character.CharacterItem.SetInt("Exp", originalPreset.exp, true);
                state.UnregisterStagingPreset(stagingPreset);
                stagingPresetRegistered = false;
                UnityEngine.Object.Destroy(stagingPreset);
                stagingPreset = null;

                ManagedBossRuntimeHandle handle = new ManagedBossRuntimeHandle
                {
                    Character = character,
                    AchievementBossType = "Normal",
                    Activate = () =>
                    {
                        if (character == null || character.Health == null || character.Health.IsDead) return false;
                        ActivateModeGManagedCharacter(character);
                        return true;
                    },
                    Cleanup = reason => CleanupModeGManagedCharacter(
                        character,
                        character.characterPreset != null ? character.characterPreset.nameKey : string.Empty,
                        character.characterPreset != null ? character.characterPreset.name : string.Empty,
                        "[ModeGOfficial]")
                };
                preparedSuccessfully = true;
                return new ManagedBossPrepareResult { Character = character, Handle = handle };
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] SpawnModeGOfficialBossAsync 异常: " + e.Message);
                return null;
            }
            finally
            {
                if (stagingPresetRegistered && state != null) state.UnregisterStagingPreset(stagingPreset);
                if (stagingPreset != null) UnityEngine.Object.Destroy(stagingPreset);
                if (!preparedSuccessfully && stagingBossRegistered && state != null)
                    state.UnregisterStagingBoss(character != null ? character.Health : null);
                if (!preparedSuccessfully && character != null) DestroyManagedCharacterQuiet(character);
            }
        }

        #endregion

        #region Preset Lookup Bridge（official key = EnemyPresetInfo.name）

        /// <summary>
        /// official Boss 池 run-scoped 快照 key（name 去重、Ordinal 升序；WavePlan.Build 用）。
        /// </summary>
        internal List<string> GetModeGOfficialBossPoolKeys()
        {
            List<string> keys = new List<string>();
            try
            {
                List<EnemyPresetInfo> pool = GetFilteredEnemyPresets();
                if (pool == null) return keys;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < pool.Count; i++)
                {
                    string name = pool[i] != null ? pool[i].name : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    // 托管三 Boss 的 Legacy preset 不进 official 池（避免署名/官方重复出场）
                    if (IsModeGManagedLegacyPreset(pool[i])) continue;
                    if (!ModeGOfficialBossEligibilityRegistry.IsEligible(name)) continue;
                    if (seen.Add(name)) keys.Add(name);
                }
                keys.Sort(StringComparer.Ordinal);
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] GetModeGOfficialBossPoolKeys 异常: " + e.Message);
                keys.Clear();
            }
            return keys;
        }

        /// <summary>
        /// 按 key（EnemyPresetInfo.name）查找 official Boss preset。未找到返回 null。
        /// </summary>
        internal EnemyPresetInfo FindModeGOfficialPresetByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!ModeGOfficialBossEligibilityRegistry.IsEligible(key)) return null;
            try
            {
                List<EnemyPresetInfo> pool = GetFilteredEnemyPresets();
                if (pool == null) return null;
                EnemyPresetInfo found = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null && string.Equals(pool[i].name, key, StringComparison.Ordinal))
                    {
                        if (found != null && !ReferenceEquals(found, pool[i])) return null;
                        found = pool[i];
                    }
                }
                return found;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] FindModeGOfficialPresetByKey 异常: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 判定 preset 是否属于托管三 Boss 的 Legacy 预设（official 池排除项）。
        /// </summary>
        private bool IsModeGManagedLegacyPreset(EnemyPresetInfo preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.name)) return false;
            string name = preset.name;
            return string.Equals(name, DragonDescendantConfig.BasePresetNameKey, StringComparison.Ordinal)
                || string.Equals(name, DragonKingConfig.BossNameKey, StringComparison.Ordinal)
                || string.Equals(name, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal)
                || string.Equals(name, PhantomWitchConfig.FallbackPresetNameKey, StringComparison.Ordinal);
        }

        #endregion

        #region Reward Candidate Bridge（Victory 奖励候选）

        /// <summary>
        /// 构建 Mode G 奖励候选（BuildGeneralBossLootCandidateIdSet 桥接 + GetMetaData 估值）。
        /// 失败返回空表（BuildSlotPlan fail-closed）。
        /// </summary>
        internal List<ModeGRewardCandidate> GetModeGRewardCandidates()
        {
            List<ModeGRewardCandidate> candidates = new List<ModeGRewardCandidate>();
            try
            {
                HashSet<int> typeIds = BuildGeneralBossLootCandidateIdSet();
                if (typeIds == null) return candidates;
                foreach (int typeId in typeIds)
                {
                    try
                    {
                        ItemStatsSystem.ItemMetaData meta = ItemStatsSystem.ItemAssetsCollection.GetMetaData(typeId);
                        // Mode G 奖励池只消费现有 Boss 战利品中的 Q5-Q8 高品质候选；
                        // 普通 BossRush 仍复用原始 Q1-Q8 搜索结果，不改变 Legacy 经济。
                        if (meta.id != typeId || meta.quality < 5 || meta.quality > 8) continue;
                        candidates.Add(new ModeGRewardCandidate(typeId, meta.priceEach, meta.defaultStackCount));
                    }
                    catch { /* 单项失败跳过 */ }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] GetModeGRewardCandidates 异常: " + e.Message);
                candidates.Clear();
            }
            return candidates;
        }

        #endregion

        #region Managed Boss Spawn Dispatcher（§13.2；RuntimeModule Initialize 赋值）

        /// <summary>
        /// Mode G 托管 Boss 生成调度（ManagedBossSpawnDispatcher 实现体）。
        /// 按 ctx.CreditPresetKey 路由三个 PrepareManagedXxx adapter；
        /// 这里只执行 Prepare；slot 提交成功后才由 RuntimeModule 激活 handle。
        /// </summary>
        internal async UniTask<ManagedBossPrepareResult> DispatchModeGManagedBossSpawnAsync(
            EnemyPresetInfo preset, Vector3 position, object managedContext, bool deferActivationUntilNextFrame)
        {
            ManagedBossSpawnContext ctx = managedContext as ManagedBossSpawnContext;
            if (ctx == null || ctx.Owner != ManagedBossOwner.ModeG) return null;

            // owner 存活校验（每个长 await 前重验）
            if (ctx.IsOwnerValid != null)
            {
                bool ownerValid = false;
                try { ownerValid = ctx.IsOwnerValid(); } catch { }
                if (!ownerValid) return null;
            }

            try
            {
                ManagedBossPrepareResult prepared = null;
                string key = ctx.CreditPresetKey ?? string.Empty;
                if (string.Equals(key, ModeGEncounterVariation.ManagedDragonDescendantKey, StringComparison.Ordinal))
                {
                    prepared = await PrepareManagedDragonDescendantAsync(position, ctx);
                }
                else if (string.Equals(key, ModeGEncounterVariation.ManagedDragonKingKey, StringComparison.Ordinal))
                {
                    prepared = await PrepareManagedDragonKingAsync(position, ctx);
                }
                else if (string.Equals(key, ModeGEncounterVariation.ManagedPhantomWitchKey, StringComparison.Ordinal))
                {
                    prepared = await PrepareManagedPhantomWitchAsync(position, ctx);
                }

                if (prepared == null || prepared.Character == null || prepared.Handle == null)
                {
                    DevLog("[ModeG] managed Prepare 失败 key=" + key);
                    return null;
                }

                // owner 二次校验（长 await 后）
                if (ctx.IsOwnerValid != null)
                {
                    bool stillValid = false;
                    try { stillValid = ctx.IsOwnerValid(); } catch { }
                    if (!stillValid)
                    {
                        prepared.Handle.CleanupOnce(ManagedBossCleanupReason.OwnerInvalid);
                        return null;
                    }
                }

                return prepared;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] DispatchModeGManagedBossSpawnAsync 异常: " + e.Message);
                return null;
            }
        }

        #endregion
    }
}
