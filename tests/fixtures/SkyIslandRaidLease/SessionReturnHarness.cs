using UnityEngine.SceneManagement;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace BossRush
{
    // DispatchReturnIfReady 原样抽取生产方法；只替代会话其余 UI / 物理清理，资源仍走真实租约。
    internal sealed partial class SkyIslandSession
    {
        private SkyIslandRaidLease lease;
        private Scene entryScene;
        private bool moved, closed;
        private StoryBoundary story;
        private GameObject root;
        private bool loadStarted;
        private string assemblyError;
        private readonly HashSet<string> tickFaults = new HashSet<string>(StringComparer.Ordinal);
        internal IEnumerator NavigationWait() { return WaitForNavigationOwner(); }
        internal IEnumerator LootWarm() { return PrewarmLootGuarded(); }
        internal IEnumerator BuildWarm() { return DriveEntryPrewarm(); }
        internal void CancelEntry() { ReleaseEntryPrewarm(); }
        internal IEnumerator EntryWait() { return WaitForEntryActivation(); }
        internal void BindWorld() { SceneManager.LoadRaid(); root = new GameObject("World"); entryScene = SceneManager.Raid; }
        internal bool ResidentsUnavailable { get { return residentsFailed; } }
        internal int FaultCount { get { return tickFaults.Count; } }
        internal void Optional(Action action, Action release) { EntryStep("optional", action, release); }
        internal bool Closed { get { return closed; } }
        internal int ReleaseCallbacks;
        internal SkyIslandSession(SkyIslandRaidLease value) { lease = value; }
        internal void TickReturn() { if (!closed) DispatchReturnIfReady(); }
        private void Cleanup(string reason)
        {
            closed = true;
            lease.Release(() => ReleaseCallbacks++);
        }
        private sealed class StoryBoundary
        {
            internal void SettleRaidHeld(bool value) { }
            internal void Tick(bool value) { }
        }
    }
}
