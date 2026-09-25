using UnityEngine;

namespace BossRush
{
    public sealed class ZombieModeVisualScaleRecord
    {
        public Transform Target;
        public Vector3 OriginalScale;
    }

    internal static class ZombieModeFootMarkerPool
    {
        private static readonly System.Collections.Generic.Stack<GameObject> Pool =
            new System.Collections.Generic.Stack<GameObject>();

        internal static GameObject Acquire(Transform owner, Mesh mesh, Material material)
        {
            GameObject marker = null;
            while (Pool.Count > 0 && marker == null)
            {
                marker = Pool.Pop();
            }

            if (marker == null)
            {
                marker = new GameObject("ZombieMode_PersistentFootMarker");
                marker.AddComponent<MeshFilter>();
                marker.AddComponent<MeshRenderer>();
            }

            MeshFilter meshFilter = marker.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = marker.GetComponent<MeshRenderer>();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            marker.transform.SetParent(owner, false);
            marker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = new Vector3(0.65f, 0.02f, 0.65f);
            marker.SetActive(true);
            return marker;
        }

        internal static void Release(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            marker.transform.SetParent(null, false);
            marker.SetActive(false);
            Pool.Push(marker);
        }

        internal static void Clear()
        {
            while (Pool.Count > 0)
            {
                GameObject marker = Pool.Pop();
                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker);
                }
            }
        }
    }

    public sealed class ZombieModeEnemyRuntimeMarker : MonoBehaviour
    {
        public int RunId;
        public int PurificationPointValue;
        public bool SuppressDrops;
        public bool IsBoss;
        public bool DeathSettled;
        public bool RemovedFromRuntime;
        public bool CustomExploderSkillDetonated;
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
        public readonly System.Collections.Generic.List<BossRushStatModifierRecord> RuntimeModifierRecords =
            new System.Collections.Generic.List<BossRushStatModifierRecord>();

        // Hot path 缓存：HandleZombieModeHealthHurt 每次玩家命中都会查 ally shield；
        // 改读字段而非 GetComponent。激活护盾时由 ApplyZombieModeShielderGroupShield /
        // Shielder 自盾路径写入；护盾过期由 ZombieModeBossShieldRuntime 自身管理状态，
        // 字段保留指向已失活的组件即可（IsShieldActive() 会返回 false）。
        public ZombieModeBossShieldRuntime AllyShield;
        public ZombieModeShieldedAffixRuntime ShieldedAffix;
        public ZombieModeCommanderAuraTargetRuntime CommanderAuraTargetRuntime;
        public float SuppressedForceTraceDistance;
        public bool HasSuppressedForceTraceDistance;
        public bool VisualIdentityApplied;
        public bool VisualScaleApplied;
        public bool VisualFaceApplied;
        public bool VisualFootMarkerFallbackApplied;
        public GameObject VisualFootMarker;
        public readonly System.Collections.Generic.List<ZombieModeVisualScaleRecord> VisualScaleRecords =
            new System.Collections.Generic.List<ZombieModeVisualScaleRecord>();
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        internal bool IsZombieModeKnownEnemy(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeKnownEnemy(character);
        }

        internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)
        {
            marker = null;
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryGetZombieModeKnownEnemyMarker(character, out marker);
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEnemyInstanceId(character);
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEnemyInstanceId(character, marker);
        }

        internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.UnregisterZombieModeEnemyInstanceId(character);
        }

        internal void ClearZombieModeEnemyInstanceIds()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ClearZombieModeEnemyInstanceIds();
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
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.RegisterZombieModeEnemyRuntimeShell(
                    runId,
                    enemy,
                    isBoss,
                    bossKind,
                    overridePointValue,
                    enemyKind,
                    specialKind,
                    eliteAffixes)
                : null;
        }

        internal int CalculateZombieModeEnemyPurificationPointsForRuntimeModule(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            return CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind);
        }

        internal void RestoreZombieModeVisualScaleForRuntimeModule(ZombieModeEnemyRuntimeMarker marker)
        {
            RestoreZombieModeVisualScale(marker);
        }

        internal void ReleaseZombieModeFootMarkerForRuntimeModule(ZombieModeEnemyRuntimeMarker marker)
        {
            ReleaseZombieModeFootMarker(marker);
        }

        private static void RestoreZombieModeVisualScale(ZombieModeEnemyRuntimeMarker marker)
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

        private static void ReleaseZombieModeFootMarker(ZombieModeEnemyRuntimeMarker marker)
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

        private static AICharacterController GetZombieModeEnemyAI(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
        {
            return ZombieModeRuntimeModule.GetZombieModeEnemyAI(enemyObject, marker);
        }

        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone()
        {
            return zombieModeRuntimeModule.ShouldSuppressZombieModeEnemyAggroForSafeZone();
        }

        private bool TryMoveZombieModeEnemyOutsideSafeZone(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressThreat)
        {
            return zombieModeRuntimeModule.TryMoveZombieModeEnemyOutsideSafeZone(enemyObject, marker, suppressThreat);
        }

        private void SetZombieModeEnemyThreatSuppressed(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressed)
        {
            zombieModeRuntimeModule.SetZombieModeEnemyThreatSuppressed(enemyObject, marker, suppressed);
        }

        private bool ShouldZombieModeEnemyAggroPlayerNow()
        {
            return zombieModeRuntimeModule.ShouldZombieModeEnemyAggroPlayerNow();
        }
    }
}
