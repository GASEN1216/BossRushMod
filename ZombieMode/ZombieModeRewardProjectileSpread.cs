// ============================================================================
// ZombieModeRewardProjectileSpread.cs - 丧尸模式弹道扩散与战场效果
// ============================================================================

using System.Collections;
using Duckov.Buffs;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RebuildZombieModeProjectileSpreadState()
        {
            EnsureZombieModeProjectileSpreadListener();
            CharacterMainControl player = CharacterMainControl.Main;
            DuckovItemAgent holdAgent = player != null ? player.CurrentHoldItemAgent : null;
            Item holdItem = holdAgent != null ? holdAgent.Item : null;
            if (holdItem == null)
            {
                RestoreZombieModeProjectileSpreadState();
                return;
            }

            ItemSetting_Gun gunSetting = holdItem.GetComponent<ItemSetting_Gun>();
            if (gunSetting == null || holdItem.Stats == null)
            {
                RestoreZombieModeProjectileSpreadState();
                return;
            }

            RestoreZombieModeProjectileSpreadStateExcept(holdItem);
            RestoreExistingZombieModeProjectileSpreadSnapshot(holdItem);
            ZombieModeProjectileSpreadSnapshot snapshot = CaptureZombieModeProjectileSpreadSnapshot(holdItem);
            if (snapshot == null)
            {
                return;
            }

            Stat shotCountStat = holdItem.Stats.GetStat("ShotCount");
            Stat shotAngleStat = holdItem.Stats.GetStat("ShotAngle");
            Stat damageStat = holdItem.Stats.GetStat("Damage");
            if (shotCountStat == null || shotAngleStat == null || damageStat == null)
            {
                RestoreZombieModeProjectileSpreadSnapshot(snapshot);
                zombieModeProjectileSpreadSnapshots.Remove(holdItem.GetInstanceID());
                return;
            }

            float currentShotCount = shotCountStat.Value;
            float currentShotAngle = shotAngleStat.Value;
            int shotCount = Mathf.Max(1, Mathf.RoundToInt(currentShotCount));
            float shotAngle = Mathf.Max(0f, currentShotAngle);
            if (zombieModeRunState.OptionRuntime.ProjectileTridentStacks > 0)
            {
                shotCount = Mathf.Max(shotCount, 3);
                shotAngle = Mathf.Max(shotAngle, 8f);
            }

            if (zombieModeRunState.OptionRuntime.ProjectileShotgunSprayStacks > 0)
            {
                shotCount = Mathf.Max(shotCount, 5);
                shotAngle = Mathf.Max(shotAngle, 18f);
            }

            float damageSplitMultiplier = CalculateZombieModeProjectileSpreadDamageMultiplier(currentShotCount, shotCount);
            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "ShotCount", shotCount - currentShotCount);
            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "ShotAngle", shotAngle - currentShotAngle);
            TryAddZombieModeGunStatRuntimePercentageModifier(snapshot, holdItem, "Damage", damageSplitMultiplier - 1f);
        }

        private float CalculateZombieModeProjectileSpreadDamageMultiplier(float originalShotCount, int appliedShotCount)
        {
            float safeOriginalShotCount = Mathf.Max(1f, originalShotCount);
            int safeAppliedShotCount = Mathf.Max(1, appliedShotCount);
            return Mathf.Clamp(safeOriginalShotCount / safeAppliedShotCount, 0.05f, 1f);
        }

        internal bool CanTriggerZombieModeProjectileTrailDamage(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            float now = GetZombieModeRuntimeNow();
            if (now - options.LastProjectileTrailDamageTime < 0.06f)
            {
                return false;
            }

            options.LastProjectileTrailDamageTime = now;
            return true;
        }

        private void RestoreZombieModeProjectileSpreadState()
        {
            foreach (var pair in zombieModeProjectileSpreadSnapshots)
            {
                RestoreZombieModeProjectileSpreadSnapshot(pair.Value);
            }

            zombieModeProjectileSpreadSnapshots.Clear();
        }

        private void RestoreZombieModeProjectileSpreadStateExcept(Item exceptItem)
        {
            zombieModeProjectileSpreadRestoreScratch.Clear();
            foreach (var pair in zombieModeProjectileSpreadSnapshots)
            {
                ZombieModeProjectileSpreadSnapshot snapshot = pair.Value;
                if (snapshot != null && object.ReferenceEquals(snapshot.Item, exceptItem))
                {
                    continue;
                }

                RestoreZombieModeProjectileSpreadSnapshot(snapshot);
                zombieModeProjectileSpreadRestoreScratch.Add(pair.Key);
            }

            for (int i = 0; i < zombieModeProjectileSpreadRestoreScratch.Count; i++)
            {
                zombieModeProjectileSpreadSnapshots.Remove(zombieModeProjectileSpreadRestoreScratch[i]);
            }

            zombieModeProjectileSpreadRestoreScratch.Clear();
        }

        private void RestoreZombieModeProjectileSpreadSnapshot(ZombieModeProjectileSpreadSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            RuntimeStatModifierTracker.RemoveAll(snapshot.ModifierRecords, "ZombieMode Projectile Spread");
        }

        private void EnsureZombieModeProjectileSpreadListener()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            if (zombieModeSpreadSubscribedPlayer == player)
            {
                return;
            }

            if (zombieModeSpreadSubscribedPlayer != null)
            {
                try
                {
                    zombieModeSpreadSubscribedPlayer.OnHoldAgentChanged -= OnZombieModeSpreadHoldAgentChanged;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] spread hold listener swap failed: " + e.Message);
                }
            }

            zombieModeSpreadSubscribedPlayer = player;
            player.OnHoldAgentChanged += OnZombieModeSpreadHoldAgentChanged;
        }

        private void OnZombieModeSpreadHoldAgentChanged(DuckovItemAgent newAgent)
        {
            if (!IsZombieModeActive)
            {
                return;
            }

            if (zombieModeRunState.OptionRuntime.ProjectileTridentStacks <= 0 &&
                zombieModeRunState.OptionRuntime.ProjectileShotgunSprayStacks <= 0)
            {
                return;
            }

            RebuildZombieModeProjectileSpreadState();
        }

        private void TryAddZombieModeGunStatRuntimeModifier(ZombieModeProjectileSpreadSnapshot snapshot, Item item, string statName, float delta)
        {
            if (snapshot == null || item == null || item.Stats == null || string.IsNullOrEmpty(statName) || Mathf.Abs(delta) < 0.0001f)
            {
                return;
            }

            try
            {
                Stat stat = item.Stats.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                // Order 300 is intentional: spread overlays are a late runtime delta; revisit if ShotCount/ShotAngle gain multiplicative gear affixes.
                Modifier modifier = new Modifier(ModifierType.Add, delta, true, 300, snapshot);
                stat.AddModifier(modifier);
                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = item;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                snapshot.ModifierRecords.Add(record);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread stat apply failed: " + statName + ", " + e.Message);
            }
        }

        private void TryAddZombieModeGunStatRuntimePercentageModifier(ZombieModeProjectileSpreadSnapshot snapshot, Item item, string statName, float percent)
        {
            if (snapshot == null || item == null || item.Stats == null || string.IsNullOrEmpty(statName) || Mathf.Abs(percent) < 0.0001f)
            {
                return;
            }

            try
            {
                Stat stat = item.Stats.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                // Order 300 is intentional: spread overlays are a late runtime delta; revisit if per-pellet Damage needs to compose differently with gun affixes.
                Modifier modifier = new Modifier(ModifierType.PercentageAdd, percent, true, 300, snapshot);
                stat.AddModifier(modifier);
                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = item;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                snapshot.ModifierRecords.Add(record);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread percent stat apply failed: " + statName + ", " + e.Message);
            }
        }

        private void StartZombieModeBattlefieldAreaRuntimeIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) ||
                (options.BattlefieldPurgeAuraStacks <= 0 && options.BattlefieldCurseTrapStacks <= 0) ||
                options.BattlefieldAreaRuntimeStarted)
            {
                return;
            }

            options.BattlefieldAreaRuntimeStarted = true;
            StartZombieModeCoroutine(ZombieModeBattlefieldAreaCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeBattlefieldAreaCoroutine(int runId)
        {
            float nextAuraTime = GetZombieModeRuntimeNow() + 2.5f;
            float nextTrapTime = GetZombieModeRuntimeNow() + 10f;
            while (IsZombieModeRunValid(runId) &&
                   (zombieModeRunState.OptionRuntime.BattlefieldPurgeAuraStacks > 0 ||
                    zombieModeRunState.OptionRuntime.BattlefieldCurseTrapStacks > 0))
            {
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    yield return null;
                    continue;
                }

                float now = GetZombieModeRuntimeNow();
                int auraStacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldPurgeAuraStacks);
                if (auraStacks > 0 && now >= nextAuraTime)
                {
                    float auraRadius = auraStacks >= 2 ? 4.5f : 3.2f;
                    float auraDamage = auraStacks >= 2 ? 35f : 20f;
                    DealZombieModeExplosionAreaDamage(runId, player, player.transform.position, auraRadius, auraDamage, false);
                    nextAuraTime = now + (auraStacks >= 2 ? 2f : 3f);
                }

                int trapStacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldCurseTrapStacks);
                if (trapStacks > 0 && now >= nextTrapTime)
                {
                    Vector3 origin = player.transform.position + player.transform.forward * (trapStacks >= 2 ? 4f : 3f);
                    float radius = trapStacks >= 2 ? 4.5f : 3.5f;
                    float damage = trapStacks >= 2 ? 70f : 45f;
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        player,
                        origin,
                        radius,
                        damage,
                        trapStacks >= 2 ? 0.8f : 1.2f,
                        L10n.T("BossRush_ZombieMode_Reward_BattlefieldCurseTrap"));
                    nextTrapTime = now + (trapStacks >= 2 ? 14f : 18f);
                }

                yield return null;
            }

            zombieModeRunState.OptionRuntime.BattlefieldAreaRuntimeStarted = false;
        }

        private void StartZombieModeBattlefieldGravityRuntimeIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) ||
                (options.BattlefieldBlackHoleStacks <= 0 && options.BattlefieldGravityDragStacks <= 0) ||
                options.BattlefieldGravityRuntimeStarted)
            {
                return;
            }

            options.BattlefieldGravityRuntimeStarted = true;
            StartZombieModeCoroutine(ZombieModeBattlefieldGravityCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeBattlefieldGravityCoroutine(int runId)
        {
            float nextWellTime = GetZombieModeRuntimeNow() + 4f;
            while (IsZombieModeRunValid(runId) &&
                   (zombieModeRunState.OptionRuntime.BattlefieldBlackHoleStacks > 0 ||
                    zombieModeRunState.OptionRuntime.BattlefieldGravityDragStacks > 0))
            {
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    yield return null;
                    continue;
                }

                float now = GetZombieModeRuntimeNow();
                if (now >= nextWellTime)
                {
                    ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
                    float radius = options.BattlefieldBlackHoleStacks > 0 ? 5.5f : 4.5f;
                    float duration = options.BattlefieldBlackHoleStacks > 0 ? 4.5f : 3.2f;
                    float pullStrength = options.BattlefieldGravityDragStacks > 0 ? 2.4f : 1.4f;
                    Vector3 origin = player.transform.position + player.transform.forward * (options.BattlefieldBlackHoleStacks > 0 ? 4.5f : 3.2f);

                    GameObject zone = CreateZombieModeFlatZoneVisual(
                        "ZombieMode_GravityWell",
                        origin + Vector3.up * 0.03f,
                        radius,
                        0.02f,
                        new Color(0.25f, 0.25f, 0.25f, 0.40f));
                    ZombieModeGravityWellRuntime runtime = zone.AddComponent<ZombieModeGravityWellRuntime>();
                    runtime.Initialize(runId, origin, radius, pullStrength, duration);
                    RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, zone, runtime, null);

                    nextWellTime = now + (options.BattlefieldBlackHoleStacks > 0 ? 12f : 16f);
                }

                yield return null;
            }

            zombieModeRunState.OptionRuntime.BattlefieldGravityRuntimeStarted = false;
        }

        internal void RefreshZombieModeGravityWellTargets(int runId, Vector3 origin, float radius, float pullStrength)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }
            if (radius <= 0f)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            DamageReceiver playerDamageReceiver = player != null ? player.mainDamageReceiver : null;
            float radiusSqr = radius * radius;
            const float minPullDistance = 0.4f;
            const float stopDistance = 0.35f;
            float minPullDistanceSqr = minPullDistance * minPullDistance;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime || marker.IsBoss)
                {
                    continue;
                }

                CharacterMainControl enemy = marker.Owner;
                if (enemy == null)
                {
                    enemy = marker.GetComponent<CharacterMainControl>();
                    marker.Owner = enemy;
                }

                if (enemy == null || enemy.transform == null)
                {
                    continue;
                }

                Vector3 delta = origin - enemy.transform.position;
                delta.y = 0f;
                float distanceSqr = delta.sqrMagnitude;
                if (distanceSqr <= minPullDistanceSqr || distanceSqr > radiusSqr)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSqr);
                float stepDistance = Mathf.Min(distance - stopDistance, pullStrength);
                Vector3 step = delta * (stepDistance / distance);
                enemy.transform.position += step;

                AICharacterController ai = GetZombieModeEnemyAI(enemy.gameObject, marker);
                if (ai != null && playerDamageReceiver != null)
                {
                    ai.searchedEnemy = playerDamageReceiver;
                    try { ai.SetTarget(playerDamageReceiver.transform); } catch { }
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
        }

        private ZombieModeProjectileSpreadSnapshot CaptureZombieModeProjectileSpreadSnapshot(Item item)
        {
            if (item == null || item.Stats == null)
            {
                return null;
            }

            ZombieModeProjectileSpreadSnapshot existing;
            int snapshotKey = item.GetInstanceID();
            if (zombieModeProjectileSpreadSnapshots.TryGetValue(snapshotKey, out existing))
            {
                if (existing != null && object.ReferenceEquals(existing.Item, item))
                {
                    RestoreZombieModeProjectileSpreadSnapshot(existing);
                    return existing;
                }

                RestoreZombieModeProjectileSpreadSnapshot(existing);
                zombieModeProjectileSpreadSnapshots.Remove(snapshotKey);
            }

            try
            {
                Stat shotCountStat = item.Stats.GetStat("ShotCount");
                Stat shotAngleStat = item.Stats.GetStat("ShotAngle");
                if (shotCountStat == null || shotAngleStat == null)
                {
                    return null;
                }

                ZombieModeProjectileSpreadSnapshot snapshot = new ZombieModeProjectileSpreadSnapshot();
                snapshot.Item = item;
                zombieModeProjectileSpreadSnapshots[snapshotKey] = snapshot;
                return snapshot;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread snapshot failed: " + e.Message);
                return null;
            }
        }

        private void RestoreExistingZombieModeProjectileSpreadSnapshot(Item item)
        {
            if (item == null)
            {
                return;
            }

            ZombieModeProjectileSpreadSnapshot existing;
            int snapshotKey = item.GetInstanceID();
            if (zombieModeProjectileSpreadSnapshots.TryGetValue(snapshotKey, out existing) &&
                existing != null &&
                object.ReferenceEquals(existing.Item, item))
            {
                RestoreZombieModeProjectileSpreadSnapshot(existing);
            }
        }
    }
}
