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
        public bool RecycledForPerformance;
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

        // Hot path 缓存：HandleZombieModeHealthHurt 每次玩家命中都会查 ally shield；
        // 改读字段而非 GetComponent。激活护盾时由 ApplyZombieModeShielderGroupShield /
        // Shielder 自盾路径写入；护盾过期由 ZombieModeBossShieldRuntime 自身管理状态，
        // 字段保留指向已失活的组件即可（IsShieldActive() 会返回 false）。
        public ZombieModeBossShieldRuntime AllyShield;
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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
            marker.RecycledForPerformance = false;
            marker.BossKind = bossKind;
            marker.EnemyKind = isBoss ? ZombieModeEnemyKind.Elite : enemyKind;
            marker.SpecialKind = specialKind;
            marker.EliteAffixes.Clear();
            marker.AllyShield = null;
            if (eliteAffixes != null)
            {
                marker.EliteAffixes.AddRange(eliteAffixes);
            }

            RegisterZombieModeRunOnlyObject(
                runId,
                isBoss ? ZombieModeRunOnlyObjectKind.Boss : ZombieModeRunOnlyObjectKind.Enemy,
                marker.gameObject,
                marker,
                null);
            return marker;
        }
    }
}
