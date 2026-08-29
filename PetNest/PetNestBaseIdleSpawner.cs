// ============================================================================
// PetNestBaseIdleSpawner.cs - 基地闲逛崽（实施计划 步骤 12）
// ============================================================================
// 巢边蹲着的那几只。纯观赏，不参战、不进任何战斗统计。
//
// 硬约束（AGENTS.md 4.12 重运行时工作按实际使用状态门控）：
//   - **只在基地场景**且巢里有崽时才生成；离开基地立刻全清；
//   - 上限 PetNestTuning.MaxBaseIdleCompanions（3），超出不显示；
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

        /// <summary>当前在场的闲逛崽数量。</summary>
        internal static int ActiveCount { get { return _handles.Count; } }

        #endregion

        #region 生成

        /// <summary>
        /// 基地场景就绪后铺一批闲逛崽。非基地场景直接清空并返回。
        /// </summary>
        internal static void RefreshForScene(ModBehaviour owner, int sceneGeneration, bool isBaseScene)
        {
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
            if (_spawnInFlight) return;
            if (_handles.Count > 0 && sceneGeneration == _sceneGeneration) return;

            CleanupAll();
            _sceneGeneration = sceneGeneration;

            List<PetNestPetRecord> candidates = CollectCandidates();
            if (candidates.Count == 0) return;

            SpawnAsync(owner, candidates, sceneGeneration).Forget();
        }

        /// <summary>挑最多 N 只在巢待命的崽（远征中的不在巢，天然不入选）。</summary>
        private static List<PetNestPetRecord> CollectCandidates()
        {
            List<PetNestPetRecord> candidates = new List<PetNestPetRecord>();
            try
            {
                List<PetNestPetRecord> pets = PetNestService.Pets;
                for (int i = 0; i < pets.Count; i++)
                {
                    if (candidates.Count >= PetNestTuning.MaxBaseIdleCompanions) break;
                    PetNestPetRecord pet = pets[i];
                    if (pet == null) continue;
                    if (pet.state == (int)PetNestPetState.OnExpedition) continue;
                    if (string.IsNullOrEmpty(pet.lineageKey)) continue;
                    candidates.Add(pet);
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
            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    // 分帧：一只一帧，不在同一帧连开多次 CreateCharacterAsync
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(PetNestTuning.BaseIdleSpawnIntervalSeconds),
                        DelayType.UnscaledDeltaTime);

                    if (sceneGeneration != _sceneGeneration) return;
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player == null) return;

                    await SpawnOneAsync(owner, candidates[i], player, sceneGeneration, i);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 闲逛崽生成异常: " + e.Message);
            }
            finally
            {
                _spawnInFlight = false;
            }
        }

        private static async UniTask SpawnOneAsync(
            ModBehaviour owner, PetNestPetRecord pet, CharacterMainControl player,
            int sceneGeneration, int index)
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
            if (sceneGeneration != _sceneGeneration || CharacterMainControl.Main == null)
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

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            CleanupAll();
            _spawnInFlight = false;
            _sceneGeneration = -1;
        }

        #endregion
    }
}
