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

        /// <summary>当前是否有随从在场。</summary>
        internal static bool HasCompanion
        {
            get { return _handle != null && _handle.Character != null && _handle.Activated; }
        }

        /// <summary>在场随从对应的崽 id（无则 null）。</summary>
        internal static string ActiveCompanionPetId { get { return _deployedPetId; } }

        /// <summary>最近一次未能带崽的原因（诊断与 HUD 提示用）。</summary>
        internal static string LastBlockReasonId { get { return _lastBlockReasonId; } }

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

            string blockReasonId;
            if (!PetNestModeGate.IsCompanionAllowed(owner, out blockReasonId))
            {
                _lastBlockReasonId = blockReasonId;
                return;
            }

            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null)
            {
                _lastBlockReasonId = "lineage_unknown";
                return;
            }

            CharacterRandomPreset source = PetNestCompanionSpawner.ResolveSourcePreset(pet.lineageKey);
            if (source == null)
            {
                // fail-closed：该血脉的 preset 不可用，本局不带崽，不影响其他血脉
                _lastBlockReasonId = "lineage_preset_missing";
                return;
            }

            _sceneGeneration = sceneGeneration;
            SpawnAsync(owner, pet, lineage, source, sceneGeneration).Forget();
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
                if (!PetNestCompanionSpawner.TryActivate(handle, spawnPos, playerNow, owner, out failureReasonId))
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                    _lastBlockReasonId = failureReasonId;
                    return;
                }

                _handle = handle;
                _deployedPetId = pet.id;
                handle = null;

                // 捡漏背包：借席 + 容量 Modifier，与随从同寿命
                string yieldReason;
                PetNestPetProxyBridge.TryBorrowSeat(_handle.Character, out yieldReason);
                PetNestPetProxyBridge.ApplyCapacityBonus(playerNow, ResolveCapacityBonus(pet));

                // 战痕要刻"被谁打倒"：只在随从在场期间订阅官方 OnHurt，离场立刻退订
                PetNestDownedHandler.EnsureHurtSubscribed();

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
            if (pet == null || pet.talents == null) return bonus;
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
                    PetNestPetRecord pet = PetNestService.TryGetPet(_deployedPetId);
                    if (pet != null)
                    {
                        pet.state = (int)PetNestPetState.Downed;
                        PetNestService.StageCommit();
                    }
                }
            }
            catch (Exception e)
            {
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
            try
            {
                PetNestDownedHandler.ShutdownHurtSubscription();
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
            TrySpawnForScene(owner, sceneGeneration);
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            CleanupOnce();
            _sceneGeneration = -1;
            _spawnInFlight = false;
            _lastBlockReasonId = null;
        }

        #endregion
    }
}
