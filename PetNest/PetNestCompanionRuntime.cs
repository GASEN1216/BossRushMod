// ============================================================================
// PetNestCompanionRuntime.cs - 遗种巢随从局内生命周期（实施计划 步骤 6）
// ============================================================================
// 一局最多一只：入场生成 -> 跟随作战 -> 重伤退场 -> 离场清理。
//
// 硬约束（tests/PetNestCompanionLifecycleGuard.py 守卫）：
//   - 入场/退场/清理成对且幂等：任何一条路径（切图、玩家死亡、模式中止、宿主销毁）
//     都必须走同一个 CleanupOnce 入口；
//   - **零常驻**：只有出战席位非空、门控允许、且开关开启时才 CreateCharacterAsync，
//     局内至多一次；不带崽时整条链路零分配；
//   - 借席与容量 Modifier 必须与随从同寿命：入场挂、离场摘，finally 兜底；
//   - 生成是异步的，await 后必须重验 owner / 场景代数 / 玩家引用，失效就回收。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>随从局内生命周期。由 PetNestRuntimeModule 的场景回调驱动。</summary>
    internal static class PetNestCompanionRuntime
    {
        #region 状态

        private static PetNestCompanionHandle _handle;
        private static string _deployedPetId;
        private static int _sceneGeneration = -1;
        private static bool _spawnInFlight;
        private static string _lastBlockReasonId;

        /// <summary>
        /// 入场重试窗口。**必须重试的原因**：绝大多数地图在 sceneLoaded 这一刻
        /// 还没有任何 run 标志为真——带 customSpawnPos 的竞技场要等
        /// SetupBossRushInGroundZero 协程才置 bossRushArenaActive，Mode D 有 0.5s 延迟，
        /// IsActive 更是要等波次开始。只采样一次的话随从在 9 张竞技场里有 8 张永远进不了场。
        /// </summary>
        private static float _retryDeadlineUnscaled;
        private static float _nextRetryUnscaled;

        /// <summary>入场重试窗口时长（秒）。覆盖协程/延迟启动的模式置位。</summary>
        internal const float SpawnRetryWindowSeconds = 90f;

        /// <summary>入场重试间隔（秒）。</summary>
        internal const float SpawnRetryIntervalSeconds = 1f;

        /// <summary>本局已重伤退场（回基地才复位）。</summary>
        internal const string ReasonPetDowned = "pet_downed_this_run";

        /// <summary>当前是否有随从在场。</summary>
        internal static bool HasCompanion
        {
            get { return _handle != null && _handle.Character != null && _handle.Activated; }
        }

        /// <summary>在场随从对应的崽 id（无则 null）。</summary>
        internal static string ActiveCompanionPetId { get { return _deployedPetId; } }

        /// <summary>最近一次未能带崽的原因（诊断与 HUD 提示用）。</summary>
        internal static string LastBlockReasonId { get { return _lastBlockReasonId; } }

        /// <summary>已就该场景代数提示过的受阻原因（去重用）。</summary>
        private static string _notifiedBlockReasonId;
        private static int _notifiedBlockGeneration = -1;

        /// <summary>在场随从的角色引用（HUD / 重伤处理读取）。</summary>
        internal static CharacterMainControl CompanionCharacter
        {
            get { return _handle != null ? _handle.Character : null; }
        }

        #endregion

        #region 入场

        /// <summary>
        /// 场景就绪后尝试让出战席位的崽入场。
        /// 不满足任一前置条件时静默返回（零分配），并记录 blockReason 供诊断。
        /// </summary>
        internal static void TrySpawnForScene(ModBehaviour owner, int sceneGeneration)
        {
            _lastBlockReasonId = null;

            if (owner == null) { _lastBlockReasonId = PetNestModeGate.ReasonQueryFailed; return; }
            if (_spawnInFlight) return;
            if (HasCompanion) return;

            // 零常驻第一道闸：出战席位为空时连门控都不查
            PetNestPetRecord pet = PetNestService.DeployedPet;
            if (pet == null) return;
            // 本局已重伤退场：局中途换图也不能让它重新入场
            //（Downed 只在回基地由 RestoreDownedPetsOnReturnToBase 复位）
            if (pet.state == (int)PetNestPetState.Downed)
            {
                _lastBlockReasonId = ReasonPetDowned;
                NotifyBlockReasonOnce(owner, sceneGeneration);
                CloseSpawnRetryWindow();
                return;
            }

            string blockReasonId;
            if (!PetNestModeGate.IsCompanionAllowed(owner, out blockReasonId))
            {
                _lastBlockReasonId = blockReasonId;
                NotifyBlockReasonOnce(owner, sceneGeneration);
                return;
            }

            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null)
            {
                _lastBlockReasonId = "lineage_unknown";
                return;
            }

            CharacterRandomPreset source = PetNestCompanionSpawner.ResolveCompanionSourcePreset(pet.lineageKey);
            if (source == null)
            {
                // fail-closed：该血脉的 preset 不可用，本局不带崽，不影响其他血脉
                _lastBlockReasonId = "lineage_preset_missing";
                return;
            }

            _sceneGeneration = sceneGeneration;
            SpawnAsync(owner, pet, lineage, source, sceneGeneration).Forget();
        }

        /// <summary>
        /// 把「这局为什么没带上崽」告诉玩家一次。
        ///
        /// LastBlockReasonId 此前全仓没有任何消费者，而 HUD 又只在成功入场后才创建，
        /// 于是玩家设了出战崽却什么都没出现时，连一句解释都拿不到。
        /// 按 (场景代数, 原因) 去重：入场是每秒重试的，不去重会刷屏。
        /// </summary>
        private static void NotifyBlockReasonOnce(ModBehaviour owner, int sceneGeneration)
        {
            try
            {
                if (owner == null || string.IsNullOrEmpty(_lastBlockReasonId)) return;
                if (_notifiedBlockGeneration == sceneGeneration
                    && string.Equals(_notifiedBlockReasonId, _lastBlockReasonId, StringComparison.Ordinal))
                {
                    return;
                }
                _notifiedBlockGeneration = sceneGeneration;
                _notifiedBlockReasonId = _lastBlockReasonId;
                owner.ShowMessage(PetNestLocalization.DescribeFailure(_lastBlockReasonId));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 入场受阻提示失败: " + e.Message);
            }
        }

        /// <summary>
        /// 宿主 tick 驱动的入场重试。窗口内每 SpawnRetryIntervalSeconds 重试一次，
        /// 成功或窗口耗尽即停。无待办时 O(1) 早返。
        /// </summary>
        internal static void TickSpawnRetry(ModBehaviour owner, int sceneGeneration)
        {
            if (_retryDeadlineUnscaled <= 0f) return;

            float now = Time.unscaledTime;
            if (now > _retryDeadlineUnscaled)
            {
                _retryDeadlineUnscaled = 0f;
                return;
            }
            if (HasCompanion)
            {
                _retryDeadlineUnscaled = 0f;
                return;
            }
            // in-flight 只跳过本 tick，不关窗：那次异步生成若失败，窗口内还应继续重试。
            // 此前合并判定会让一次瞬态失败之后本图永远不再尝试入场。
            if (_spawnInFlight) return;
            if (now < _nextRetryUnscaled) return;
            _nextRetryUnscaled = now + SpawnRetryIntervalSeconds;

            TrySpawnForScene(owner, sceneGeneration);
        }

        /// <summary>开一个新的入场重试窗口（切图时调）。</summary>
        private static void OpenSpawnRetryWindow()
        {
            _retryDeadlineUnscaled = Time.unscaledTime + SpawnRetryWindowSeconds;
            _nextRetryUnscaled = 0f;
        }

        /// <summary>关闭入场重试窗口（回基地 / 关开关 / 清理）。</summary>
        private static void CloseSpawnRetryWindow()
        {
            _retryDeadlineUnscaled = 0f;
        }

        private static async UniTaskVoid SpawnAsync(
            ModBehaviour owner,
            PetNestPetRecord pet,
            PetNestLineageInfo lineage,
            CharacterRandomPreset source,
            int sceneGeneration)
        {
            _spawnInFlight = true;
            PetNestCompanionHandle handle = null;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                Vector3 stagingPos = player.transform.position + PetNestCompanionSpawner.StagingOffset;
                handle = await PetNestCompanionSpawner.CreateIsolatedAsync(
                    source, pet.lineageKey, lineage.ModelScale, stagingPos);
                if (handle == null) return;

                // await 之后重验 owner / 场景代数 / 玩家引用
                if (!IsRequestStillValid(owner, player, sceneGeneration))
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                    return;
                }

                CharacterMainControl playerNow = CharacterMainControl.Main;
                Vector3 spawnPos = playerNow.transform.position + PetNestCompanionSpawner.SpawnOffset;

                string failureReasonId;
                if (!PetNestCompanionSpawner.TryActivate(handle, spawnPos, playerNow, owner, pet, out failureReasonId))
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                    _lastBlockReasonId = failureReasonId;
                    return;
                }

                _handle = handle;
                _deployedPetId = pet.id;
                handle = null;

                // 捡漏背包：借席 + 容量 Modifier，与随从同寿命
                // 借席失败（反射不可用 / 席位被他方随从占着）时**不给容量加成**：
                // 席位仍归官方宠物，格子加在玩家身上等于白送给官方宠物背包，
                // 与「随从没借到席就没有捡漏收益」的 fail-closed 语义相反。
                string yieldReason;
                if (PetNestPetProxyBridge.TryBorrowSeat(_handle.Character, out yieldReason))
                {
                    PetNestPetProxyBridge.ApplyCapacityBonus(playerNow, ResolveCapacityBonus(pet));
                }
                else
                {
                    ModBehaviour.DevLog("[PetNest] 借席未成功，跳过容量加成: " + yieldReason);
                }

                // 战痕要刻"被谁打倒"：只在随从在场期间订阅官方 OnHurt，离场立刻退订
                PetNestDownedHandler.EnsureHurtSubscribed();
                // 击杀经验同理：只在随从在场窗口内订阅 OnDead
                PetNestProgressionService.EnsureKillTrackingSubscribed();
                // HUD 与随从同寿命：不带崽时连 canvas 都不建
                PetNestCompanionHudView.EnsureCreated();

                ModBehaviour.DevLog("[PetNest] 随从入场: " + PetNestService.GetPetDisplayName(pet));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 随从入场异常: " + e.Message);
            }
            finally
            {
                // 任何提前 return 都不能把半成品 handle 留在场上
                if (handle != null)
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                }
                _spawnInFlight = false;
            }
        }

        private static bool IsRequestStillValid(ModBehaviour owner, CharacterMainControl player, int sceneGeneration)
        {
            try
            {
                if (owner == null) return false;
                if (sceneGeneration != _sceneGeneration) return false;
                CharacterMainControl current = CharacterMainControl.Main;
                if (current == null || current != player) return false;
                if (current.Health != null && current.Health.IsDead) return false;
                string blockReasonId;
                return PetNestModeGate.IsCompanionAllowed(owner, out blockReasonId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 该崽提供的额外背包格子：基础值 + 出身天赋里的 PetCapcity 常量加。
        /// </summary>
        internal static int ResolveCapacityBonus(PetNestPetRecord pet)
        {
            int bonus = PetNestTuning.CompanionPetCapacityBonus;
            if (pet == null) return bonus;
            // 等级成长：每 PetLevelsPerCapacityBonus 级 +1 格（Lv10 满级共 +3）
            bonus += pet.level / PetNestTuning.PetLevelsPerCapacityBonus;
            if (pet.talents == null) return bonus;
            for (int i = 0; i < pet.talents.Count; i++)
            {
                PetNestTalentEntry t = pet.talents[i];
                if (t == null || t.percentage) continue;
                if (string.Equals(t.statKey, "PetCapcity", StringComparison.Ordinal))
                {
                    bonus += Mathf.RoundToInt(t.value);
                }
            }
            return bonus;
        }

        #endregion

        #region 退场与清理

        /// <summary>
        /// 重伤退场：本局不再出场，崽状态置 Downed（回基地自动复位）。
        /// 战痕落档由 PetNestDownedHandler 承接（步骤 7）。
        /// </summary>
        internal static void NotifyDowned()
        {
            try
            {
                if (!string.IsNullOrEmpty(_deployedPetId))
                {
                    string transactionError;
                    if (!PetNestPersistenceAccess.BeginTransaction(out transactionError))
                    {
                        CleanupOnce();
                        return;
                    }
                    PetNestPetRecord pet = PetNestService.TryGetPet(_deployedPetId);
                    if (pet != null)
                    {
                        pet.state = (int)PetNestPetState.Downed;
                        PetNestService.StageCommit();
                    }
                    else PetNestPersistenceAccess.AbortTransaction();
                }
            }
            catch (Exception e)
            {
                PetNestPersistenceAccess.AbortTransaction();
                ModBehaviour.DevLog("[PetNest] 标记重伤退场失败: " + e.Message);
            }
            CleanupOnce();
        }

        /// <summary>
        /// 幂等清理：还席 -> 摘容量 Modifier -> 回收随从。
        /// 切图、玩家死亡、模式中止、宿主销毁全部走这一个入口。
        /// </summary>
        internal static void CleanupOnce()
        {
            CloseSpawnRetryWindow();
            try
            {
                PetNestCompanionHudView.Destroy();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 销毁随从 HUD 失败: " + e.Message);
            }

            try
            {
                PetNestDownedHandler.ShutdownHurtSubscription();
                PetNestProgressionService.ShutdownKillTracking();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 退订 OnHurt 失败: " + e.Message);
            }

            try
            {
                PetNestPetProxyBridge.ReleaseSeat();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 随从还席失败: " + e.Message);
            }

            try
            {
                PetNestPetProxyBridge.RemoveCapacityBonus(CharacterMainControl.Main);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 摘除容量 Modifier 失败: " + e.Message);
            }

            try
            {
                if (_handle != null)
                {
                    PetNestCompanionSpawner.CleanupOnce(_handle);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 随从回收失败: " + e.Message);
            }
            finally
            {
                _handle = null;
                _deployedPetId = null;
            }
        }

        /// <summary>切图：先清干净上一局，再由 TrySpawnForScene 决定是否重新入场。</summary>
        internal static void OnSceneChanged(ModBehaviour owner, int sceneGeneration)
        {
            CleanupOnce();
            _sceneGeneration = sceneGeneration;
            // 没有出战崽就不必开窗：否则每秒一次空查询白跑满 90 秒
            if (PetNestService.DeployedPet == null) return;
            // 先试一次；模式标志通常要等协程/延迟才置位，所以同时开一个重试窗口
            OpenSpawnRetryWindow();
            TrySpawnForScene(owner, sceneGeneration);
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            CleanupOnce();
            CloseSpawnRetryWindow();
            _sceneGeneration = -1;
            _spawnInFlight = false;
            _lastBlockReasonId = null;
        }

        #endregion
    }
}
