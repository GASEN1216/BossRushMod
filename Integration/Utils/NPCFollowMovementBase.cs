// ============================================================================
// NPCFollowMovementBase.cs - NPC 玩家跟随通用基类
// ============================================================================
// 模块说明：
//   抽取婚后 NPC / 伙伴类 NPC 的公共跟随逻辑，统一处理：
//   - 玩家引用刷新与 OnSetPositionEvent 绑定
//   - 切场景/传送时的直接同步
//   - 跟随速度与玩家速度同步
//   - 超距拉回与持续追踪重算
// ============================================================================

using Pathfinding;
using UnityEngine;

namespace BossRush
{
    public abstract class NPCFollowMovementBase : MonoBehaviour
    {
        private Transform playerTransform;
        private CharacterMainControl playerCharacter;
        private bool followPlayerEnabled;
        private float nextFollowRepathTime;
        private float baseWalkSpeed;
        private float baseRunSpeed;
        private bool followDefaultsInitialized;

        protected bool IsFollowingPlayer
        {
            get { return followPlayerEnabled; }
        }

        protected Transform CurrentPlayerTransform
        {
            get { return playerTransform; }
        }

        protected abstract float WalkSpeed { get; set; }
        protected abstract float RunSpeed { get; set; }
        protected abstract float FollowRepathInterval { get; }
        protected abstract float FollowStopDistance { get; }
        protected abstract float FollowRunDistance { get; }
        protected abstract float FollowTeleportDistance { get; }
        protected abstract float FollowSpeedBoostDistance { get; }
        protected abstract float FollowSpeedResetDistance { get; }
        protected abstract Seeker FollowSeeker { get; }
        protected abstract CharacterController FollowCharacterController { get; }
        protected abstract int FollowActivePathRequestId { get; set; }
        protected abstract bool FollowWaitingForPathResult { get; set; }
        protected abstract bool FollowReachedEndOfPath { get; set; }
        protected abstract void StopCurrentFollowMovement();
        protected abstract void HandleFollowPathComplete(Path path, int requestId);

        protected virtual Vector3 GetFollowDestination(Transform target)
        {
            return target != null ? target.position : transform.position;
        }

        protected virtual void OnEnable()
        {
            RefreshFollowPlayerCharacter(true);
        }

        protected virtual void OnDisable()
        {
            UnregisterPlayerPositionSync();
            ResetFollowSpeed();
        }

        protected virtual void OnDestroy()
        {
            UnregisterPlayerPositionSync();
        }

        protected void InitializeFollowDefaults()
        {
            EnsureFollowDefaultsInitialized();
            RefreshFollowPlayerCharacter(true);
        }

        public void EnablePlayerFollow(Transform target)
        {
            if (target != null)
            {
                playerTransform = target;
            }

            if (playerTransform == null)
            {
                RefreshFollowPlayerCharacter(true);
            }

            if (playerTransform == null)
            {
                return;
            }

            EnsureFollowDefaultsInitialized();
            followPlayerEnabled = true;
            RefreshFollowPlayerCharacter(true);
            SyncFollowSpeed(GetDistanceToPlayer());
            nextFollowRepathTime = 0f;
            StopMoveForFollow();
        }

        public void DisablePlayerFollow()
        {
            followPlayerEnabled = false;
            nextFollowRepathTime = 0f;
            ResetFollowSpeed();
            UnregisterPlayerPositionSync();
            StopMoveForFollow();
        }

        protected void RefreshFollowPlayerCharacter(bool force = false)
        {
            CharacterMainControl currentPlayer = null;

            try
            {
                currentPlayer = CharacterMainControl.Main;
            }
            catch
            {
            }

            if (currentPlayer != null)
            {
                if (force || playerCharacter != currentPlayer)
                {
                    RegisterPlayerPositionSync(currentPlayer);
                }

                playerTransform = currentPlayer.transform;
                return;
            }

            if (force)
            {
                UnregisterPlayerPositionSync();
            }

            if (playerTransform == null || force)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                playerTransform = player != null ? player.transform : null;
            }
        }

