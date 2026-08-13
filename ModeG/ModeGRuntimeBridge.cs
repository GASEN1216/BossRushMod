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

                    if (!snapshot.infoByKey.ContainsKey(info.name))
                    {
                        snapshot.infoByKey[info.name] = info;
                        snapshot.officialKeys.Add(info.name);
                    }
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
                return snapshot.officialKeys.Count > 0 ? snapshot : null;
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

        internal Vector3[] GetModeGSpawnPositions(int waveIndex, int count, ModeGPlanVariant variant)
        {
            try
            {
                if (count <= 0) return new Vector3[0];
                Vector3[] source = GetCurrentSceneSpawnPoints();
                if (source == null || source.Length == 0) return null;

                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

                List<Vector3> candidates = new List<Vector3>(source);
                candidates.Sort((a, b) =>
                    (b - playerPos).sqrMagnitude.CompareTo((a - playerPos).sqrMagnitude));

                int start = Mathf.Abs((waveIndex * 3 + (int)variant) % candidates.Count);
                Vector3[] positions = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    Vector3 raw = candidates[(start + i) % candidates.Count];
                    positions[i] = SpawnPositionHelper.SnapToGround(raw);
                }
                return positions;
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [ERROR] GetModeGSpawnPositions 异常: " + e.Message);
                return null;
            }
        }

        internal void PrepareModeGArenaRuntime()
        {
            try
            {
                bossRushArenaActive = true;
                SetCurrentMapSpawnPoints(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                InitializeItemValueCacheAsync();
                TryCreateArenaDifficultyEntryPoint();
                BossRushSignInteractable sign = FindObjectOfType<BossRushSignInteractable>();
                if (sign != null) sign.AddAmmoRefillOption();
            }
            catch (Exception e)
            {
                DevLog("[ModeG] [WARNING] PrepareModeGArenaRuntime 异常: " + e.Message);
            }
        }

        internal void ShowModeGWaveBanner(int waveIndex, ModeGWavePlan.WaveSlot wave, ModeGCounterAxis axis)
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
