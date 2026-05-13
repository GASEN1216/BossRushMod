// ============================================================================
// PhantomWitchAbilityController partial - extracted from PhantomWitchAbilityController.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public partial class PhantomWitchAbilityController : MonoBehaviour
    {
        private void CleanupMinions()
        {
            for (int i = 0; i < summonedMinions.Count; i++)
            {
                CharacterMainControl minion = summonedMinions[i];
                if (minion != null)
                {
                    try
                    {
                        if (minion.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(minion.gameObject);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 清理随从失败: " + e.Message);
                    }
                }
            }

            summonedMinions.Clear();
            liveMinions.Clear();
            pendingMinionRoles.Clear();
            ModBehaviour.DevLog("[PhantomWitch] 所有随从已清理");
        }

        private void CleanupSpawnedMinion(CharacterMainControl minion)
        {
            if (minion == null)
            {
                return;
            }

            summonedMinions.Remove(minion);
            liveMinions.RemoveAll(delegate(MinionEntry entry)
            {
                return entry == null || entry.character == null || entry.character == minion;
            });

            try
            {
                if (minion.gameObject != null)
                {
                    UnityEngine.Object.Destroy(minion.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 清理待接管随从失败: " + e.Message);
            }
        }

        private void CleanupAllEffects()
        {
            PruneDestroyedEffects();

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                GameObject effect = activeEffects[i];
                if (effect != null)
                {
                    UnityEngine.Object.Destroy(effect);
                }
            }

            activeEffects.Clear();
        }

        private CharacterRandomPreset GetCachedMinionPreset()
        {
            if (sharedMinionPresetSearched)
            {
                return cachedSharedMinionPreset;
            }

            sharedMinionPresetSearched = true;

            try
            {
                CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();

                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.MinionPresetNameKey)
                    {
                        cachedSharedMinionPreset = preset;
                        ModBehaviour.DevLog("[PhantomWitch] 缓存随从预设: " + preset.name);
                        return cachedSharedMinionPreset;
                    }
                }

                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.FallbackPresetNameKey)
                    {
                        cachedSharedMinionPreset = preset;
                        ModBehaviour.DevLog("[PhantomWitch] 缓存随从回退预设: " + preset.name);
                        return cachedSharedMinionPreset;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 查找随从预设失败: " + e.Message);
            }

            return null;
        }

        private void PruneDestroyedEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (activeEffects[i] == null)
                {
                    activeEffects.RemoveAt(i);
                }
            }
        }

        private void TrackEffect(GameObject effect)
        {
            try
            {
                PruneDestroyedEffects();
                if (effect != null)
                {
                    activeEffects.Add(effect);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 生成特效失败: " + e.Message);
            }
        }

        internal CharacterMainControl GetBossCharacterForRealmRuntime()
        {
            return bossCharacter;
        }

        internal void NotifyBossCurseRealmRuntimeEnded(PhantomWitchBossCurseRealmRuntime runtime, string reason)
        {
            if (runtime == null)
            {
                return;
            }

            if (activeBossCurseRealm == runtime)
            {
                activeBossCurseRealm = null;
            }

            EmitTelemetry("realm_clear", "reason=" + reason + ",phase=" + CurrentPhase);
        }

        private void EmitTelemetry(string eventName, string payloadKv)
        {
            if (!PhantomWitchConfig.TelemetryEnabled)
            {
                return;
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] " + eventName + " | " + payloadKv);
        }

        private void PrintTelemetrySummary()
        {
            if (!PhantomWitchConfig.TelemetryEnabled)
            {
                return;
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] === Phantom Witch Summary ===");
            string verdict = "PASS";
            for (int phaseIndex = 0; phaseIndex < 3; phaseIndex++)
            {
                float total = phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex] + phaseVisibleSeconds[phaseIndex];
                float stealthRatio = total > 0.01f
                    ? (phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex]) / total
                    : 0f;
                float target = phaseIndex == 0
                    ? PhantomWitchConfig.Phase1StealthRatioTarget
                    : (phaseIndex == 1 ? PhantomWitchConfig.Phase2StealthRatioTarget : PhantomWitchConfig.Phase3StealthRatioTarget);
                bool pass = Mathf.Abs(stealthRatio - target) <= PhantomWitchConfig.StealthRatioTolerance;
                if (!pass && verdict == "PASS")
                {
                    verdict = "WARN";
                }

                ModBehaviour.DevLog("[PhantomWitchTelemetry] P" + (phaseIndex + 1)
                    + " duration: " + total.ToString("0.0") + "s"
                    + " | stealth ratio: " + stealthRatio.ToString("0.00")
                    + " (target " + target.ToString("0.00") + " ±" + PhantomWitchConfig.StealthRatioTolerance.ToString("0.00") + ") "
                    + (pass ? "PASS" : "WARN"));
            }

            if (weaponTransformPosDriftMax > 0f || weaponTransformRotDriftMax > 0f || realmMisfireCount > 0 || minionRosterDesyncCount > 0)
            {
                verdict = "FAIL";
            }
            else if ((stealthTimeoutCount > 0 || wraithFallbackCount > 0) && verdict == "PASS")
            {
                verdict = "WARN";
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] Packages fired: P1=" + phasePackageCounts[0] + " P2=" + phasePackageCounts[1] + " P3=" + phasePackageCounts[2]);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Realms: warnings=" + realmWarningCount
                + " commits=" + realmCommitCount
                + " forced_clears_on_transition=" + realmForcedClearOnTransitionCount
                + " misfires=" + realmMisfireCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Minions: max concurrent=" + minionMaxConcurrent
                + " totalSpawned=" + minionTotalSpawned
                + " rolesSeen=[" + DescribeLiveRoles() + "] desync=" + minionRosterDesyncCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Weapon transform drift: posMax=" + weaponTransformPosDriftMax.ToString("0.0000")
                + " rotMax=" + weaponTransformRotDriftMax.ToString("0.0000")
                + " " + ((weaponTransformPosDriftMax <= 0f && weaponTransformRotDriftMax <= 0f) ? "PASS" : "FAIL"));
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Stealth timeouts=" + stealthTimeoutCount + " Wraith fallbacks=" + wraithFallbackCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Verdict: " + verdict);
        }

        private string DescribeLiveRoles()
        {
            List<string> roles = new List<string>(2);
            if (sawSustainMinion)
            {
                roles.Add(PhantomWitchMinionRole.Sustain.ToString());
            }
            if (sawHarassMinion)
            {
                roles.Add(PhantomWitchMinionRole.Harass.ToString());
            }

            return string.Join(",", roles.ToArray());
        }

        private string DescribeCurrentLiveRoles()
        {
            List<string> roles = new List<string>(2);
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                if (entry == null || entry.character == null || entry.character.Health == null || entry.character.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                string roleName = entry.role.ToString();
                if (!roles.Contains(roleName))
                {
                    roles.Add(roleName);
                }
            }

            return string.Join(",", roles.ToArray());
        }

        private float GetPlayerHealthRatio()
        {
            if (playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.MaxHealth <= 0f)
            {
                return 0f;
            }

            return playerCharacter.Health.CurrentHealth / playerCharacter.Health.MaxHealth;
        }

        private void OnDisable()
        {
            RestoreStealthVisuals();
            stealthCachedRenderers.Clear();
            stealthCachedAlphas.Clear();
            stealthCachedBlocks.Clear();
        }

        private void OnDestroy()
        {
            // OnBossDeath / OnPlayerDeath 已完成清理并调了 Destroy(gameObject)，
            // Unity 再次回调 OnDestroy 时跳过重复操作。
            if (CurrentPhase == PhantomWitchPhase.Dead)
            {
                ModBehaviour.DevLog("[PhantomWitch] 组件销毁（已由 OnBossDeath/OnPlayerDeath 清理）");
                return;
            }

            Health.OnDead -= OnAnyEntityDeath;
            StopAllCoroutines();
            ClearActiveBossCurseRealm("controller_destroy");
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            CleanupControllerRuntimeState();
            ReleaseAssetReferenceIfNeeded();

            bossCharacter = null;
            bossHealth = null;
            playerCharacter = null;

            ModBehaviour.DevLog("[PhantomWitch] 组件销毁，资源清理完成");
        }

        // 清理跨场景缓存；部分 AppDomain 级缓存由运行时复用，不在场景切换时释放。
        public static void ClearStaticCache()
        {
            cachedSharedMinionPreset = null;
            sharedMinionPresetSearched = false;
        }
    }
}
