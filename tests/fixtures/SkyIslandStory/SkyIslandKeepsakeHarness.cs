using System;

namespace BossRush
{
    // Story 专用：执行生产 GrantKeepsakes 和 Tick 重试语句；不被链接共享 Stubs.cs 的判据夹具带入。
    internal sealed partial class SkyIslandKeepsakeHarness
    {
        private readonly SkyIslandStoryService story;
        private readonly KeepsakeSessionStub session = new KeepsakeSessionStub();
        internal SkyIslandKeepsakeHarness(SkyIslandStoryService value) { story = value; }
        internal void GrantForTest() { GrantKeepsakes(); }
        internal int Announcements { get { return session.Announcements; } }
        private sealed class KeepsakeSessionStub
        {
            internal int Announcements;
            internal void Announce(string text, bool warning) { Announcements++; }
        }
    }

    // 只模拟 prefab 尚未就绪和投递成功。真实官方归属/异常/缓冲回执由 SkyIslandDelivery 验证。
    internal static class SkyIslandItems
    {
        internal static bool MissingResources;
        internal static int Attempts, Delivered;
        internal static bool TryGiveWithReceipt(int typeId, bool toStorage, Func<bool> recordGrant,
            Func<bool> rollbackGrant, out bool buffered)
        {
            Attempts++;
            buffered = false;
            if (MissingResources || recordGrant == null || !recordGrant()) return false;
            PlayerStorage.IncomingItemBuffer.Add(new ItemStatsSystem.Data.ItemTreeData { RootTypeID = typeId });
            buffered = true;
            Delivered++;
            return true;
        }
    }
}
