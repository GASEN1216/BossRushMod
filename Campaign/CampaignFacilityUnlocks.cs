// ============================================================================
// CampaignFacilityUnlocks.cs - 战役 → 后山 的跨系统解锁契约（M0 骨架）
// ============================================================================
// 这是「鸭王征程」与「竞技场后山」之间唯一的耦合面。契约登记 docs/contracts.md。
//
// 【为什么是静态 API + 静态事件，而不是 SavesSystem key 互读】
//   两个系统在同一个 DLL 里，直接调用比让后山去猜战役的存档键健壮得多：
//   键名改一次两边就悄悄失联，而方法签名改一次编译期就报错。
//
// 【两条读法，缺一不可】
//   1. GetGrantedTokens() / IsTokenGranted()：权威查询。后山在自身 init 时
//      **必须**全量查一次——存档回放不会重放事件，只靠事件会漏掉所有历史解锁。
//   2. OnFacilityTokenGranted：仅在本会话实时授予时触发，用于即时弹提示、
//      当场注入建筑，免得玩家非要重进基地才看得见奖励。
//
// 【fail-closed】战役开关关闭或未 bootstrap 时，查询一律返回「未解锁」。
//   后山另有自己的 UnlockAll 调试开关可绕过，那是后山的事，不在本契约内。
//
// 【token 常量冻结】BossRush_Campaign_Unlock_Ch1..Ch6，发布后不得改名。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 战役设施解锁 token 的唯一权威。战役侧写，后山侧读。
    /// 全静态：token 集合在一次游戏会话内只有一份。
    /// </summary>
    internal static class CampaignFacilityUnlocks
    {
        #region 状态

        /// <summary>已授予的 token。权威副本由 CampaignPersistence 在读档时灌入。</summary>
        private static readonly HashSet<string> _granted = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 是否已由战役侧完成一次装载。未装载时查询一律 fail-closed，
        /// 避免「战役还没读档，后山先问，得到空答案就把设施藏了」的时序坑。
        /// </summary>
        private static bool _loaded;

        #endregion

        #region 事件

        /// <summary>
        /// token 实时授予事件。**只在本会话真正新授予时触发**，读档回放不重发。
        /// 订阅方必须幂等且在自身 shutdown 时退订（AGENTS.md 4.6）。
        /// </summary>
        internal static event Action<string> OnFacilityTokenGranted;

        #endregion

        #region 查询（后山侧使用）

        /// <summary>
        /// 权威查询：该 token 是否已解锁。no-throw。
        /// 战役未装载/未启用时返回 false（fail-closed）。
        /// </summary>
        internal static bool IsTokenGranted(string token)
        {
            try
            {
                if (!_loaded) return false;
                if (string.IsNullOrEmpty(token)) return false;
                return _granted.Contains(token);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 权威查询：已解锁 token 的快照。no-throw，永不返回 null。
        /// 返回副本而非内部集合，避免调用方持有可变引用。
        /// </summary>
        internal static IReadOnlyCollection<string> GetGrantedTokens()
        {
            try
            {
                if (!_loaded) return EmptyTokens;
                return new List<string>(_granted);
            }
            catch (Exception)
            {
                return EmptyTokens;
            }
        }

        private static readonly string[] EmptyTokens = new string[0];

        /// <summary>按章节号拼 token。章节号越界返回空串。</summary>
        internal static string BuildTokenForChapter(int chapter)
        {
            if (chapter < CampaignTuning.FirstChapter) return string.Empty;
            if (chapter > CampaignTuning.ChapterCount) return string.Empty;
            return CampaignTuning.FacilityTokenPrefix + chapter;
        }

        #endregion

        #region 写入（战役侧使用）

        /// <summary>
        /// 读档装载：用存档里的 token 集合整体替换内存态，并标记为已装载。
        /// **不触发事件**——读档不是新授予，后山靠自身 init 的全量查询拿历史解锁。
        /// 换槽时会再次调用，因此必须整体替换而不是追加。
        /// </summary>
        internal static void LoadGrantedTokens(IEnumerable<string> tokens)
        {
            try
            {
                _granted.Clear();
                if (tokens != null)
                {
                    foreach (string token in tokens)
                    {
                        if (string.IsNullOrEmpty(token)) continue;
                        _granted.Add(token);
                    }
                }
                _loaded = true;
            }
            catch (Exception e)
            {
                LogFailure("load", e);
            }
        }

        /// <summary>
        /// 实时授予一个 token。幂等：已授予时返回 false 且不重复发事件。
        /// 返回 true 表示这次确实是新授予（调用方据此决定是否落盘）。
        /// </summary>
        internal static bool TryGrant(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return false;
                if (!_loaded)
                {
                    // 没装载就授予会被下一次 LoadGrantedTokens 整体覆盖掉，
                    // 属于时序错误，明确拒绝而不是静默写进一个会被丢弃的集合。
                    LogWarning("尚未装载存档就尝试授予 token，已拒绝: " + token);
                    return false;
                }
                if (!_granted.Add(token)) return false;

                Action<string> handler = OnFacilityTokenGranted;
                if (handler != null)
                {
                    try
                    {
                        handler(token);
                    }
                    catch (Exception e)
                    {
                        // 订阅方异常绝不能回滚授予，也不能拖崩交付流程
                        LogFailure("notify", e);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                LogFailure("grant", e);
                return false;
            }
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 换槽/删档/宿主销毁时的复位。清集合并回到未装载态，
        /// 避免上一个档的解锁泄漏到新档（PetNest 曾踩过同类坑）。
        /// 事件订阅者不在这里清——退订是订阅方自己的责任（AGENTS.md 4.6）。
        /// </summary>
        internal static void ResetForSlotReload()
        {
            try
            {
                _granted.Clear();
                _loaded = false;
            }
            catch (Exception e)
            {
                LogFailure("reset", e);
            }
        }

        /// <summary>
        /// 宿主销毁时的静态缓存复位。连事件委托一起清，防止跨会话残留死引用。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                _granted.Clear();
                _loaded = false;
                OnFacilityTokenGranted = null;
            }
            catch (Exception)
            {
                // 清理路径静默：绝不让复位拖崩宿主销毁
            }
        }

        #endregion

        #region 诊断

        private static void LogWarning(string message)
        {
            try
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] " + message);
            }
            catch (Exception)
            {
                // 日志函数自身：这里再记日志会无限递归
            }
        }

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 解锁契约 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 日志函数自身：这里再记日志会无限递归
            }
        }

        #endregion
    }
}
