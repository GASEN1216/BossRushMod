using System;
using System.Collections.Generic;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    public enum BossRushTrackedLootboxMode
    {
        None = 0,
        ModeE = 1,
        ModeF = 2
    }

    internal sealed class AwenLootSweepTarget
    {
        public InteractableLootbox Lootbox;
        public Vector3 VisitPosition;
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const int MODEEF_SWEEP_TOKEN_GRANT_INTERVAL = 20;
        private const float AWEN_SWEEP_TARGET_CACHE_REFRESH_INTERVAL = 0.2f;
        private const float AWEN_SWEEP_FAILURE_BUBBLE_Y_OFFSET = 2.5f;
        private const float AWEN_SWEEP_FAILURE_BUBBLE_DURATION = 3f;
        private const float AWEN_SWEEP_FAILURE_BUBBLE_COOLDOWN = 1.25f;

        private int modeEFBossDeathGrantCounter = 0;
        private float nextAwenSweepFailureBubbleTime = 0f;
        private float nextAwenSweepTargetCacheRefreshTime = 0f;
        private string lastAwenSweepFailureBubbleText = string.Empty;
        private readonly List<InteractableLootbox> modeEFLootboxScratch = new List<InteractableLootbox>();
        private readonly List<AwenLootSweepTarget> awenLootSweepTargetScratch = new List<AwenLootSweepTarget>();
        private readonly List<AwenLootSweepTarget> cachedAwenLootSweepTargets = new List<AwenLootSweepTarget>();
        private AwenLootSweepRunner awenLootSweepRunner = null;

        private bool TryGetActiveModeEFLootboxContext(out BossRushTrackedLootboxMode mode, out int sessionToken)
        {
            mode = BossRushTrackedLootboxMode.None;
            sessionToken = 0;

            if (modeFActive && modeFState.IsActive && CurrentModeFSessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeF;
                sessionToken = CurrentModeFSessionToken;
                return true;
            }

            if (modeEActive && CurrentModeESessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeE;
                sessionToken = CurrentModeESessionToken;
                return true;
            }

            return false;
        }

        private void ResetModeEFLootboxTrackerState()
        {
            CancelAwenLootSweep(true);
            CourierPaidLootSweepService.ReleasePendingSweepResultToPlayer(true, false);
            modeEFBossDeathGrantCounter = 0;
            nextAwenSweepFailureBubbleTime = 0f;
            lastAwenSweepFailureBubbleText = string.Empty;
            modeEFLootboxScratch.Clear();
            awenLootSweepTargetScratch.Clear();
            InvalidateAwenLootSweepTargetCache();
        }

        internal void CaptureModeEFLootboxBaseline()
        {
            modeEFBossDeathGrantCounter = 0;
            nextAwenSweepFailureBubbleTime = 0f;
            lastAwenSweepFailureBubbleText = string.Empty;
            modeEFLootboxScratch.Clear();
            awenLootSweepTargetScratch.Clear();
            InvalidateAwenLootSweepTargetCache();
        }

        internal bool CanUseAwenLootSweepInCurrentMode()
        {
            return (IsActive && !IsModeDActive) || (IsBossRushArenaActive && !IsModeDActive) || modeEActive || modeFActive;
        }

        internal void InvalidateAwenLootSweepTargetCache()
        {
            nextAwenSweepTargetCacheRefreshTime = 0f;
            cachedAwenLootSweepTargets.Clear();
        }

        internal int GetCurrentAwenLootSweepTargetCount()
        {
            return EnsureAwenLootSweepTargetCache();
        }

        internal int CopyCurrentAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        {
            if (output == null)
            {
                return 0;
            }

            output.Clear();
            EnsureAwenLootSweepTargetCache();
            for (int i = 0; i < cachedAwenLootSweepTargets.Count; i++)
            {
                AwenLootSweepTarget source = cachedAwenLootSweepTargets[i];
                if (source == null || source.Lootbox == null)
                {
                    continue;
                }

                AwenLootSweepTarget copy = new AwenLootSweepTarget();
                copy.Lootbox = source.Lootbox;
                copy.VisitPosition = source.VisitPosition;
                output.Add(copy);
            }

            return output.Count;
        }

        internal int CopyFreshAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        {
            if (output == null)
            {
                return 0;
            }

            output.Clear();
            if (!CanUseAwenLootSweepInCurrentMode() || courierNPCInstance == null)
            {
                return 0;
            }

            if (CollectTrackedModeEFLootboxes(modeEFLootboxScratch, int.MinValue) <= 0)
            {
                return 0;
            }

            BuildAwenLootSweepTargets(modeEFLootboxScratch, output);
            return output.Count;
        }

        internal void NotifyAwenLootSweepRunnerDestroyed(AwenLootSweepRunner runner)
        {
            if (object.ReferenceEquals(awenLootSweepRunner, runner))
            {
                awenLootSweepRunner = null;
            }
        }

        internal bool IsAwenLootSweepSessionStillValid(BossRushTrackedLootboxMode mode, int sessionToken, int relatedScene)
        {
            switch (mode)
            {
                case BossRushTrackedLootboxMode.ModeE:
                    return IsModeESessionStillValid(sessionToken, relatedScene);
                case BossRushTrackedLootboxMode.ModeF:
                    return IsModeFSessionStillValid(sessionToken, relatedScene);
                default:
                    return false;
            }
        }

        internal void TryRegisterModeEFLootbox(InteractableLootbox lootbox)
        {
            BossRushTrackedLootboxMode mode;
            int sessionToken;
            if (!TryGetActiveModeEFLootboxContext(out mode, out sessionToken))
            {
                return;
            }

            int activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            if (!IsAwenLootSweepSessionStillValid(mode, sessionToken, activeScene) ||
                !BossRushLootboxUtility.IsMarkedLootbox(lootbox, activeScene))
            {
                return;
            }

            BossRushLootboxUtility.StampLootboxMarker(lootbox, mode, sessionToken);
            InvalidateAwenLootSweepTargetCache();
        }

        internal void RegisterModeEFBossDeathForSweepToken()
        {
            try
            {
                BossRushTrackedLootboxMode mode;
                int sessionToken;
                if (!TryGetActiveModeEFLootboxContext(out mode, out sessionToken))
                {
                    return;
                }

                int activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                if (!IsAwenLootSweepSessionStillValid(mode, sessionToken, activeScene))
                {
                    return;
                }

                modeEFBossDeathGrantCounter++;
                DevLog("[AwenLootSweep] 模式E/F Boss死亡累计: " + modeEFBossDeathGrantCounter);

                if (modeEFBossDeathGrantCounter % MODEEF_SWEEP_TOKEN_GRANT_INTERVAL == 0)
                {
                    GrantAwenLootSweepToken();
                }
            }
            catch (Exception e)
            {
                DevLog("[AwenLootSweep] [WARNING] RegisterModeEFBossDeathForSweepToken failed: " + e.Message);
            }
        }

        internal bool CanUseAwenLootSweepToken()
        {
            return CanUseAwenLootSweepToken(null, false);
        }

        internal bool CanUseAwenLootSweepToken(CharacterMainControl player, bool showFailureFeedback)
        {
            if (!CanUseAwenLootSweepInCurrentMode())
            {
                return ReportAwenLootSweepFailure(
                    player,
                    showFailureFeedback,
                    L10n.T(
                        "现在还不能叫阿稳扫箱，得在可用的BossRush战场里用。",
                        "You can only call Awen to sweep in supported BossRush modes."));
            }

            if (courierNPCInstance == null || courierController == null)
            {
                return ReportAwenLootSweepFailure(
                    player,
                    showFailureFeedback,
                    L10n.T(
                        "阿稳现在不在，没人帮你扫箱。",
                        "Awen isn't here right now."));
            }

            AwenLootSweepRunner runner = courierNPCInstance.GetComponent<AwenLootSweepRunner>();
            if (runner != null && runner.IsRunning)
            {
                return ReportAwenLootSweepFailure(
                    player,
                    showFailureFeedback,
                    L10n.T(
                        "阿稳已经开吃了，等他扫完再用。",
                        "Awen is already sweeping. Let him finish first."));
            }

            int freshTargetCount = CopyFreshAwenLootSweepTargets(awenLootSweepTargetScratch);

            // 与垃圾桶“清空所有箱子”保持一致：不按 active scene buildIndex 过滤，
            // 只要当前已加载场景里带 BossRushLootboxMarker 就算可扫目标。
            if (freshTargetCount <= 0)
            {
                return ReportAwenLootSweepFailure(
                    player,
                    showFailureFeedback,
                    L10n.T(
                        "现在还没有箱子可扫。",
                        "There aren't any lootboxes to clear right now."));
            }

            return true;
        }

        internal bool TryActivateAwenLootSweepToken(CharacterMainControl player)
        {
            if (!CanUseAwenLootSweepToken(player, true))
            {
                return false;
            }

            AwenLootSweepRunner runner = GetOrCreateAwenLootSweepRunner();
            if (runner == null)
            {
                ReportAwenLootSweepFailure(player, true, L10n.T(
                    "阿稳这会儿腾不开手，稍后再试。",
                    "Awen can't start sweeping right now. Try again in a moment."));
                return false;
            }

            BossRushTrackedLootboxMode mode = BossRushTrackedLootboxMode.None;
            int sessionToken = 0;
            TryGetActiveModeEFLootboxContext(out mode, out sessionToken);

            if (!AwenLootSweepTokenConfig.EnsureRuntimeRegistration())
            {
                ReportAwenLootSweepFailure(player, true, L10n.T(
                    "这张扫箱令这次没起作用，稍后再试一次。",
                    "The sweep token didn't take effect. Try again in a moment."));
                return false;
            }

            int targetCount = CopyFreshAwenLootSweepTargets(awenLootSweepTargetScratch);
            DevLog("[AwenLootSweep] 当前已标记箱子数量=" + targetCount);

            if (targetCount <= 0)
            {
                ReportAwenLootSweepFailure(player, true, L10n.T(
                    "现在还没有箱子可扫。",
                    "There aren't any lootboxes to clear right now."));
                return false;
            }

            int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            if (!runner.BeginSweep(awenLootSweepTargetScratch, mode, sessionToken, relatedScene))
            {
                ReportAwenLootSweepFailure(player, true, L10n.T(
                    "阿稳这会儿腾不开手，稍后再试。",
                    "Awen can't start sweeping right now. Try again in a moment."));
                return false;
            }

            ShowBigBanner(L10n.T(
                "<color=yellow>阿稳扫箱令</color>已生效！阿稳将处理 <color=yellow>" + awenLootSweepTargetScratch.Count + "</color> 个掉落箱。",
                "<color=yellow>Awen Loot Sweep Token</color> activated! Awen will clear <color=yellow>" + awenLootSweepTargetScratch.Count + "</color> lootboxes."));
            return true;
        }

        private bool ReportAwenLootSweepFailure(CharacterMainControl player, bool showFailureFeedback, string message)
        {
            if (showFailureFeedback)
            {
                ShowAwenLootSweepFailureBubble(player, message);
            }

            return false;
        }

        private void ShowAwenLootSweepFailureBubble(CharacterMainControl player, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Transform target = null;
            try
            {
                CharacterMainControl bubblePlayer = player ?? CharacterMainControl.Main;
                if (bubblePlayer != null)
                {
                    target = bubblePlayer.transform;
                }
            }
            catch (Exception targetEx)
            {
                DevLog("[AwenLootSweep] [WARNING] 获取扫箱失败气泡目标失败: " + targetEx.Message);
            }

            if (target != null)
            {
                try
                {
                    float now = Time.unscaledTime;
                    if (message == lastAwenSweepFailureBubbleText && now < nextAwenSweepFailureBubbleTime)
                    {
                        return;
                    }

                    lastAwenSweepFailureBubbleText = message;
                    nextAwenSweepFailureBubbleTime = now + AWEN_SWEEP_FAILURE_BUBBLE_COOLDOWN;
                    DialogueBubblesManager.Show(
                        message,
                        target,
                        AWEN_SWEEP_FAILURE_BUBBLE_Y_OFFSET,
                        false,
                        false,
                        -1f,
                        AWEN_SWEEP_FAILURE_BUBBLE_DURATION);
                    return;
                }
                catch (Exception bubbleEx)
                {
                    DevLog("[AwenLootSweep] [WARNING] 显示扫箱失败气泡失败: " + bubbleEx.Message);
                }
            }

            ShowMessage(message);
        }

        private void CancelAwenLootSweep(bool restoreDefaultState)
        {
            if (awenLootSweepRunner != null)
            {
                awenLootSweepRunner.CancelSweep(restoreDefaultState);
            }
        }

        private AwenLootSweepRunner GetOrCreateAwenLootSweepRunner()
        {
            if (courierNPCInstance == null)
            {
                return null;
            }

            if (awenLootSweepRunner == null)
            {
                awenLootSweepRunner = courierNPCInstance.GetComponent<AwenLootSweepRunner>();
                if (awenLootSweepRunner == null)
                {
                    awenLootSweepRunner = courierNPCInstance.AddComponent<AwenLootSweepRunner>();
                }
            }

            return awenLootSweepRunner;
        }

        private int CollectCurrentAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        {
            return CopyCurrentAwenLootSweepTargets(output);
        }

        private bool HasTrackedModeEFLootboxes(int requiredSceneBuildIndex)
        {
            if (requiredSceneBuildIndex == int.MinValue)
            {
                return GetCurrentAwenLootSweepTargetCount() > 0;
            }

            return CollectTrackedModeEFLootboxes(modeEFLootboxScratch, requiredSceneBuildIndex) > 0;
        }

        private int EnsureAwenLootSweepTargetCache()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextAwenSweepTargetCacheRefreshTime)
            {
                RefreshAwenLootSweepTargetCache();
            }

            return cachedAwenLootSweepTargets.Count;
        }

        private void RefreshAwenLootSweepTargetCache()
        {
            nextAwenSweepTargetCacheRefreshTime = Time.realtimeSinceStartup + AWEN_SWEEP_TARGET_CACHE_REFRESH_INTERVAL;
            cachedAwenLootSweepTargets.Clear();

            if (!CanUseAwenLootSweepInCurrentMode() || courierNPCInstance == null)
            {
                return;
            }

            if (CollectTrackedModeEFLootboxes(modeEFLootboxScratch, int.MinValue) <= 0)
            {
                return;
            }

            BuildAwenLootSweepTargets(modeEFLootboxScratch, cachedAwenLootSweepTargets);
        }

        private int CollectTrackedModeEFLootboxes(List<InteractableLootbox> output, int requiredSceneBuildIndex)
        {
            if (output == null)
            {
                return 0;
            }

            output.Clear();
            return BossRushLootboxUtility.CollectMarkedLootboxes(output, requiredSceneBuildIndex);
        }

        private void BuildAwenLootSweepTargets(List<InteractableLootbox> source, List<AwenLootSweepTarget> output)
        {
            if (source == null || output == null)
            {
                return;
            }

            output.Clear();
            if (courierNPCInstance == null)
            {
                return;
            }

            Vector3 currentPos = courierNPCInstance.transform.position;
            bool[] used = new bool[source.Count];

            while (true)
            {
                int bestIndex = -1;
                float bestDistanceSqr = float.MaxValue;

                for (int i = 0; i < source.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    InteractableLootbox candidate = source[i];
                    if (!BossRushLootboxUtility.IsMarkedLootbox(candidate))
                    {
                        continue;
                    }

                    Vector3 candidatePos = candidate.transform.position;
                    float distSqr = (candidatePos - currentPos).sqrMagnitude;
                    if (distSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distSqr;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                used[bestIndex] = true;

                InteractableLootbox lootbox = source[bestIndex];
                if (!BossRushLootboxUtility.IsMarkedLootbox(lootbox))
                {
                    continue;
                }

                Vector3 targetPos = lootbox.transform.position;
                output.Add(new AwenLootSweepTarget
                {
                    Lootbox = lootbox,
                    VisitPosition = targetPos
                });

                currentPos = targetPos;
            }
        }

        private void GrantAwenLootSweepToken()
        {
            try
            {
                if (!AwenLootSweepTokenConfig.EnsureRuntimeRegistration())
                {
                    DevLog("[AwenLootSweep] [WARNING] Failed to register the sweep token before auto grant");
                    return;
                }

                bool granted = TryGiveItemToPlayerOrDrop(
                    AwenLootSweepTokenConfig.TYPE_ID,
                    AwenLootSweepTokenConfig.GetDisplayName(),
                    false,
                    true);

                if (!granted)
                {
                    DevLog("[AwenLootSweep] [WARNING] Failed to auto grant the sweep token");
                    return;
                }

                ShowBigBanner(L10n.T(
                    "累计 <color=yellow>" + modeEFBossDeathGrantCounter + "</color> 个Boss死亡！获得 <color=yellow>阿稳扫箱令</color> ×1",
                    "Reached <color=yellow>" + modeEFBossDeathGrantCounter + "</color> boss deaths! Received <color=yellow>Awen Loot Sweep Token</color> x1"));
            }
            catch (Exception e)
            {
                DevLog("[AwenLootSweep] [WARNING] GrantAwenLootSweepToken failed: " + e.Message);
            }
        }

        internal bool TryRefundAwenLootSweepToken()
        {
            try
            {
                try
                {
                    AwenLootSweepTokenConfig.EnsureRuntimeRegistration();
                }
                catch (Exception registerEx)
                {
                    DevLog("[AwenLootSweep] [WARNING] 退款前注册扫箱令失败: " + registerEx.Message);
                }

                bool refunded = TryGiveItemToPlayerOrDrop(
                    AwenLootSweepTokenConfig.TYPE_ID,
                    AwenLootSweepTokenConfig.GetDisplayName(),
                    false,
                    true);

                if (refunded)
                {
                    ShowMessage(L10n.T(
                        "扫箱令未生效，物品已返还。",
                        "The sweep token didn't take effect. The item was refunded."));
                    return true;
                }
            }
            catch (Exception e)
            {
                DevLog("[AwenLootSweep] [WARNING] TryRefundAwenLootSweepToken failed: " + e.Message);
            }

            ShowMessage(L10n.T(
                "扫箱令未生效，但返还失败，请查看日志。",
                "The sweep token didn't take effect, but the refund failed. Check the log."));
            return false;
        }
    }
}
