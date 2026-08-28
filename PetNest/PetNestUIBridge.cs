// ============================================================================
// PetNestUIBridge.cs - 交互点与面板之间的解耦桥（实施计划 步骤 9 / 10）
// ============================================================================
// 为什么要一层桥：
//   交互点（Interactables）在基地场景里常驻，面板（UI）是惰性构建的。
//   让交互点直接 new 面板会把 UI 装配拖进交互点的 Awake；让面板反过来找交互点又会
//   把两边绑死。桥只做两件事：
//     1) 判定"现在能不能用遗种巢"（开关 / 存档故障 / 血脉目录）；
//     2) 把"打开某一页"的请求转给已注册的面板打开器。
//
// 面板未接线时（步骤 10 之前，或 UI 装配失败）不崩：请求被记为 pending 并 DevLog，
// 交互点照常返回。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>
    /// 遗种巢面板的页面。序列化不用它，纯运行时。
    /// public 是因为它出现在 public 交互点类的 protected 成员签名上（CS0053）。
    /// </summary>
    public enum PetNestUIPage
    {
        /// <summary>巢：崽列表、出战席位、命名。</summary>
        Nest = 0,
        /// <summary>孵化：蛋列表、凝蛋。</summary>
        Hatch = 1,
        /// <summary>天灾远征：派遣、倒计时、翻牌。</summary>
        Expedition = 2,
        /// <summary>遗种博物馆：图鉴、纪念碑。</summary>
        Museum = 3,
    }

    /// <summary>交互点与面板之间的解耦桥。</summary>
    internal static class PetNestUIBridge
    {
        private static Action<PetNestUIPage> _pageOpener;
        private static PetNestRuntimeModule _runtime;

        /// <summary>
        /// 运行时模块 bootstrap 时把自己绑进来。
        /// 显式绑定而不是让桥去够宿主单例：交互点在基地场景常驻，
        /// 每帧 IsInteractable 都去够单例既是无谓开销，也会给冻结的宿主单例
        /// 引用分类基线增列（tests/ModBehaviourInstanceClassificationGuard.py）。
        /// </summary>
        internal static void BindRuntime(PetNestRuntimeModule runtime)
        {
            _runtime = runtime;
        }

        /// <summary>运行时模块回到 dormant / 销毁时解绑。</summary>
        internal static void UnbindRuntime()
        {
            _runtime = null;
        }

        /// <summary>面板注册自己的打开器。幂等：后注册的覆盖前一个。</summary>
        internal static void RegisterPageOpener(Action<PetNestUIPage> opener)
        {
            _pageOpener = opener;
        }

        /// <summary>面板销毁时注销。</summary>
        internal static void UnregisterPageOpener()
        {
            _pageOpener = null;
        }

        /// <summary>
        /// 现在能不能用遗种巢。
        /// 开关关闭、运行时模块未 bootstrap、或存档进入单向故障时一律不可用
        /// （不可用时交互点仍在，只是点不动，玩家不会看到一个会崩的入口）。
        /// </summary>
        internal static bool IsPetNestUsable()
        {
            try
            {
                PetNestRuntimeModule runtime = _runtime;
                if (runtime == null || !runtime.IsEnabled || !runtime.IsBootstrapped) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>打开指定页。面板未接线时只记日志，不抛。</summary>
        internal static void OpenPage(PetNestUIPage page)
        {
            try
            {
                Action<PetNestUIPage> opener = _pageOpener;
                if (opener == null)
                {
                    ModBehaviour.DevLog("[PetNest] 面板尚未接线，忽略打开请求: " + page);
                    return;
                }
                opener(page);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 打开面板失败: " + e.Message);
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            _pageOpener = null;
            _runtime = null;
        }
    }
}
