using UnityEngine;

namespace BossRush
{
    public sealed class ZombieModeEnemyRuntimeMarker : MonoBehaviour
    {
        public int RunId;
        public int PurificationPointValue;
        public bool SuppressDrops;
        public bool IsBoss;
        public bool DeathSettled;
        public bool RemovedFromRuntime;
        public ZombieModeBossKind BossKind;
        public ZombieModeEnemyKind EnemyKind;
        public ZombieModeSpecialKind SpecialKind;
        public readonly System.Collections.Generic.List<ZombieModeEliteAffix> EliteAffixes =
            new System.Collections.Generic.List<ZombieModeEliteAffix>();
        public float BaseMaxHealth;
        public float HealthMultiplier = 1f;
        public float DamageMultiplier = 1f;
        public float MoveSpeedMultiplier = 1f;
        public int AdaptiveRangedHitCount;
        public int AdaptiveMeleeHitCount;
        public float AdaptiveReductionEndTime;
        public bool AdaptiveRangedActive;
        public bool AdaptiveMeleeActive;
        public CharacterMainControl Owner;
        public AICharacterController CachedAI;
        public readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> RuntimeModifierRecords =
            new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        // Hot path 缓存：HandleZombieModeHealthHurt 每次玩家命中都会查 ally shield；
        // 改读字段而非 GetComponent。激活护盾时由 ApplyZombieModeShielderGroupShield /
        // Shielder 自盾路径写入；护盾过期由 ZombieModeBossShieldRuntime 自身管理状态，
        // 字段保留指向已失活的组件即可（IsShieldActive() 会返回 false）。
        public ZombieModeBossShieldRuntime AllyShield;
        public ZombieModeShieldedAffixRuntime ShieldedAffix;
        public ZombieModeCommanderAuraTargetRuntime CommanderAuraTargetRuntime;
        public float SuppressedForceTraceDistance;
        public bool HasSuppressedForceTraceDistance;
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // Hot path 早返/marker 缓存（审查 §3.1）：OnHurt / OnDead 是 Health 全局事件，丧尸模式
        // 启动后场内每次扣血都会触发；过去需要付出 GetComponent<ZombieModeEnemyRuntimeMarker>
        // 才能判断是否丧尸模式敌人。引入 InstanceID 集合和 marker 字典后，handler 可以做 O(1)
        // 早返并复用 spawn 时注册的 marker；只有 fallback 才重新 GetComponent。
        // 同步路径：
        //   - spawn:   RegisterZombieModeEnemyRuntimeShell 内 Add
        //   - death:   HandleZombieModeHealthDead 在 marker.DeathSettled = true 之后 Remove
        //   - cleanup: 安全区/运行期清理移除敌人时 Remove
        //   - cleanup: ClearRuntime / CleanupZombieModeRunOnlyState 调用 Clear
        private readonly System.Collections.Generic.HashSet<int> zombieModeEnemyInstanceIds
            = new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId
            = new System.Collections.Generic.Dictionary<int, ZombieModeEnemyRuntimeMarker>();

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

        private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(
            int runId,
            CharacterMainControl enemy,
            bool isBoss = false,
            ZombieModeBossKind bossKind = ZombieModeBossKind.Titan,
            int overridePointValue = -1,
            ZombieModeEnemyKind enemyKind = ZombieModeEnemyKind.Normal,
            ZombieModeSpecialKind specialKind = ZombieModeSpecialKind.None,
            System.Collections.Generic.List<ZombieModeEliteAffix> eliteAffixes = null)
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
                : CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind);
            marker.SuppressDrops = true;
            marker.IsBoss = isBoss;
            marker.DeathSettled = false;
            marker.RemovedFromRuntime = false;
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
                null);
            return marker;
        }

        private static AICharacterController GetZombieModeEnemyAI(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
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
    }
}
