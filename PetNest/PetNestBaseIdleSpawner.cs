// ============================================================================
// PetNestBaseIdleSpawner.cs - 基地里跟着你的那只崽（实施计划 步骤 12）
// ============================================================================
// 纯观赏，不参战、不进任何战斗统计。
//
// 2026-09-20 owner 修正：「我选择了一个出击但是我全部崽都出击了」。
//   旧实现会把巢里最多 3 只崽一起铺到玩家身边，玩家的读法是「全都跟出来了」，
//   与「出战席位只有一个」的契约对不上。现在**只生成出战席位上的那一只**：
//   席位为空就一只都不出，改席位 / 点「不带崽出门」立刻重铺或收回。
//
// 硬约束（AGENTS.md 4.12 重运行时工作按实际使用状态门控）：
//   - **只在基地场景**且出战席位非空时才生成；离开基地立刻全清；
//   - 上限 PetNestTuning.MaxBaseIdleCompanions（仍作为硬顶，实际只会有 1 只）；
//   - **分帧生成**：一只一帧（间隔 BaseIdleSpawnIntervalSeconds），
//     不在同一帧连开三次 CreateCharacterAsync；
//   - 闲逛崽不借席、不挂容量 Modifier、不进致死钳制身份表以外的任何链路；
//   - 远征中的崽不出现（它不在巢里）。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>基地闲逛崽。由运行时模块的场景回调驱动。</summary>
    internal static class PetNestBaseIdleSpawner
    {
        #region 状态

        private static readonly List<PetNestCompanionHandle> _handles =
            new List<PetNestCompanionHandle>();
        private static bool _spawnInFlight;
        private static int _sceneGeneration = -1;

        /// <summary>最后一次 RefreshForScene 的宿主与是否基地，供出战席位变化时原地重铺。</summary>
        private static ModBehaviour _lastOwner;
        private static bool _lastIsBaseScene;

        /// <summary>
        /// 出战席位代数。改席位时递增，in-flight 生成协程一并校验——
        /// 否则「点了不带崽出门」之后，已经在飞的那次生成仍会把旧崽放出来。
        /// </summary>
        private static int _deployGeneration;

        /// <summary>当前在场的闲逛崽数量。</summary>
        internal static int ActiveCount { get { return _handles.Count; } }

        #endregion

        #region 生成

        /// <summary>
        /// 基地场景就绪后铺一批闲逛崽。非基地场景直接清空并返回。
        /// </summary>
        internal static void RefreshForScene(ModBehaviour owner, int sceneGeneration, bool isBaseScene)
        {
            _lastOwner = owner;
            _lastIsBaseScene = isBaseScene;

            if (!isBaseScene)
            {
                CleanupAll();
                // 代数也要推进：分帧生成协程 await 之后只比对 _sceneGeneration，
                // 离开基地时不推进的话 in-flight 协程会拿旧代数比对成功，
                // 把闲逛崽落到战斗场景里（绕过模式门控与单席契约）。
                _sceneGeneration = sceneGeneration;
                return;
            }
            if (owner == null) return;
            if (_spawnInFlight && sceneGeneration == _sceneGeneration) return;
            if (_handles.Count > 0 && sceneGeneration == _sceneGeneration) return;

            CleanupAll();
            _sceneGeneration = sceneGeneration;

            List<PetNestPetRecord> candidates = CollectCandidates();
            if (candidates.Count == 0) return;

            SpawnAsync(owner, candidates, sceneGeneration).Forget();
        }

        /// <summary>
        /// 只取**出战席位**上的那一只（owner 2026-09-20）。远征中 / 重伤退场的不出。
        /// 返回空表 = 这次一只都不生成，调用方直接返回。
        /// </summary>
        private static List<PetNestPetRecord> CollectCandidates()
        {
            List<PetNestPetRecord> candidates = new List<PetNestPetRecord>();
            try
            {
                PetNestPetRecord deployed = PetNestService.DeployedPet;
                if (deployed != null
                    && deployed.state != (int)PetNestPetState.OnExpedition
                    && deployed.state != (int)PetNestPetState.Downed
                    && !string.IsNullOrEmpty(deployed.lineageKey)
                    && candidates.Count < PetNestTuning.MaxBaseIdleCompanions)
                {
                    candidates.Add(deployed);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 闲逛崽候选收集失败: " + e.Message);
            }
            return candidates;
        }

        private static async UniTaskVoid SpawnAsync(
            ModBehaviour owner, List<PetNestPetRecord> candidates, int sceneGeneration)
        {
            _spawnInFlight = true;
            int deployGeneration = _deployGeneration;
            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    // 分帧：一只一帧，不在同一帧连开多次 CreateCharacterAsync
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(PetNestTuning.BaseIdleSpawnIntervalSeconds),
                        DelayType.UnscaledDeltaTime);

                    if (sceneGeneration != _sceneGeneration) return;
                    // 席位在等待期间被改掉：这次生成整条作废，不把旧崽放出来
                    if (deployGeneration != _deployGeneration) return;
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player == null) return;

                    await SpawnOneAsync(owner, candidates[i], player, sceneGeneration, deployGeneration, i);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 闲逛崽生成异常: " + e.Message);
            }
            finally
            {
                if (deployGeneration == _deployGeneration) _spawnInFlight = false;
            }
        }

        private static async UniTask SpawnOneAsync(
            ModBehaviour owner, PetNestPetRecord pet, CharacterMainControl player,
            int sceneGeneration, int deployGeneration, int index)
        {
            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null) return;

            CharacterRandomPreset source = PetNestCompanionSpawner.ResolveCompanionSourcePreset(pet.lineageKey);
            if (source == null) return;

            Vector3 stagingPos = player.transform.position + PetNestCompanionSpawner.StagingOffset;
            PetNestCompanionHandle handle = await PetNestCompanionSpawner.CreateIsolatedAsync(
                source, pet.lineageKey, lineage.ModelScale, stagingPos);
            if (handle == null) return;

            // await 之后重验：可能已经离开基地
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (sceneGeneration != _sceneGeneration || deployGeneration != _deployGeneration
                || !_lastIsBaseScene || owner == null || owner != _lastOwner
                || CharacterMainControl.Main == null || CharacterMainControl.Main != player
                || deployed == null || !string.Equals(deployed.id, pet.id, StringComparison.Ordinal)
                || deployed.state == (int)PetNestPetState.Downed
                || deployed.state == (int)PetNestPetState.OnExpedition)
            {
                PetNestCompanionSpawner.CleanupOnce(handle);
                return;
            }

            Vector3 spawnPos = player.transform.position
                + new Vector3(1.6f + index * 1.1f, 0.5f, -1.4f - index * 0.6f);

            string failureReasonId;
            if (!PetNestCompanionSpawner.TryActivate(handle, spawnPos, player, owner, pet, out failureReasonId))
            {
                PetNestCompanionSpawner.CleanupOnce(handle);
                return;
            }

            _handles.Add(handle);
        }

        #endregion

        #region 清理

        /// <summary>清空所有闲逛崽。幂等。离开基地、关开关、宿主销毁都要调。</summary>
        internal static void CleanupAll()
        {
            // 取消包含 CreateIsolatedAsync 在内的整条请求；旧 finally 不得清掉新请求的占位。
            unchecked { _deployGeneration++; }
            _spawnInFlight = false;
            for (int i = 0; i < _handles.Count; i++)
            {
                try
                {
                    PetNestCompanionSpawner.CleanupOnce(_handles[i]);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PetNest] 闲逛崽回收失败: " + e.Message);
                }
            }
            _handles.Clear();
        }

        /// <summary>
        /// 出战席位变化：立刻收回在场的崽，并按新席位重铺（owner 2026-09-20
        /// 「点了不带他们出击后就要立马收回」）。非基地场景只收不铺。幂等、no-throw。
        /// </summary>
        internal static void NotifyDeployedPetChanged()
        {
            try
            {
                CleanupAll();
                if (_lastOwner == null || !_lastIsBaseScene) return;
                RefreshForScene(_lastOwner, _sceneGeneration, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 出战席位变化后重铺闲逛崽失败: " + e.Message);
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            CleanupAll();
            _spawnInFlight = false;
            _sceneGeneration = -1;
            _lastOwner = null;
            _lastIsBaseScene = false;
        }

        #endregion
    }
}
