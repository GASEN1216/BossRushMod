using System.Collections;
using System.Collections.Generic;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 阿稳掉落箱清理任务执行器。
    /// 仅在模式 E/F 的临时任务期间接管阿稳移动。
    /// </summary>
    public class AwenLootSweepRunner : MonoBehaviour
    {
        private const float STOP_DURATION_SECONDS = 0.5f;
        private const float MOVE_TIMEOUT_SECONDS = 8f;
        private const float ARRIVE_DISTANCE_SQR = 1.2f * 1.2f;
        private const float SWEEP_RUN_SPEED = 15f;

        private readonly List<AwenLootSweepTarget> activeTargets = new List<AwenLootSweepTarget>();

        private ModBehaviour mod;
        private CourierMovement movement;
        private CourierNPCController controller;
        private Coroutine sweepCoroutine;
        private BossRushTrackedLootboxMode activeMode = BossRushTrackedLootboxMode.None;
        private int activeSessionToken = 0;
        private int activeSceneIndex = -1;
        private float savedRunSpeed = -1f;

        public bool IsRunning
        {
            get { return sweepCoroutine != null; }
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnDestroy()
        {
            CancelSweep(false);
            if (mod != null)
            {
                mod.NotifyAwenLootSweepRunnerDestroyed(this);
            }
        }

        internal bool BeginSweep(
            List<AwenLootSweepTarget> targets,
            BossRushTrackedLootboxMode mode,
            int sessionToken,
            int relatedScene)
        {
            CacheReferences();

            if (IsRunning || targets == null || targets.Count <= 0 || mod == null || movement == null || controller == null)
            {
                return false;
            }

            activeTargets.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                AwenLootSweepTarget source = targets[i];
                if (source == null || source.Lootbox == null)
                {
                    continue;
                }

                AwenLootSweepTarget copy = new AwenLootSweepTarget();
                copy.Lootbox = source.Lootbox;
                copy.VisitPosition = source.VisitPosition;
                activeTargets.Add(copy);
            }

            if (activeTargets.Count <= 0)
            {
                return false;
            }

            activeMode = mode;
            activeSessionToken = sessionToken;
            activeSceneIndex = relatedScene;
            sweepCoroutine = StartCoroutine(RunSweep());
            return true;
        }

        public void CancelSweep(bool restoreDefaultState)
        {
            if (sweepCoroutine != null)
            {
                StopCoroutine(sweepCoroutine);
                sweepCoroutine = null;
            }

            activeTargets.Clear();

            if (restoreDefaultState)
            {
                RestoreDefaultState();
            }
            else if (movement != null)
            {
                if (savedRunSpeed >= 0f)
                {
                    movement.runSpeed = savedRunSpeed;
                    savedRunSpeed = -1f;
                }
                movement.SetScriptedRunMode(false);
            }
        }

        private IEnumerator RunSweep()
        {
            int clearedCount = 0;
            PrepareForSweep();

            try
            {
                for (int i = 0; i < activeTargets.Count; i++)
                {
                    AwenLootSweepTarget target = activeTargets[i];
                    if (target == null)
                    {
                        continue;
                    }

                    InteractableLootbox lootbox = target.Lootbox;
                    if (lootbox == null || lootbox.gameObject == null)
                    {
                        continue;
                    }

                    Vector3 targetPos = lootbox.transform.position;
                    target.VisitPosition = targetPos;

                    movement.ClearIdleState();
                    controller.StopTalking();
                    movement.SetScriptedRunMode(true);
                    movement.MoveToPos(targetPos);

                    float deadline = Time.unscaledTime + MOVE_TIMEOUT_SECONDS;
                    bool arrivedNormally = false;
                    while (Time.unscaledTime < deadline)
                    {
                        if (lootbox != null && lootbox.gameObject != null)
                        {
                            targetPos = lootbox.transform.position;
                            target.VisitPosition = targetPos;
                        }

                        if (movement.IsIdling || IsCloseEnough(targetPos))
                        {
                            arrivedNormally = true;
                            break;
                        }

                        yield return null;
                    }

                    // 超时未到达：瞬移到目标位置
                    if (!arrivedNormally)
                    {
                        TeleportTo(targetPos);
                    }

                    movement.StopMove();
                    movement.SetScriptedRunMode(false);
                    controller.StartTalking(false);
                    yield return new WaitForSecondsRealtime(STOP_DURATION_SECONDS);

                    bool cleared = DestroyTargetLootbox(target);
                    if (cleared)
                    {
                        clearedCount++;
                        ShowSweepBubble(clearedCount);
                    }

                    movement.ClearIdleState();
                    controller.StopTalking();
                    yield return null;
                }
            }
            finally
            {
                sweepCoroutine = null;
                activeTargets.Clear();
                RestoreDefaultState();
            }
        }

        private void PrepareForSweep()
        {
            CacheReferences();
            if (controller != null)
            {
                controller.SetStationary(false);
                controller.StopTalking();
            }

            if (movement != null)
            {
                movement.SetStationary(false);
                movement.SetScriptedOverride(true);
                movement.SetScriptedRunMode(true);
                movement.ClearIdleState();

                // 提升跑步速度以便快速到达目标
                savedRunSpeed = movement.runSpeed;
                movement.runSpeed = SWEEP_RUN_SPEED;
            }

            SetInteractionLocked(true);
        }

        private void RestoreDefaultState()
        {
            CacheReferences();
            bool shouldStationary = mod != null && (mod.IsModeEActive || mod.IsModeFActive);

            if (movement != null)
            {
                // 恢复原始跑步速度
                if (savedRunSpeed >= 0f)
                {
                    movement.runSpeed = savedRunSpeed;
                    savedRunSpeed = -1f;
                }

                movement.StopMove();
                movement.ClearIdleState();
                movement.SetScriptedRunMode(false);
                movement.SetScriptedOverride(false);
                movement.SetStationary(shouldStationary);
            }

            if (controller != null)
            {
                controller.SetStationary(shouldStationary);
                if (shouldStationary)
                {
                    controller.StartTalking(false);
                }
                else
                {
                    controller.StopTalking();
                }
            }

            SetInteractionLocked(false);
        }

        private bool DestroyTargetLootbox(AwenLootSweepTarget target)
        {
            if (target == null)
            {
                return false;
            }

            InteractableLootbox lootbox = target.Lootbox;
            if (lootbox == null || lootbox.gameObject == null)
            {
                return false;
            }

            UnityEngine.Object.Destroy(lootbox.gameObject);
            return true;
        }

        private bool IsCloseEnough(Vector3 targetPos)
        {
            Vector3 delta = targetPos - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= ARRIVE_DISTANCE_SQR;
        }

        private void TeleportTo(Vector3 targetPos)
        {
            try
            {
                if (movement != null)
                {
                    movement.StopMove();
                }

                CharacterController cc = GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.enabled = false;
                }

                transform.position = targetPos;

                if (cc != null)
                {
                    cc.enabled = true;
                }
            }
            catch { }
        }

        private void ShowSweepBubble(int clearedCount)
        {
            try
            {
                DialogueBubblesManager.Show(
                    L10n.T("爽吃x" + clearedCount, "Nom x" + clearedCount),
                    transform,
                    1.5f,
                    false,
                    false,
                    -1f,
                    1.5f);
            }
            catch { }
        }

        private void SetInteractionLocked(bool locked)
        {
            InteractableBase[] interactables = GetComponentsInChildren<InteractableBase>(true);
            if (interactables == null)
            {
                return;
            }

            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableBase interactable = interactables[i];
                if (interactable == null)
                {
                    continue;
                }

                interactable.enabled = !locked;
            }
        }

        private void CacheReferences()
        {
            if (mod == null)
            {
                mod = ModBehaviour.Instance;
            }

            if (movement == null)
            {
                movement = GetComponent<CourierMovement>();
            }

            if (controller == null)
            {
                controller = GetComponent<CourierNPCController>();
            }
        }
    }
}
