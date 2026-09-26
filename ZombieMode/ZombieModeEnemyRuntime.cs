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

}
