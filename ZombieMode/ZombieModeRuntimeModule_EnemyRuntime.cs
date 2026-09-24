using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly HashSet<int> zombieModeEnemyInstanceIds = new HashSet<int>();

        private readonly Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId =
            new Dictionary<int, ZombieModeEnemyRuntimeMarker>();

        internal bool IsZombieModeKnownEnemy(CharacterMainControl character)
        {
            return character != null && zombieModeEnemyInstanceIds.Contains(character.GetInstanceID());
        }

        internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)
        {
            marker = null;
            if (character == null)
            {
                return false;
            }

            int instanceId = character.GetInstanceID();
            if (!zombieModeEnemyInstanceIds.Contains(instanceId))
            {
                return false;
            }

            if (zombieModeEnemyMarkersByInstanceId.TryGetValue(instanceId, out marker) &&
                marker != null)
            {
                return true;
            }

            marker = character.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker != null)
            {
                zombieModeEnemyMarkersByInstanceId[instanceId] = marker;
            }

            return true;
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            RegisterZombieModeEnemyInstanceId(character, null);
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)
        {
            if (character != null)
            {
                int instanceId = character.GetInstanceID();
                zombieModeEnemyInstanceIds.Add(instanceId);
                if (marker != null)
                {
                    zombieModeEnemyMarkersByInstanceId[instanceId] = marker;
                }
            }
        }

        internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            if (character != null)
            {
                int instanceId = character.GetInstanceID();
                zombieModeEnemyInstanceIds.Remove(instanceId);
                zombieModeEnemyMarkersByInstanceId.Remove(instanceId);
            }
        }

        internal void ClearZombieModeEnemyInstanceIds()
        {
            zombieModeEnemyInstanceIds.Clear();
            zombieModeEnemyMarkersByInstanceId.Clear();
        }

        internal ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(
            int runId,
            CharacterMainControl enemy,
            bool isBoss = false,
            ZombieModeBossKind bossKind = ZombieModeBossKind.Titan,
            int overridePointValue = -1,
            ZombieModeEnemyKind enemyKind = ZombieModeEnemyKind.Normal,
            ZombieModeSpecialKind specialKind = ZombieModeSpecialKind.None,
            List<ZombieModeEliteAffix> eliteAffixes = null)
        {
            if (!IsZombieModeRunValid(runId) || enemy == null || enemy.gameObject == null)
            {
                return null;
            }

            ZombieModeEnemyRuntimeMarker marker = enemy.gameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker == null)
            {
                marker = enemy.gameObject.AddComponent<ZombieModeEnemyRuntimeMarker>();
            }

            marker.RunId = runId;
            marker.PurificationPointValue = overridePointValue > 0
                ? overridePointValue
                : owner.CalculateZombieModeEnemyPurificationPointsForRuntimeModule(isBoss, enemyKind);
            marker.SuppressDrops = true;
            marker.IsBoss = isBoss;
            marker.DeathSettled = false;
            marker.RemovedFromRuntime = false;
            marker.CustomExploderSkillDetonated = false;
            marker.BossKind = bossKind;
            marker.EnemyKind = isBoss ? ZombieModeEnemyKind.Elite : enemyKind;
            marker.SpecialKind = specialKind;
            marker.Owner = enemy;
            marker.CachedAI = null;
            marker.RuntimeModifierRecords.Clear();
            marker.EliteAffixes.Clear();
            marker.AllyShield = null;
            marker.ShieldedAffix = null;
            marker.CommanderAuraTargetRuntime = null;
            marker.SuppressedForceTraceDistance = 0f;
            marker.HasSuppressedForceTraceDistance = false;
            owner.RestoreZombieModeVisualScaleForRuntimeModule(marker);
            owner.ReleaseZombieModeFootMarkerForRuntimeModule(marker);
            marker.VisualIdentityApplied = false;
            marker.VisualScaleApplied = false;
            marker.VisualFaceApplied = false;
            marker.VisualFootMarkerFallbackApplied = false;
            if (eliteAffixes != null)
            {
                marker.EliteAffixes.AddRange(eliteAffixes);
            }

            RegisterZombieModeEnemyInstanceId(enemy, marker);

            RegisterZombieModeRunOnlyObject(
                runId,
                isBoss ? ZombieModeRunOnlyObjectKind.Boss : ZombieModeRunOnlyObjectKind.Enemy,
                marker.gameObject,
                marker,
                () => owner.ReleaseZombieModeFootMarkerForRuntimeModule(marker));
            return marker;
        }

    }
}
