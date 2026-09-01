// ============================================================================
// AffixForgeHostCleanup.cs - 词缀锻造的宿主销毁清理入口
// ============================================================================
// 为什么单独一个文件：
//   ModBehaviour.OnDestroy 已经很长，把各系统的清理内联进去有两个坏处——
//   一是可读性，二是 StaticCacheLifecycleGuard 判断「某个 ResetStaticCaches
//   是否在 OnDestroy 路径上」时只向前回溯有限字符找所属方法声明，链条太长会让
//   末尾的调用被误判成漏清理。因此按仓库既有约定（CleanupAchievementRuntime /
//   CleanupIntegrationRuntimeOnDestroy 等）收口成具名方法。
//
// 顺序是硬约束：
//   先 ShutdownRuntime 退订三组静态事件（Health 伤害/死亡、手持变化、槽位变化），
//   再清 Buff 工厂缓存的运行时 GameObject，最后清词缀表缓存。
//   顺序颠倒会出现「事件还挂着但缓存已空」的窗口，卸载 Mod 时可能空引用。
// ============================================================================

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>词缀锻造的宿主销毁清理。幂等，异常经 SafeRuntime 自吞不拖崩宿主。</summary>
        internal void CleanupAffixForgeRuntimeOnDestroy()
        {
            SafeRuntime.Run("AffixRuntimeService.ShutdownRuntime", () => AffixRuntimeService.ShutdownRuntime());
            SafeRuntime.Run("AffixRuntimeService.ResetStaticCaches", () => AffixRuntimeService.ResetStaticCaches());
            // 掉落轨的 per-character 订阅必须与运行时服务同批退订，否则宿主重建后
            // 旧 handler 仍挂在未回收的 Boss 上。
            SafeRuntime.Run("AffixForgeStoneDropService.ResetStaticCaches", () => AffixForgeStoneDropService.ResetStaticCaches());
            SafeRuntime.Run("AffixBuffFactory.ResetStaticCaches", () => AffixBuffFactory.ResetStaticCaches());
            SafeRuntime.Run("AffixDefinitions.ResetStaticCaches", () => AffixDefinitions.ResetStaticCaches());
        }
    }
}
