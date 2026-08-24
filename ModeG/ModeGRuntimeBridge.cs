using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode G 运行时桥接：Boss 池快照、managed 生成入口、生成位置、竞技场准备和波横幅。
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        internal ModeGBossSnapshot CreateModeGBossSnapshot()
        {
            try
            {
                InitializeEnemyPresets();
                InitializeBossPoolFilter();
                EnsureCharacterPresetsCacheReady();

                List<EnemyPresetInfo> filtered = GetFilteredEnemyPresets();
                if (filtered == null || filtered.Count == 0) return null;

                ModeGBossSnapshot snapshot = new ModeGBossSnapshot();
                EnemyPresetInfo dragonDescendantBase = null;
                EnemyPresetInfo dragonKingBase = null;
                EnemyPresetInfo phantomWitchBase = null;

                for (int i = 0; i < filtered.Count; i++)
                {
                    EnemyPresetInfo info = filtered[i];
                    if (info == null || string.IsNullOrEmpty(info.name)) continue;

                    if (IsDragonDescendantPreset(info))
                    {
                        if (dragonDescendantBase == null) dragonDescendantBase = info;
                        continue;
                    }
                    if (IsDragonKingPreset(info))
                    {
                        if (dragonKingBase == null) dragonKingBase = info;
                        continue;
                    }
                    if (IsPhantomWitchPreset(info))
                    {
                        if (phantomWitchBase == null) phantomWitchBase = info;
                        continue;
                    }
                    if (IsManagedBossPreset(info)) continue;
                    if (!ModeGOfficialBossEligibilityRegistry.IsEligible(info.name)) continue;

                    EnemyPresetInfo existingOfficial;
                    if (snapshot.infoByKey.TryGetValue(info.name, out existingOfficial))
                    {
                        if (!ReferenceEquals(existingOfficial, info))
                        {
                            DevLog("[ModeG] official stable key 对应多个 preset 引用，拒绝快照: " + info.name);
                            return null;
                        }
                        continue;
                    }
                    snapshot.infoByKey[info.name] = info;
                    snapshot.officialKeys.Add(info.name);
                }

                if (dragonDescendantBase != null)
                {
                    snapshot.infoByKey[ModeGEncounterVariation.ManagedDragonDescendantKey] = dragonDescendantBase;
                }
                if (dragonKingBase != null)
                {
                    snapshot.infoByKey[ModeGEncounterVariation.ManagedDragonKingKey] = dragonKingBase;
                }
                if (phantomWitchBase != null)
                {
                    snapshot.infoByKey[ModeGEncounterVariation.ManagedPhantomWitchKey] = phantomWitchBase;
                }

                snapshot.officialKeys.Sort(StringComparer.Ordinal);
                if (snapshot.officialKeys.Count
                    < ModeGOfficialBossEligibilityRegistry.MinimumProductionOfficialBossCount)
                {
                    DevLog("[ModeG] 当前过滤 Boss 池没有可用的官方 Boss key，拒绝创建快照");
                    return null;
                }
                if (snapshot.officialKeys.Count
                    < ModeGOfficialBossEligibilityRegistry.OfficialPoolReplicationTarget)
                {
                    DevLog("[ModeG] 官方 Boss 池仅有 " + snapshot.officialKeys.Count
                        + " 个唯一 key；本局波次将按 seed 从已有 key 随机复用至编排目标 6 个槽位");
                }
                return snapshot;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] CreateModeGBossSnapshot 异常: " + e.Message);
                return null;
            }
        }

        internal async UniTask<ManagedBossPrepareResult> SpawnModeGManagedBossAsync(
            EnemyPresetInfo info, Vector3 position, int waveNumber, ManagedBossSpawnContext ctx)
        {
            if (info == null || ctx == null) return null;
            try
            {
                EnemySpawnCoreResult result = await SpawnEnemyCoreInternalAsync(
                    info,
                    position,
                    true,
                    () => ModeGRuntimeGates.IsModeGRunInProgress,
                    waveNumber,
                    skipDragonDescendant: false,
                    skipDragonKing: false,
                    applyEquipment: false,
                    applyBossMultiplier: true,
                    directPreset: null,
                    skipBossRushLootTracking: true,
                    normalizeDamageMultiplier: false,
                    deferActivationUntilNextFrame: false,
                    onCommit: null,
                    options: ModeGSpawnTransaction.CreateManagedSpawnOptions(ctx));
                if (result == null || !result.success || result.context == null
                    || result.context.character == null || result.context.managedBossHandle == null)
                    return null;
                return new ManagedBossPrepareResult
                {
                    Character = result.context.character,
                    Handle = result.context.managedBossHandle
                };
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] SpawnModeGManagedBossAsync 异常: " + e.Message);
                return null;
            }
        }

        internal Vector3[] GetModeGSpawnPositions(int waveIndex, int count, ModeGPlanVariant variant,
            ModeGNemesisTemperament temperament, bool isNemesisWave)
        {
            try
            {
                if (count <= 0) return new Vector3[0];
                Vector3[] source = GetCurrentSceneSpawnPoints();
                if (source == null || source.Length == 0) return null;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return null;
                Vector3 playerPos = player.transform.position;

                ModeGWavePlan.FormationSpec spec = ModeGWavePlan.GetFormationSpec(variant);
                bool hunter = isNemesisWave && temperament == ModeGNemesisTemperament.Hunter;
                if (hunter)
                {
                    spec = new ModeGWavePlan.FormationSpec(
                        Mathf.Max(8f, spec.playerMinDistance - 3f), spec.bossPairMinDistance);
                }

                Vector3[] positions;
                if (TrySelectModeGFormation(source, playerPos, waveIndex, count, variant, spec,
                    hunter, out positions)) return positions;

                if (variant != ModeGPlanVariant.Split)
                {
                    ModeGWavePlan.FormationSpec splitSpec =
                        ModeGWavePlan.GetFormationSpec(ModeGPlanVariant.Split);
                    if (TrySelectModeGFormation(source, playerPos, waveIndex, count,
                        ModeGPlanVariant.Split, splitSpec, false, out positions))
                    {
                        DevLog("[ModeG] " + variant + " 几何不足，已在同一 verified 点集降级 Split");
                        return positions;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] GetModeGSpawnPositions 异常: " + e.Message);
                return null;
            }
        }

        private static bool TrySelectModeGFormation(Vector3[] source, Vector3 playerPos,
            int waveIndex, int count, ModeGPlanVariant variant,
            ModeGWavePlan.FormationSpec spec, bool preferNearest,
            out Vector3[] positions)
        {
            positions = null;
            List<Vector3> candidates = new List<Vector3>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 grounded;
                if (!SpawnPositionHelper.TrySnapToGround(source[i], out grounded)) continue;
                Vector3 playerDelta = grounded - playerPos;
                playerDelta.y = 0f;
                if (playerDelta.sqrMagnitude < spec.playerMinDistance * spec.playerMinDistance) continue;
                candidates.Add(grounded);
            }
            if (candidates.Count < count) return false;

            Vector2[] offsets = ModeGEncounterVariation.GetSpawnOffsets(variant, count, spec);
            if (offsets == null || offsets.Length != count) return false;
            float rotation = ((waveIndex * 47 + (int)variant * 31) % 360) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rotation);
            float sin = Mathf.Sin(rotation);
            bool[] used = new bool[candidates.Count];
            Vector3[] selected = new Vector3[count];
            float pairMinSqr = spec.bossPairMinDistance * spec.bossPairMinDistance;

            for (int slot = 0; slot < count; slot++)
            {
                Vector2 offset = offsets[slot];
                Vector3 target = playerPos + new Vector3(
                    offset.x * cos - offset.y * sin, 0f,
                    offset.x * sin + offset.y * cos);
                int bestIndex = -1;
                float bestScore = float.MaxValue;
                int start = Mathf.Abs((waveIndex * 3 + slot * 5 + (int)variant) % candidates.Count);
                for (int step = 0; step < candidates.Count; step++)
                {
                    int index = (start + step) % candidates.Count;
                    if (used[index]) continue;
                    Vector3 candidate = candidates[index];
                    bool pairSafe = true;
                    for (int j = 0; j < slot; j++)
                    {
                        Vector3 delta = candidate - selected[j];
                        delta.y = 0f;
                        if (delta.sqrMagnitude < pairMinSqr) { pairSafe = false; break; }
                    }
                    if (!pairSafe) continue;

                    Vector3 scoreDelta = candidate - (preferNearest ? playerPos : target);
                    scoreDelta.y = 0f;
                    float score = scoreDelta.sqrMagnitude;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = index;
                    }
                }
                if (bestIndex < 0) return false;
                used[bestIndex] = true;
                selected[slot] = candidates[bestIndex];
            }

            positions = selected;
            return true;
        }

        internal bool PrepareModeGArenaRuntime(ModeGEntryPreview preview)
        {
            try
            {
                if (!IsModeGEntryPreviewValidForCurrentScene(preview)) return false;
                SetCurrentMapSpawnPoints(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    DevLog("[ModeG] [ERROR] 竞技场准备失败：verified 地图没有刷新点");
                    return false;
                }
                InitializeItemValueCacheAsync();
                TryCreateArenaDifficultyEntryPoint();
                BossRushSignInteractable sign = FindObjectOfType<BossRushSignInteractable>();
                if (sign != null) sign.AddAmmoRefillOption();
                bossRushArenaActive = true;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] PrepareModeGArenaRuntime 异常: " + e.Message);
                return false;
            }
        }

        internal bool CommitModeGArenaEntry(ModeGEntryPreview preview)
        {
            try
            {
                if (!IsModeGEntryPreviewValidForCurrentScene(preview)) return false;
                PreCacheMapSpawnerPositions();
                DisableAllSpawners();
                if (!spawnersDisabled)
                {
                    DevLog("[ModeG] [ERROR] 竞技场提交失败：原生刷怪器未进入禁用状态");
                    return false;
                }
                ClearEnemiesForBossRush();
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] CommitModeGArenaEntry 异常: " + e.Message);
                return false;
            }
        }

        internal void ShowModeGWaveBanner(int waveIndex, ModeGWavePlan.WaveSlot wave,
            ModeGCounterAxis axis, ModeGNemesisTemperament temperament)
        {
            try
            {
                string axisText;
                if (axis == ModeGCounterAxis.Distance)
                {
                    axisText = L10n.T("BossRush_ModeG_AxisDistance");
                }
                else if (axis == ModeGCounterAxis.Ammo)
                {
                    axisText = L10n.T("BossRush_ModeG_AxisAmmo");
                }
                else if (axis == ModeGCounterAxis.Attribute)
                {
                    axisText = L10n.T("BossRush_ModeG_AxisAttribute");
                }
                else
                {
                    axisText = L10n.T("宿命试探", "Fate Probe");
                }

                if (wave != null && wave.isNemesisWave)
                {
                    axisText += L10n.T(" · 宿敌降临", " · Nemesis Descends");
                    string temperamentName = ModeGAdaptiveCombat.GetTemperamentDisplayName(temperament);
                    if (!string.IsNullOrEmpty(temperamentName)) axisText += " · " + temperamentName;
                }

                ShowBigBanner(L10n.T("第 ", "Wave ") + (waveIndex + 1)
                    + L10n.T("/9 波 - ", "/9 - ") + axisText);
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] ShowModeGWaveBanner 异常: " + e.Message);
            }
        }
    }
}
