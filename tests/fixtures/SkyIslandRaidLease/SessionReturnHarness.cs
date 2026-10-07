using UnityEngine.SceneManagement;

namespace BossRush
{
    // DispatchReturnIfReady 原样抽取生产方法；只替代会话其余 UI / 物理清理，资源仍走真实租约。
    internal sealed partial class SkyIslandSession
    {
        private SkyIslandRaidLease lease;
        private Scene entryScene;
        private bool moved, closed;
        private StoryBoundary story;
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
