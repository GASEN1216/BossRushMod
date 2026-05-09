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
        public readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> RuntimeModifierRecords =
            new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        // Hot path 缓存：HandleZombieModeHealthHurt 每次玩家命中都会查 ally shield；
        // 改读字段而非 GetComponent。激活护盾时由 ApplyZombieModeShielderGroupShield /
        // Shielder 自盾路径写入；护盾过期由 ZombieModeBossShieldRuntime 自身管理状态，
        // 字段保留指向已失活的组件即可（IsShieldActive() 会返回 false）。
        public ZombieModeBossShieldRuntime AllyShield;
        public ZombieModeShieldedAffixRuntime ShieldedAffix;
        public float SuppressedForceTraceDistance;
        public bool HasSuppressedForceTraceDistance;
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // Hot path 早返集合（审查 §3.1）：OnHurt / OnDead 是 Health 全局事件，丧尸模式
        // 启动后场内每次扣血都会触发；过去需要付出 GetComponent<ZombieModeEnemyRuntimeMarker>
        // 才能判断是否丧尸模式敌人。引入此 InstanceID 集合后，handler 可以做 O(1) Contains
        // 早返；只有在敌人/Boss spawn 路径里才付一次 GetInstanceID。
        // 同步路径：
        //   - spawn:   RegisterZombieModeEnemyRuntimeShell 内 Add
        //   - death:   HandleZombieModeHealthDead 在 marker.DeathSettled = true 之后 Remove
        //   - cleanup: 安全区/运行期清理移除敌人时 Remove
        //   - cleanup: ClearRuntime / CleanupZombieModeRunOnlyState 调用 Clear
        private readonly System.Collections.Generic.HashSet<int> zombieModeEnemyInstanceIds
            = new System.Collections.Generic.HashSet<int>();

        internal bool IsZombieModeKnownEnemy(CharacterMainControl character)
        {
            return character != null && zombieModeEnemyInstanceIds.Contains(character.GetInstanceID());
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            if (character != null)
            {
                zombieModeEnemyInstanceIds.Add(character.GetInstanceID());
            }
        }

        internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            if (character != null)
            {
                zombieModeEnemyInstanceIds.Remove(character.GetInstanceID());
            }
        }

        internal void ClearZombieModeEnemyInstanceIds()
        {
            zombieModeEnemyInstanceIds.Clear();
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
            marker.RuntimeModifierRecords.Clear();
            marker.EliteAffixes.Clear();
            marker.AllyShield = null;
            marker.ShieldedAffix = null;
            marker.SuppressedForceTraceDistance = 0f;
            marker.HasSuppressedForceTraceDistance = false;
            if (eliteAffixes != null)
            {
                marker.EliteAffixes.AddRange(eliteAffixes);
            }

            RegisterZombieModeEnemyInstanceId(enemy);

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
