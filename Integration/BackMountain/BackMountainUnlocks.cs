// ============================================================================
// BackMountainUnlocks.cs - 后山设施解锁状态的读侧（M0 骨架）
// ============================================================================
// 契约对面是 Campaign/CampaignFacilityUnlocks（战役侧写，后山侧读）。
//
// 【两条读法必须都有】
//   1. 全量查询：本类的 IsFacilityUnlocked 每次现查 CampaignFacilityUnlocks。
//      不缓存解锁结果——存档读入 token 时**不会**发事件（那不是新授予），
//      只靠事件会漏掉全部历史解锁。现查是 O(1) 哈希，没有缓存的必要。
//   2. 事件订阅：OnFacilityTokenGranted 只为「当场解锁当场可见」的即时反馈，
//      让玩家交付章节后不必重进基地才看到新设施。
//
// 【订阅纪律 AGENTS.md 4.6】_subscribed 布尔防重复订阅；Shutdown 必须退订。
//
// 【旁路】后山自己的 UnlockAll 调试开关在这里生效，不污染战役侧契约。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>后山设施解锁状态的唯一读入口。</summary>
    internal static class BackMountainUnlocks
    {
        #region 状态

        /// <summary>订阅幂等标记。</summary>
        private static bool _subscribed;

        /// <summary>宿主 owner（读旁路开关用）。订阅期间持有，Shutdown 时清空。</summary>
        private static ModBehaviour _owner;

        /// <summary>
        /// 本会话实时解锁回调。由运行时模块在 bootstrap 时挂上，
        /// 用于即时注入建筑/曲目并弹提示。可为空。
        /// </summary>
        private static Action<BackMountainFacility> _onFacilityUnlocked;

        #endregion

        #region 订阅生命周期

        /// <summary>
        /// 幂等订阅战役侧的实时授予事件。
        /// onFacilityUnlocked 可为 null（只查询、不要即时回调时）。
        /// </summary>
        internal static void EnsureSubscribed(ModBehaviour owner, Action<BackMountainFacility> onFacilityUnlocked)
        {
            try
            {
                _owner = owner;
                _onFacilityUnlocked = onFacilityUnlocked;
                if (_subscribed) return;

                CampaignFacilityUnlocks.OnFacilityTokenGranted += HandleTokenGranted;
                _subscribed = true;
            }
            catch (Exception e)
            {
                LogFailure("subscribe", e);
            }
        }

        /// <summary>幂等退订。dormant 与宿主销毁两条路径都必须调。</summary>
        internal static void Shutdown()
        {
            try
            {
                if (_subscribed)
                {
                    CampaignFacilityUnlocks.OnFacilityTokenGranted -= HandleTokenGranted;
                    _subscribed = false;
                }
                _onFacilityUnlocked = null;
                _owner = null;
            }
            catch (Exception e)
            {
                LogFailure("shutdown", e);
            }
        }

        /// <summary>宿主销毁时的静态缓存复位。</summary>
        internal static void ResetStaticCaches()
        {
            Shutdown();
        }

        #endregion

        #region 查询

        /// <summary>
        /// 该设施是否已解锁。no-throw，任何异常一律当作未解锁（fail-closed）。
        /// 每次现查，不缓存：读档灌入 token 时不发事件，缓存会永久错过历史解锁。
        /// </summary>
        internal static bool IsFacilityUnlocked(BackMountainFacility facility)
        {
            try
            {
                if (facility == BackMountainFacility.None) return false;

                // 旁路：后山自己的调试开关，不经战役侧契约
                if (_owner != null && _owner.IsBackMountainUnlockAllConfigured()) return true;

                int chapter = BackMountainConfig.GetRequiredChapter(facility);
                if (chapter <= 0) return false;

                string token = CampaignFacilityUnlocks.BuildTokenForChapter(chapter);
                if (string.IsNullOrEmpty(token)) return false;

                return CampaignFacilityUnlocks.IsTokenGranted(token);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>是否有任意一个设施已解锁。用于决定要不要做后山的重初始化。</summary>
        internal static bool IsAnyFacilityUnlocked()
        {
            return IsFacilityUnlocked(BackMountainFacility.Garden)
                || IsFacilityUnlocked(BackMountainFacility.Showcase)
                || IsFacilityUnlocked(BackMountainFacility.Jukebox);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 战役实时授予 token → 翻译成设施 → 回调运行时模块。
        /// token 不对应任何后山设施时静默忽略（战役 token 是通用的，
        /// 未必每章都解设施）。
        /// </summary>
        private static void HandleTokenGranted(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return;

                BackMountainFacility facility = ResolveFacilityFromToken(token);
                if (facility == BackMountainFacility.None) return;

                Action<BackMountainFacility> handler = _onFacilityUnlocked;
                if (handler == null) return;
                handler(facility);
            }
            catch (Exception e)
            {
                LogFailure("token_granted", e);
            }
        }

        /// <summary>token → 设施。无匹配返回 None。</summary>
        private static BackMountainFacility ResolveFacilityFromToken(string token)
        {
            if (string.Equals(token, CampaignFacilityUnlocks.BuildTokenForChapter(
                BackMountainConfig.GardenUnlockChapter), StringComparison.Ordinal))
            {
                return BackMountainFacility.Garden;
            }
            if (string.Equals(token, CampaignFacilityUnlocks.BuildTokenForChapter(
                BackMountainConfig.ShowcaseUnlockChapter), StringComparison.Ordinal))
            {
                return BackMountainFacility.Showcase;
            }
            if (string.Equals(token, CampaignFacilityUnlocks.BuildTokenForChapter(
                BackMountainConfig.JukeboxUnlockChapter), StringComparison.Ordinal))
            {
                return BackMountainFacility.Jukebox;
            }
            return BackMountainFacility.None;
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 解锁读侧 "
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
