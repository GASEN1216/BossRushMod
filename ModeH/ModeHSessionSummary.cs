// ============================================================================
// ModeHSessionSummary.cs - 鸭王杯「本场总结」：这一趟打了哪几场、押注输赢、得到 / 失去什么（2026-09-29 owner）
// ============================================================================
// 这一趟（从进鸭王杯地图到离开）里真正发生过的结算事实按场次记下来，离开前弹一张总结页：
//   - 记账只收集「发生过什么」：结算过的场次号、每场押注账本的快照（ModeHCashBetService 只留最新一条，
//     下一场锁盘会覆盖，所以在结算页、锁盘前、退回时各抄一份）、技术中止 / 中途退出的场次、名人堂；
//   - 页面内容由 ModeHRuntimeModule.BuildSessionSummaryContent 按赛季里的战报与这些快照组装，版式就是
//     结算页（ModeHPage.Settlement：横幅 + 物品格 + 战报单 + 一颗按钮），独立的 ModeHUI 实例，
//     赛季运行时关停（UI 全拆）之后照样能显示；
//   - 唯一的按钮是「返回基地」（或「关闭」），ESC 等于点它；点完才执行离场回调（SafeExitFromModeH）。
//     显示失败、F3 自动验收在跑、这一趟什么也没发生时不弹，直接离场，不挡官方返回流程；
//     场景被别的途径切走（官方返回）时总结页自行收起，不再重复离场。
// 纯运行时，不落盘：押注与奖励的持久事实仍在各自账本里，这里只是一次性的展示。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class ModeHSessionSummary
    {
        #region 这一趟的记账

        /// <summary>这一趟结算过的场次（按先后），键是 runId|场次号。</summary>
        private static readonly List<string> _settled = new List<string>();
        /// <summary>这一趟被技术中止打断、还没打完的场次。</summary>
        private static readonly List<string> _interrupted = new List<string>();
        /// <summary>每场押注账本的最新快照（键同上）。</summary>
        private static readonly Dictionary<string, ModeHCashBetRecord> _bets =
            new Dictionary<string, ModeHCashBetRecord>(StringComparer.Ordinal);
        private static bool _hallOfFame;

        internal static string Key(string runId, int matchIndex)
        {
            return (runId ?? string.Empty) + "|" + matchIndex;
        }

        /// <summary>这一场出了结算（结算页出现过）。</summary>
        internal static void NoteSettled(string runId, int matchIndex)
        {
            if (matchIndex <= 0) return;
            string key = Key(runId, matchIndex);
            if (!_settled.Contains(key)) _settled.Add(key);
            _interrupted.Remove(key);
        }

        /// <summary>这一场被技术中止打断（还没有战报）。之后重打出了结算会被 NoteSettled 取代。</summary>
        internal static void NoteInterrupted(string runId, int matchIndex)
        {
            if (matchIndex <= 0) return;
            string key = Key(runId, matchIndex);
            if (!_settled.Contains(key) && !_interrupted.Contains(key)) _interrupted.Add(key);
        }

        /// <summary>抄一份押注账本（同一场只留最新的一份）。</summary>
        internal static void NoteBet(ModeHCashBetRecord record)
        {
            if (record == null || record.status == ModeHCashBetService.StatusNone || record.matchIndex <= 0) return;
            try { _bets[Key(record.runId, record.matchIndex)] = record.Clone(); }
            catch (Exception) { /* 快照失败只少一行总结 */ }
        }

        internal static void NoteHallOfFame()
        {
            _hallOfFame = true;
        }

        internal static List<string> SettledKeys { get { return new List<string>(_settled); } }
        internal static List<string> InterruptedKeys { get { return new List<string>(_interrupted); } }
        internal static bool HallOfFame { get { return _hallOfFame; } }

        internal static ModeHCashBetRecord FindBet(string key)
        {
            ModeHCashBetRecord record;
            return key != null && _bets.TryGetValue(key, out record) ? record : null;
        }

        /// <summary>这一趟所有押注快照（含上一季原样退回的那笔）。</summary>
        internal static List<ModeHCashBetRecord> AllBets()
        {
            return new List<ModeHCashBetRecord>(_bets.Values);
        }

        /// <summary>这一趟有没有值得总结的事。</summary>
        internal static bool HasFacts
        {
            get { return _settled.Count > 0 || _interrupted.Count > 0 || _bets.Count > 0 || _hallOfFame; }
        }

        /// <summary>这一趟结束（总结已组装或不需要）：清掉记账，下一趟从零开始。</summary>
        internal static void Discard()
        {
            _settled.Clear();
            _interrupted.Clear();
            _bets.Clear();
            _hallOfFame = false;
        }

        #endregion

        #region 总结页

        private static ModeHUI _ui;
        private static Action _onClosed;
        private static bool _sceneSubscribed;

        /// <summary>总结页是否开着。</summary>
        internal static bool IsOpen { get { return _ui != null; } }

        /// <summary>
        /// 弹出总结页；按钮（或 ESC）点下后收起页面，再执行 <paramref name="onClosed"/>（可为 null）。
        /// 返回 false 表示没弹出来（内容为空或建页失败），调用方应当直接做原本的离场。
        /// </summary>
        internal static bool Show(ModeHPageContent content, string buttonLabel, Action onClosed)
        {
            if (content == null) return false;
            try
            {
                Close();
                content.Actions.Clear();
                content.Actions.Add(new ModeHActionData
                {
                    Label = buttonLabel,
                    IsPrimary = true,
                    IsCancel = true, // ESC = 返回基地 / 关闭：这一页只有这一个去处
                    OnClick = Finish,
                });
                _onClosed = onClosed;
                _ui = new ModeHUI();
                _ui.OpenPage(ModeHPage.Settlement, ModeHLifecycle.SeasonEnded, "summary", content);
                // 只有「点完要离场」的总结才跟场景走：场景被别的途径切走就收起、不再离场。
                // 只能关闭的那种本来就是在回基地途中 / 基地里弹的，基地还在分场景加载，不能被切场景顺手收掉。
                if (onClosed != null && !_sceneSubscribed)
                {
                    SceneManager.activeSceneChanged += OnActiveSceneChanged;
                    _sceneSubscribed = true;
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 本场总结显示失败: " + e.Message);
                _onClosed = null;
                Close();
                return false;
            }
        }

        /// <summary>按钮：先收页面（释放输入租约），再离场。回调只执行一次。</summary>
        private static void Finish()
        {
            Action callback = _onClosed;
            _onClosed = null;
            Close();
            if (callback == null) return;
            try { callback(); }
            catch (Exception e) { ModBehaviour.DevLog("[ModeH] 本场总结离场回调失败: " + e.Message); }
        }

        /// <summary>场景被别的途径切走（官方返回等）：页面收起，不再执行离场回调。</summary>
        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (_ui == null) return;
            _onClosed = null;
            Close();
        }

        private static void Close()
        {
            if (_sceneSubscribed)
            {
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                _sceneSubscribed = false;
            }
            ModeHUI ui = _ui;
            _ui = null;
            if (ui == null) return;
            try { ui.ClosePage(); }
            catch (Exception) { /* 画布已随场景销毁 */ }
        }

        /// <summary>Mod 销毁：立即收起页面、退订场景事件、清掉记账（§4.6）。</summary>
        internal static void ResetStaticCaches()
        {
            _onClosed = null;
            if (_sceneSubscribed)
            {
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                _sceneSubscribed = false;
            }
            ModeHUI ui = _ui;
            _ui = null;
            try { if (ui != null) ui.DestroyAll(); }
            catch (Exception) { /* 画布已销毁 */ }
            Discard();
        }

        #endregion
    }
}
