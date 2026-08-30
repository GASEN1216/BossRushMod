// ============================================================================
// AffixRuntimeTicker.cs - 词缀的每帧驱动器（当前只有「死契」的持续流失需要）
// ============================================================================
// 模块职责：
//   只做一件事：每帧把 Time.deltaTime 转交给 AffixRuntimeService.TickDrain。
//
// 硬约束（AGENTS 4.12 重运行时工作按实际使用状态门控）：
//   1. **只有 Tick 类词缀真的激活时才存在这个对象**。
//      创建/销毁由 AffixRuntimeService.SyncTicker 统一负责，本文件不自作主张。
//      离手 / 卸下 / 主角死亡 / 切图 / Mod 卸载都会走到 SyncTicker → DestroyInstance。
//   2. Update 是每帧热路径：零日志、零分配、零 GetComponent。
//      异常在 TickDrain 内部自吞，本文件不再套第二层 try/catch 以免多一层栈开销。
//   3. 本对象 DontDestroyOnLoad，切场景不会自动回收，必须靠 DestroyInstance 显式销毁。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 词缀每帧驱动器。生命周期完全由 AffixRuntimeService 托管。
    /// </summary>
    internal sealed class AffixRuntimeTicker : MonoBehaviour
    {
        private const string TickerObjectName = "BossRush_AffixRuntimeTicker";

        private static AffixRuntimeTicker instance;

        /// <summary>幂等创建。已存在则直接返回现有实例。</summary>
        internal static AffixRuntimeTicker EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            try
            {
                GameObject host = new GameObject(TickerObjectName);
                Object.DontDestroyOnLoad(host);
                instance = host.AddComponent<AffixRuntimeTicker>();
                return instance;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] [ERROR] 创建词缀 Ticker 失败: " + e.Message);
                instance = null;
                return null;
            }
        }

        /// <summary>幂等销毁。未创建时安全空转。</summary>
        internal static void DestroyInstance()
        {
            if (instance == null)
            {
                instance = null;
                return;
            }

            try
            {
                GameObject host = instance.gameObject;
                instance = null;
                if (host != null)
                {
                    Object.Destroy(host);
                }
            }
            catch (System.Exception e)
            {
                instance = null;
                ModBehaviour.DevLog("[AffixForge] [WARNING] 销毁词缀 Ticker 失败: " + e.Message);
            }
        }

        /// <summary>每帧热路径：不写日志、不分配、不查组件。</summary>
        private void Update()
        {
            AffixRuntimeService.TickDrain(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }
        }
    }
}
