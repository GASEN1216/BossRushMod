using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>入场等待与可恢复步骤。核心碰撞、导航和官方关卡合同仍由 Build 验证。</summary>
    internal sealed partial class SkyIslandSession
    {
        private string firstReturnReason;
        private bool residentsFailed;
        private IEnumerator entryPrewarm;

        private void ReleaseEntryPrewarm()
        {
            IEnumerator pending = entryPrewarm;
            entryPrewarm = null;
            Safe("entry_prewarm", delegate { (pending as IDisposable)?.Dispose(); });
        }

        private void EntryStep(string step, Action action, Action release)
        {
            try { action(); }
            catch (Exception e)
            {
                TickFault(step, e);
                Safe(step + "_release", release);
            }
        }

        private IEnumerator WaitForNavigationOwner()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (global::AstarPath.active == null || global::AstarPath.active.data == null || global::AstarPath.active.isScanning)
            {
                if (lease.Error != null) throw new InvalidOperationException(lease.Error);
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException("天空岛导航服务未能就绪，或原场景扫描仍未结束");
                yield return null;
            }
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
        }

        private IEnumerator PrewarmLootGuarded()
        {
            IEnumerator warm = null;
            EntryStep("loot_prewarm", delegate { warm = SkyIslandLootPools.Prewarm(); }, delegate { });
            try
            {
                while (warm != null)
                {
                    bool more = false;
                    object current = null;
                    try { more = warm.MoveNext(); if (more) current = warm.Current; }
                    catch (Exception e) { TickFault("loot_prewarm", e); }
                    if (!more) yield break;
                    yield return current;
                }
            }
            finally { Safe("loot_prewarm_release", delegate { (warm as IDisposable)?.Dispose(); }); }
        }

        private void RememberReturnReason(string reason, string notice = null)
        {
            if (firstReturnReason != null) return;
            firstReturnReason = reason;
            Debug.LogWarning("[SkyIsland] RETURN_REASON " + reason);
            if (lease != null && notice != null) lease.SetFailureNotice(notice);
        }
    }
}