        protected void UpdateFollowDecision(bool moving, bool waitingForPathResult, bool hasPath)
        {
            if (!followPlayerEnabled)
            {
                return;
            }

            RefreshFollowPlayerCharacter();
            if (playerTransform == null)
            {
                return;
            }

            float distanceToPlayer = GetDistanceToPlayer();
            SyncFollowSpeed(distanceToPlayer);

            if (distanceToPlayer >= FollowTeleportDistance)
            {
                TeleportNearPlayer(playerTransform.position);
                return;
            }

            bool hasFollowTask = moving || waitingForPathResult || hasPath;
            if (distanceToPlayer <= FollowStopDistance)
            {
                if (hasFollowTask)
                {
                    StopMoveForFollow();
                }
                return;
            }

            if (Time.time < nextFollowRepathTime)
            {
                return;
            }

            if (TryRequestFollowPath(GetFollowDestination(playerTransform)))
            {
                nextFollowRepathTime = Time.time + FollowRepathInterval;
            }
        }

        protected bool ShouldRunWhileFollowing()
        {
            return followPlayerEnabled
                && playerTransform != null
                && GetDistanceToPlayer() > FollowRunDistance;
        }

        protected virtual void StopMoveForFollow()
        {
            StopCurrentFollowMovement();
        }

        protected virtual bool TryRequestFollowPath(Vector3 destination)
        {
            if (FollowSeeker == null)
            {
                return false;
            }

            FollowSeeker.CancelCurrentPathRequest(true);
            FollowReachedEndOfPath = false;

            int requestId = ++FollowActivePathRequestId;
            FollowWaitingForPathResult = true;
            FollowSeeker.StartPath(transform.position, destination, p => HandleFollowPathComplete(p, requestId));
            return true;
        }

        protected virtual void TeleportToFollowPosition(Vector3 targetPosition)
        {
            CharacterController controller = FollowCharacterController;
            bool wasEnabled = controller != null && controller.enabled;

            if (wasEnabled)
            {
                controller.enabled = false;
            }

            transform.position = targetPosition;

            if (wasEnabled)
            {
                controller.enabled = true;
            }
        }

        private void RegisterPlayerPositionSync(CharacterMainControl currentPlayer)
        {
            if (playerCharacter == currentPlayer)
            {
                return;
            }

            UnregisterPlayerPositionSync();
            playerCharacter = currentPlayer;

            if (playerCharacter != null)
            {
                playerCharacter.OnSetPositionEvent -= OnPlayerSetPosition;
                playerCharacter.OnSetPositionEvent += OnPlayerSetPosition;
            }
        }

        private void UnregisterPlayerPositionSync()
        {
            if (playerCharacter != null)
            {
                playerCharacter.OnSetPositionEvent -= OnPlayerSetPosition;
                playerCharacter = null;
            }
        }

        private void OnPlayerSetPosition(CharacterMainControl character, Vector3 targetPos)
        {
            if (!followPlayerEnabled || character == null)
            {
                return;
            }

            if (LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel)
            {
                return;
            }

            TeleportNearPlayer(targetPos);
        }

        private void TeleportNearPlayer(Vector3 playerPosition)
        {
            TeleportToFollowPosition(playerPosition);
            nextFollowRepathTime = 0f;
            StopMoveForFollow();
        }

        private float GetDistanceToPlayer()
        {
            if (playerTransform == null)
            {
                return float.MaxValue;
            }

            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;
            return toPlayer.magnitude;
        }

        private void SyncFollowSpeed(float distanceToPlayer)
        {
            EnsureFollowDefaultsInitialized();

            if (playerCharacter == null)
            {
                return;
            }

            if (distanceToPlayer > FollowSpeedBoostDistance)
            {
                WalkSpeed = Mathf.Max(baseWalkSpeed, playerCharacter.CharacterWalkSpeed + 2f);
                RunSpeed = Mathf.Max(baseRunSpeed, playerCharacter.CharacterRunSpeed + 2f);
            }
            else if (distanceToPlayer < FollowSpeedResetDistance)
            {
                WalkSpeed = Mathf.Max(baseWalkSpeed, playerCharacter.CharacterWalkSpeed);
                RunSpeed = Mathf.Max(baseRunSpeed, playerCharacter.CharacterRunSpeed);
            }
        }

        private void ResetFollowSpeed()
        {
            if (!followDefaultsInitialized)
            {
                return;
            }

            WalkSpeed = baseWalkSpeed;
            RunSpeed = baseRunSpeed;
        }

        private void EnsureFollowDefaultsInitialized()
        {
            if (followDefaultsInitialized)
            {
                return;
            }

            baseWalkSpeed = WalkSpeed;
            baseRunSpeed = RunSpeed;
            followDefaultsInitialized = true;
        }
    }
}
