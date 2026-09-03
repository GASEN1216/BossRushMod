using System;
using System.Collections;
using System.Diagnostics;
using Duckov.Economy;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // 只从 Dev 专用测试档 runner 调用。准备期与撤离 UI 走生产入口，事件后核对真实钱包和清理。
        internal bool ValidationCompleteZombieExtraction(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            if (!DevModeEnabled || !IsZombieModeActive) { reason = "zombie_not_active"; return false; }
            int runId = zombieModeRunState.RunId;
            // 正常初始净化点为 0。用生产拾取入口准备正数结算样本，不能改玩法初始值或跳过现金断言。
            int pointsBeforePickup = zombieModeRunState.PurificationPoints;
            CollectZombieModePurificationPoint(runId, 3, null, null);
            if (zombieModeRunState.PurificationPoints != pointsBeforePickup + 3)
            { reason = "purification_pickup_not_applied"; return false; }
            BeginZombieModeExtractionOpportunity(runId);
            StartZombieModeExtractionFromUi(runId);
            CountDownArea area = zombieModeRunState.ActiveExtractionArea;
            if (area == null || !zombieModeRunState.ExtractionChanneling)
            { reason = "extraction_area_or_channel_missing"; return false; }
            long points = zombieModeRunState.PurificationPoints;
            if (points <= 0) { reason = "no_points_to_verify_settlement"; return false; }
            long before = EconomyManager.Money;
            var succeed = area.onCountDownSucceed;
            if (succeed == null) { reason = "extraction_event_missing"; return false; }
            succeed.Invoke();
            long first = EconomyManager.Money;
            // 同一事件重复派发不能重复结算；也覆盖丧尸成功回调中的嵌套 Invoke。
            succeed.Invoke();
            long second = EconomyManager.Money;
            bool settled = first - before == points && second == first && !IsZombieModeActive;
            metrics = "pickup=" + pointsBeforePickup + "->" + points + ",points=" + points
                + ",cash=" + before + "->" + first + "->" + second
                + ",active=" + IsZombieModeActive;
            if (!settled) reason = "extraction_settlement_or_idempotency_failed";
            return settled;
        }
    }

    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunZombieExtraction()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            string metrics = string.Empty;
            try
            {
                if (!_host.ValidationStartZombie(out reason))
                { Record("MODE_ZOMBIE_EXTRACTION", "FAIL", sw.ElapsedMilliseconds, metrics, reason); yield break; }
                yield return WaitSeconds(6f);
                if (ShouldAbort())
                { Record("MODE_ZOMBIE_EXTRACTION", "SKIP", sw.ElapsedMilliseconds, metrics, DescribeAbortReason()); yield break; }
                bool settled = _host.ValidationCompleteZombieExtraction(out metrics, out reason);
                float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
                while (settled && !IsRuntimeReady(BaseSceneNameForValidation()) && !ShouldAbort()
                    && Time.realtimeSinceStartup < deadline) yield return null;
                bool ready = IsRuntimeReady(BaseSceneNameForValidation());
                Record("MODE_ZOMBIE_EXTRACTION", settled && ready ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    metrics + ",base_ready=" + ready, reason ?? (ready ? null : "extraction_base_not_ready"));
            }
            finally { _host.ValidationSafeCleanup(); }
        }
    }
}
