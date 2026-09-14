namespace BossRush
{
    /// <summary>
    /// 写入门替身。真实实现在 DebugAndTools/F3GameplayValidationAutotest.cs：Dev 构建 + 专用测试档 + 这一轮自动验收正在跑（或正在崩溃恢复），
    /// 读的全是 Unity 侧状态（Runner 单例、当前槽位、测试档标记），离线造不出来，所以只留一个静态开关。
    /// 默认关闭并给出理由，模拟「不是专用测试档」：夹具必须显式打开才写得进去。
    /// </summary>
    internal sealed partial class F3GameplayValidationRunner
    {
        internal const string ClosedReason = "dedicated_test_slot_required";
        internal static bool AutotestWritesOpen;

        internal static bool AutotestWriteAllowed(out string reason)
        {
            reason = AutotestWritesOpen ? null : ClosedReason;
            return AutotestWritesOpen;
        }
    }
}
