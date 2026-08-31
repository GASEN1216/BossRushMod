// ============================================================================
// CampaignHud.cs - 局内契约目标追踪条
// ============================================================================
// 在屏幕右上角显示当前契约的章节名与逐条目标进度。
// 由 CampaignRuntimeModule.OnUpdate 每帧驱动；未武装契约时隐藏并早返。
//
// UI 硬约束（AGENTS 4.14 + tests/BossRushUISharedLibraryGuard.py），照 RandomEventHud：
//   - Canvas 一律 BossRushUI.CreateCanvasRoot(..., interactive:false)：
//     内部已配好 CanvasScaler 并关掉 GraphicRaycaster，本文件不得自己 AddComponent。
//   - sortingOrder 复用 BossRushUILayers.HudOverlay 常量，禁止魔法数字。
//   - 颜色只用 BossRushUIColors token；文本一律 TMP + 共享字体解析器。
//   - 全部 Graphic 的 raycastTarget 置 false：HUD 必须让点击穿透。
//
// 【性能】Tick 是每帧路径：只在文本真的变化时才写 TMP.text，
// 其余帧只有一次字符串构建前的短路比较。未武装时第一行就早返，零成本。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>局内契约目标 HUD。非交互，点击必须穿透。</summary>
    internal static class CampaignHud
    {
        #region 状态

        private static Canvas _canvas;
        private static GameObject _panel;
        private static TextMeshProUGUI _titleText;
        private static TextMeshProUGUI _bodyText;

        /// <summary>
        /// 上一次写进 TMP 时对应的章节 ID；null 表示下一帧必须重建。
        /// 与下面两个数组一起构成**零分配脏检查**：只有内容真的变了才拼字符串。
        /// </summary>
        private static string _shownChapterId;

        /// <summary>上一次写进 TMP 时各目标的计数。容器复用，比对不分配。</summary>
        private static readonly List<int> _shownCurrent = new List<int>();

        /// <summary>上一次写进 TMP 时各目标的失败位。</summary>
        private static readonly List<bool> _shownFailed = new List<bool>();

        private static bool _buildFailed;

        /// <summary>正文拼装缓冲。复用同一个 builder，避免每帧新建。</summary>
        private static readonly StringBuilder _builder = new StringBuilder(160);

        #endregion

        #region 每帧驱动

        /// <summary>由运行时模块 OnUpdate 调用。未武装契约时隐藏并早返。</summary>
        internal static void Tick()
        {
            try
            {
                if (!CampaignObjectiveTracker.IsArmed)
                {
                    HideImmediate();
                    return;
                }

                CampaignChapterDef def = CampaignProgressService.GetActiveChapterDef();
                if (def == null)
                {
                    HideImmediate();
                    return;
                }

                EnsureBuilt();
                if (_panel == null) return;

                if (!_panel.activeSelf) _panel.SetActive(true);

                // 脏检查放在**构建之前**：拼字符串本身就是分配，
                // 先拼再比等于每帧都付了代价（本文件早先的写法正是如此）。
                // 这里只比整数与 bool，零分配。
                if (!HasProgressChanged(def)) return;

                CaptureProgressSnapshot(def);

                string title = L10n.T("契约 · " + def.TitleCN, "Contract · " + def.TitleEN);
                if (_titleText != null) _titleText.text = title;

                string body = BuildBody();
                if (_bodyText != null) _bodyText.text = body;
            }
            catch (Exception)
            {
                // 每帧路径：不抛也不打日志
            }
        }

        /// <summary>
        /// 显示内容是否需要重建。只读整数与 bool，不分配。
        /// </summary>
        private static bool HasProgressChanged(CampaignChapterDef def)
        {
            if (!string.Equals(_shownChapterId, def.ChapterId, StringComparison.Ordinal)) return true;

            IList<CampaignObjectiveProgress> progress = CampaignObjectiveTracker.Progress;
            if (_shownCurrent.Count != progress.Count) return true;

            for (int i = 0; i < progress.Count; i++)
            {
                CampaignObjectiveProgress item = progress[i];
                int current = item != null ? item.Current : 0;
                bool failed = item != null && item.Failed;
                if (_shownCurrent[i] != current) return true;
                if (_shownFailed[i] != failed) return true;
            }
            return false;
        }

        /// <summary>记下这次显示对应的进度快照。</summary>
        private static void CaptureProgressSnapshot(CampaignChapterDef def)
        {
            _shownChapterId = def.ChapterId;
            _shownCurrent.Clear();
            _shownFailed.Clear();

            IList<CampaignObjectiveProgress> progress = CampaignObjectiveTracker.Progress;
            for (int i = 0; i < progress.Count; i++)
            {
                CampaignObjectiveProgress item = progress[i];
                _shownCurrent.Add(item != null ? item.Current : 0);
                _shownFailed.Add(item != null && item.Failed);
            }
        }

        /// <summary>逐条目标的一行进度文本。</summary>
        private static string BuildBody()
        {
            _builder.Length = 0;

            IList<CampaignObjectiveProgress> progress = CampaignObjectiveTracker.Progress;
            for (int i = 0; i < progress.Count; i++)
            {
                CampaignObjectiveProgress item = progress[i];
                if (item == null || item.Def == null) continue;

                if (_builder.Length > 0) _builder.Append('\n');

                if (item.Failed) _builder.Append("✗ ");
                else if (item.IsSatisfied) _builder.Append("✓ ");
                else _builder.Append("· ");

                _builder.Append(L10n.T(item.Def.DescCN, item.Def.DescEN));

                if (item.Failed)
                {
                    _builder.Append(L10n.T("（本局已失败）", " (failed)"));
                }
                else if (item.Def.Threshold > 1)
                {
                    _builder.Append("  ").Append(item.Current).Append('/').Append(item.Def.Threshold);
                }
            }

            return _builder.ToString();
        }

        #endregion

        #region 构建

        private static void EnsureBuilt()
        {
            if (_panel != null || _buildFailed) return;

            try
            {
                _canvas = BossRushUI.CreateCanvasRoot(
                    "BossRushCampaignHudCanvas", BossRushUILayers.HudOverlay, false);
                UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);

                // 右上角贴边，避开左上角的随机事件徽章与波次提示
                _panel = ZombieModeUIHelper.CreateRect(
                    "CampaignHud", _canvas.transform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-24f, -110f), new Vector2(300f, 96f),
                    new Vector2(1f, 1f));

                Image background = _panel.AddComponent<Image>();
                background.color = BossRushUIColors.Surface;
                background.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(background, 10);

                _titleText = ZombieModeUIHelper.CreateText(
                    "Title", _panel.transform, string.Empty, 15f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -16f), new Vector2(-20f, 22f),
                    TextAlignmentOptions.Left, BossRushUIColors.Accent);
                _titleText.raycastTarget = false;
                BossRushUI.ApplyGameFont(_titleText);

                _bodyText = ZombieModeUIHelper.CreateText(
                    "Body", _panel.transform, string.Empty, 13f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -56f), new Vector2(-20f, 56f),
                    TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                _bodyText.raycastTarget = false;
                BossRushUI.ApplyGameFont(_bodyText);
            }
            catch (Exception e)
            {
                // 构建失败只记一次，之后不再重试——每帧重试会刷爆日志
                _buildFailed = true;
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] HUD 构建失败: " + e.Message);
                HideImmediate();
            }
        }

        #endregion

        #region 隐藏与清理

        private static void HideImmediate()
        {
            try
            {
                if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
                // 快照作废：下次显示时无条件重建一次，
                // 顺带覆盖「隐藏期间换了语言」这种不体现在计数上的变化
                _shownChapterId = null;
                _shownCurrent.Clear();
                _shownFailed.Clear();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 隐藏目标 HUD 失败: " + e.Message);
            }
        }

        /// <summary>宿主销毁时的静态缓存复位。</summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                if (_canvas != null)
                {
                    UnityEngine.Object.Destroy(_canvas.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 销毁 HUD 画布失败: " + e.Message);
            }

            _canvas = null;
            _panel = null;
            _titleText = null;
            _bodyText = null;
            _shownChapterId = null;
            _shownCurrent.Clear();
            _shownFailed.Clear();
            _buildFailed = false;
        }

        #endregion
    }
}
