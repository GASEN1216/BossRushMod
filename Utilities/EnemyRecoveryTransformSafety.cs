using System;
using UnityEngine;

namespace BossRush
{
    // 只恢复共享监控已跟踪的敌人；受击碰撞体祖先每两秒读缓存，
    // 注册和每十秒发现一次层级，补齐稍晚才生成的头部碰撞体。
    internal static class EnemyRecoveryTransformSafety
    {
        private const float ColliderCacheRefreshInterval = 10f;

        internal sealed class RecoveryTransformSnapshot
        {
            public Transform target;
            public Transform parent;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool valid;
            public int depth;
        }

        internal static bool IsFiniteRecoveryFloat(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFiniteRecoveryVector(Vector3 value)
        {
            return IsFiniteRecoveryFloat(value.x) && IsFiniteRecoveryFloat(value.y) && IsFiniteRecoveryFloat(value.z);
        }

        internal static bool IsRecoveryRotationValid(Quaternion value)
        {
            if (!IsFiniteRecoveryFloat(value.x) || !IsFiniteRecoveryFloat(value.y) ||
                !IsFiniteRecoveryFloat(value.z) || !IsFiniteRecoveryFloat(value.w)) return false;
            double lengthSquared = (double)value.x * value.x + (double)value.y * value.y +
                                   (double)value.z * value.z + (double)value.w * value.w;
            return Math.Abs(lengthSquared - 1d) < 0.01d;
        }

        internal static bool IsRecoveryScaleValid(Vector3 value)
        {
            return IsFiniteRecoveryVector(value) &&
                   // 超过一万亿倍的 scale 即使逐项有限，也可能使碰撞矩阵 / 包围盒溢出。
                   // 有限的零 / 负值 / 小 scale 保留，避免干扰有意隐藏的模型节点。
                   Math.Abs(value.x) <= 1e12f && Math.Abs(value.y) <= 1e12f && Math.Abs(value.z) <= 1e12f;
        }

        internal static bool IsRecoveryLocalTransformValid(Transform target)
        {
            return target != null && IsFiniteRecoveryVector(target.localPosition) &&
                   IsRecoveryRotationValid(target.localRotation) && IsRecoveryScaleValid(target.localScale);
        }

        internal static bool IsRecoveryRootTransformValid(Transform target)
        {
            return IsRecoveryLocalTransformValid(target) && IsFiniteRecoveryVector(target.position) &&
                   IsRecoveryRotationValid(target.rotation) && IsRecoveryScaleValid(target.lossyScale);
        }

        internal static void CaptureRecoveryTransformSnapshot(CharacterMainControl enemy, EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            Transform root = enemy.transform;
            if (!IsRecoveryRootTransformValid(root)) return;
            state.validPosition = root.position;
            state.validRotation = root.rotation;
            state.validScale = root.localScale;
            state.hasValidTransformSnapshot = true;
        }

        internal static void CacheRecoveryColliderTransforms(CharacterMainControl enemy, EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            Transform root = enemy.transform;
            if (state.root == null) state.root = root;
            for (int i = state.colliderTransforms.Count - 1; i >= 0; i--)
            {
                Transform previous = state.colliderTransforms[i].target;
                if (previous != null && previous.IsChildOf(root)) continue;
                state.cachedColliderTransforms.Remove(previous);
                state.colliderTransforms.RemoveAt(i);
            }
            bool added = false;
            Collider[] colliders = enemy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || (collider.GetComponent<DamageReceiver>() == null && collider.GetComponent<HeadCollider>() == null)) continue;
                Transform node = collider.transform;
                if (node == null || !node.IsChildOf(root)) continue;
                while (node != null && node != root)
                {
                    if (state.cachedColliderTransforms.Add(node))
                    {
                        int depth = 0;
                        for (Transform parent = node; parent != null && parent != root; parent = parent.parent) depth++;
                        RecoveryTransformSnapshot snapshot = new RecoveryTransformSnapshot { target = node, depth = depth };
                        if (IsRecoveryRootTransformValid(node)) CaptureRecoveryColliderSnapshot(snapshot);
                        state.colliderTransforms.Add(snapshot);
                        added = true;
                    }
                    node = node.parent;
                }
            }
            // 先修祖先，再检查后代，避免新头部碰撞体因继承祖先的 NaN 且没有自身历史快照而挡住恢复。
            if (added) state.colliderTransforms.Sort(CompareRecoveryTransformDepth);
            state.rigidbodies = enemy.GetComponentsInChildren<Rigidbody>(true);
            state.characterMovement = enemy.GetComponent<ECM2.CharacterMovement>();
            state.nextColliderCacheRefresh = Time.time + ColliderCacheRefreshInterval;
        }

        private static int CompareRecoveryTransformDepth(RecoveryTransformSnapshot a, RecoveryTransformSnapshot b)
        {
            return a.depth.CompareTo(b.depth);
        }

        internal static bool AreRecoveryColliderTransformsValid(EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            for (int i = 0; i < state.colliderTransforms.Count; i++)
            {
                Transform target = state.colliderTransforms[i].target;
                if (target != null && target.IsChildOf(state.root) && !IsRecoveryRootTransformValid(target)) return false;
            }
            return true;
        }

        internal static void CaptureRecoveryColliderSnapshot(RecoveryTransformSnapshot snapshot)
        {
            snapshot.parent = snapshot.target.parent;
            snapshot.position = snapshot.target.localPosition;
            snapshot.rotation = snapshot.target.localRotation;
            snapshot.scale = snapshot.target.localScale;
            snapshot.valid = true;
        }

        internal static void CaptureRecoveryColliderSnapshots(EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            for (int i = 0; i < state.colliderTransforms.Count; i++)
            {
                RecoveryTransformSnapshot snapshot = state.colliderTransforms[i];
                if (snapshot.target != null && snapshot.target.IsChildOf(state.root)) CaptureRecoveryColliderSnapshot(snapshot);
            }
        }

        internal static bool RestoreRecoveryColliderTransforms(CharacterMainControl enemy, EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            // Nodes can be reparented after caching; compute current ancestry only on the rare repair path.
            for (int i = 0; i < state.colliderTransforms.Count; i++)
            {
                RecoveryTransformSnapshot snapshot = state.colliderTransforms[i];
                int depth = 0;
                for (Transform node = snapshot.target; node != null && node != enemy.transform; node = node.parent) depth++;
                snapshot.depth = depth;
            }
            state.colliderTransforms.Sort(CompareRecoveryTransformDepth);
            for (int i = 0; i < state.colliderTransforms.Count; i++)
            {
                RecoveryTransformSnapshot snapshot = state.colliderTransforms[i];
                Transform target = snapshot.target;
                if (target == null || !target.IsChildOf(enemy.transform) || IsRecoveryRootTransformValid(target)) continue;
                if (!snapshot.valid || target.parent != snapshot.parent) return false;
                target.localPosition = snapshot.position;
                target.localRotation = snapshot.rotation;
                target.localScale = snapshot.scale;
            }
            return true;
        }

        internal static void ResetInvalidRecoveryRigidbodies(EnemyRecoveryMonitor.EnemyRecoveryState state)
        {
            if (state.rigidbodies == null) return;
            for (int i = 0; i < state.rigidbodies.Length; i++)
            {
                Rigidbody body = state.rigidbodies[i];
                if (body == null) continue;
                if (IsRecoveryRootTransformValid(body.transform))
                {
                    body.position = body.transform.position;
                    body.rotation = body.transform.rotation;
                }
                if (body.isKinematic) continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

    }
}
