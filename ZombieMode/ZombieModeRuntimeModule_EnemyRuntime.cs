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
            RestoreZombieModeVisualScale(marker);
            ReleaseZombieModeFootMarker(marker);
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
                () => ReleaseZombieModeFootMarker(marker));
            return marker;
        }

        private readonly List<ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkerScratch = new List<ZombieModeEnemyRuntimeMarker>();

        internal int CollectZombieModeRuntimeEnemyMarkers(
            int runId,
            List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();
            if (!IsZombieModeRunValid(runId))
            {
                return 0;
            }

            for (int i = 0; i < runState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
                if (record == null || record.RunId != runId)
                {
                    continue;
                }

                if (record.Kind != ZombieModeRunOnlyObjectKind.Enemy &&
                    (!includeBosses || record.Kind != ZombieModeRunOnlyObjectKind.Boss))
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                if (marker == null ||
                    marker.RunId != runId ||
                    marker.DeathSettled ||
                    marker.RemovedFromRuntime)
                {
                    continue;
                }

                results.Add(marker);
            }

            return results.Count;
        }

        internal static AICharacterController GetZombieModeEnemyAI(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemyObject == null)
            {
                return null;
            }

            if (marker != null && marker.gameObject != enemyObject)
            {
                marker = null;
            }

            AICharacterController ai = marker != null ? marker.CachedAI : null;
            if (ai != null &&
                ai.gameObject != null &&
                ai.gameObject.activeInHierarchy &&
                ai.transform != null &&
                ai.transform.IsChildOf(enemyObject.transform))
            {
                return ai;
            }

            ai = enemyObject.GetComponentInChildren<AICharacterController>();
            if (marker != null)
            {
                marker.CachedAI = ai;
            }

            return ai;
        }

        internal static void SetZombieModeEnemyTargetToMainPlayer(AICharacterController ai)
        {
            if (ai == null)
            {
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.mainDamageReceiver == null)
            {
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
            }

            ai.searchedEnemy = main.mainDamageReceiver;
            ai.SetTarget(main.mainDamageReceiver.transform);
            ai.SetNoticedToTarget(main.mainDamageReceiver);
            ai.noticed = true;
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
                    try { ai.SetTarget(playerDamageReceiver.transform); }
                    catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] 聚怪奖励更新索敌失败: " + e.Message); }
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
        }

        internal CharacterMainControl TryFindZombieModeNearestEnemyTarget(int runId, CharacterMainControl exclude, float radius)
        {
            CharacterMainControl result = null;
            float bestSqr = radius * radius;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return null;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            Vector3 origin = exclude != null ? exclude.transform.position : (main != null ? main.transform.position : Vector3.zero);
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime)
                {
                    continue;
                }

                CharacterMainControl enemy = marker.Owner;
                if (enemy == null)
                {
                    enemy = marker.GetComponent<CharacterMainControl>();
                    marker.Owner = enemy;
                }

                if (enemy == null || enemy == exclude)
                {
                    continue;
                }

                float sqr = (enemy.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    result = enemy;
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
            return result;
        }

        internal static void RestoreZombieModeVisualScale(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.VisualScaleRecords == null)
            {
                return;
            }

            for (int i = marker.VisualScaleRecords.Count - 1; i >= 0; i--)
            {
                ZombieModeVisualScaleRecord record = marker.VisualScaleRecords[i];
                try
                {
                    if (record != null && record.Target != null)
                    {
                        record.Target.localScale = record.OriginalScale;
                    }
                }
                catch { }
            }
            marker.VisualScaleRecords.Clear();
        }

        internal static void ReleaseZombieModeFootMarker(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.VisualFootMarker == null)
            {
                return;
            }

            GameObject visual = marker.VisualFootMarker;
            marker.VisualFootMarker = null;
            marker.VisualFootMarkerFallbackApplied = false;
            ZombieModeFootMarkerPool.Release(visual);
        }
    }
}
