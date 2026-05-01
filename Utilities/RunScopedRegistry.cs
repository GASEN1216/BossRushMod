// ============================================================================
// RunScopedRegistry.cs - 局生命周期对象注册表的通用迭代/清理 helper
// ============================================================================
// 模块说明：
//   ZombieMode 的 RunOnlyObjects 列表（按"局"管理协程 / 事件订阅 / 临时
//   GameObject / 临时 NPC / 状态修饰）证明这种模式是有效的，但其它模式
//   （ModeD / ModeE / ModeF）目前还在用各自零散的字段做生命周期清理。
//   本 helper 把"反向迭代 + try/catch 包裹 + 错误回调"的样板抽出来，
//   让其它模式接入时只需自带 record 类型 + 清理 lambda。
//
// 设计要点：
//   - 不约束 record 类型：调用方传 IList<TRecord>，helper 用 reverse 顺序
//     遍历（保证清理动作如果在过程中 Remove 自己也安全）。
//   - 不调用 .Clear()：让调用方决定是否在 helper 调用后清空列表，避免
//     guard / 调试日志看不见 records.Clear() 这一行。
//   - 异常通过 onError 回调回报，helper 内部不静默吞。
// ============================================================================

namespace BossRush
{
    internal static class RunScopedRegistry
    {
        /// <summary>
        /// 反向迭代 records，对每个非 null 元素执行 action；任何异常通过 onError 回报。
        /// records 不会被 Clear，调用方按需自行处理。
        /// </summary>
        internal static void ForEachReverse<TRecord>(
            System.Collections.Generic.IList<TRecord> records,
            System.Action<TRecord> action,
            System.Action<System.Exception, TRecord> onError = null)
            where TRecord : class
        {
            if (records == null || action == null)
            {
                return;
            }

            for (int i = records.Count - 1; i >= 0; i--)
            {
                TRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                try
                {
                    action(record);
                }
                catch (System.Exception e)
                {
                    if (onError != null)
                    {
                        try { onError(e, record); }
                        catch { }
                    }
                }
            }
        }
    }
}
