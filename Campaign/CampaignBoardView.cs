// ============================================================================
// CampaignBoardView.cs - 征程公告板面板
// ============================================================================
// 全部走 Common/UI/BossRushUI.cs 共享库（AGENTS.md 4.14）：
//   sortingOrder 用 BossRushUILayers 常量、颜色用 BossRushUIColors token、
//   底图走 ApplyPanelSkin、字体走 ApplyGameFont（禁内置 Arial，渲染不了中文）、
//   CanvasScaler 走 ConfigureCanvasScaler，文本一律 TMP。
//
// 面板结构（一屏放下，不做滚动）：
//   标题栏 → 六章列表（每章一张卡：状态点 + 标题 + 目标进度 + 操作按钮）→ 关闭。
//
// 【为什么每次打开都重建而不是常驻】
//   章节状态、目标进度、线索解锁都可能在两次打开之间变化，重建比逐项刷新简单可靠，
//   而公告板是低频交互（一局最多开两次），重建成本无所谓。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>征程公告板面板。全静态：同时只允许存在一个。</summary>
    internal static class CampaignBoardView
    {
        #region 状态

        private static GameObject _root;

        #endregion

        /// <summary>面板当前是否打开。</summary>
        internal static bool IsOpen { get { return _root != null; } }

        #region 开关

        /// <summary>打开面板（幂等：已开时先关再开，保证内容是最新的）。</summary>
        internal static void Open()
        {
            try
            {
                Close();
                Build();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 打开面板失败: " + e.Message);
                Close();
            }
        }

        /// <summary>关闭面板（幂等）。</summary>
        internal static void Close()
        {
            try
            {
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                    _root = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 关闭公告板面板失败: " + e.Message);
            }
        }

        /// <summary>宿主销毁时的静态缓存复位。</summary>
        internal static void ResetStaticCaches()
        {
            Close();
        }

        #endregion

        #region 构建

        private static void Build()
        {
            Canvas canvas = BossRushUI.CreateCanvasRoot(
                "BossRushCampaignBoardCanvas", BossRushUILayers.Panel, true);
            _root = canvas.gameObject;

            BossRushUI.CreateBackdrop(_root.transform);

            GameObject panel = ZombieModeUIHelper.CreateRect(
                "Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(880f, 620f));
            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(panelBg, 14);

            BuildHeader(panel.transform);
            BuildChapterList(panel.transform);
            BuildFooter(panel.transform);

            BossRushUI.PlayOpenAnimation(panel);
        }

        private static void BuildHeader(Transform parent)
        {
            GameObject header = ZombieModeUIHelper.CreateRect(
                "Header", parent, new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(0f, 68f), new Vector2(0.5f, 0.5f));
            Image headerBg = header.AddComponent<Image>();
            headerBg.color = BossRushUIColors.Header;
            BossRushUI.ApplyPanelSkin(headerBg, 12);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", header.transform, L10n.T("鸭王征程", "Duck King Campaign"),
                30f, new Vector2(0f, 10f), new Vector2(600f, 40f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText(
                "Subtitle", header.transform,
                L10n.T("鸭王杯名人堂 · 碑上少了一个名字",
                       "Duck Cup Hall of Fame · One Name Missing From the Plaque"),
                16f, new Vector2(0f, -16f), new Vector2(700f, 26f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(subtitle);
        }

        private static void BuildChapterList(Transform parent)
        {
            IList<CampaignChapterDef> chapters = CampaignContentCatalog.Chapters;
            if (chapters == null || chapters.Count == 0) return;

            const float cardHeight = 74f;
            const float spacing = 8f;
            float startY = 200f;

            for (int i = 0; i < chapters.Count; i++)
            {
                CampaignChapterDef def = chapters[i];
                if (def == null) continue;

                float y = startY - i * (cardHeight + spacing);
                BuildChapterCard(parent, def, new Vector2(0f, y), new Vector2(800f, cardHeight));
            }
        }

        private static void BuildChapterCard(
            Transform parent, CampaignChapterDef def, Vector2 position, Vector2 size)
        {
            CampaignChapterState state = CampaignProgressService.GetState(def.ChapterId);
            Color accent = GetStateColor(state);

            GameObject card = BossRushUI.CreateCard(
                "Chapter_" + def.ChapterId, parent, position, size,
                BossRushUIColors.SurfaceRaised, accent, true);

            // 章节标题（未解锁时不剧透标题，只显示序号）
            string titleText = state == CampaignChapterState.Locked
                ? L10n.T("第 " + def.Order + " 章 · ???", "Chapter " + def.Order + " · ???")
                : L10n.T("第 " + def.Order + " 章 · " + def.TitleCN,
                         "Chapter " + def.Order + " · " + def.TitleEN);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", card.transform, titleText, 19f,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, 18f), new Vector2(430f, 26f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.rectTransform.pivot = new Vector2(0f, 0.5f);
            BossRushUI.ApplyGameFont(title);

            TextMeshProUGUI detail = ZombieModeUIHelper.CreateText(
                "Detail", card.transform, BuildDetailText(def, state), 14f,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, -12f), new Vector2(500f, 40f),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            detail.rectTransform.pivot = new Vector2(0f, 0.5f);
            BossRushUI.ApplyGameFont(detail);

            BuildChapterAction(card.transform, def, state);
        }

        /// <summary>
        /// 卡片右侧的操作按钮。四种状态对应四种交互：
        /// 可接取 → 接约；进行中 → 放弃；待交付 → 交付；已完成/未解锁 → 无按钮。
        /// </summary>
        private static void BuildChapterAction(
            Transform parent, CampaignChapterDef def, CampaignChapterState state)
        {
            string label;
            UnityEngine.Events.UnityAction action;
            Color color;

            switch (state)
            {
                case CampaignChapterState.Available:
                    label = L10n.T("接取契约", "Accept");
                    color = BossRushUIColors.Accent;
                    action = delegate { OnAccept(def.ChapterId); };
                    break;
                case CampaignChapterState.ContractActive:
                    label = L10n.T("放弃契约", "Abandon");
                    color = BossRushUIColors.Warning;
                    action = delegate { OnAbandon(); };
                    break;
                case CampaignChapterState.ReadyToDeliver:
                    label = L10n.T("交付", "Hand In");
                    color = BossRushUIColors.Success;
                    action = delegate { OnDeliver(def.ChapterId); };
                    break;
                default:
                    return;
            }

            ZombieModeUIHelper.CreateButton(
                "Action", parent, label,
                new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(140f, 40f),
                color, 16f, new Vector2(130f, 32f), action, true);
        }

        private static string BuildDetailText(CampaignChapterDef def, CampaignChapterState state)
        {
            if (state == CampaignChapterState.Locked)
            {
                return L10n.T("完成上一章后解锁。", "Unlocks after the previous chapter.");
            }

            if (state == CampaignChapterState.Completed)
            {
                return L10n.T("已交付　奖金 $" + def.RewardCash.ToString("N0"),
                              "Handed in　Reward $" + def.RewardCash.ToString("N0"));
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(L10n.T("前往：", "Go to: "));
            builder.Append(GetModeDisplayName(def.Mode));
            builder.Append("　　");
            builder.Append(L10n.T("奖金 $", "Reward $"));
            builder.Append(def.RewardCash.ToString("N0"));
            builder.Append('\n');

            // 进行中的章节显示实时进度；仅可接取时只显示目标本身
            bool showProgress = state == CampaignChapterState.ContractActive
                || state == CampaignChapterState.ReadyToDeliver;
            IList<CampaignObjectiveProgress> progress = showProgress
                ? CampaignObjectiveTracker.Progress
                : null;

            for (int i = 0; i < def.Objectives.Count; i++)
            {
                CampaignObjectiveDef objective = def.Objectives[i];
                if (objective == null) continue;
                if (i > 0) builder.Append("　·　");
                builder.Append(L10n.T(objective.DescCN, objective.DescEN));

                if (progress != null && i < progress.Count && progress[i] != null
                    && progress[i].Def == objective)
                {
                    CampaignObjectiveProgress item = progress[i];
                    if (item.Failed)
                    {
                        builder.Append(L10n.T("（本局已失败）", " (failed this run)"));
                    }
                    else if (objective.Threshold > 1)
                    {
                        builder.Append(" (").Append(item.Current).Append('/')
                            .Append(objective.Threshold).Append(')');
                    }
                    else if (item.IsSatisfied)
                    {
                        builder.Append(L10n.T("（已达成）", " (done)"));
                    }
                }
            }

            return builder.ToString();
        }

        private static string GetModeDisplayName(string mode)
        {
            switch (mode)
            {
                case CampaignContentCatalog.ModeStandard:
                    return L10n.T("标准竞技场", "Standard Arena");
                case CampaignContentCatalog.ModeModeD:
                    return L10n.T("白手起家", "Bootstrap");
                case CampaignContentCatalog.ModeModeE:
                    return L10n.T("划地为营", "Banner Wars");
                case CampaignContentCatalog.ModeModeF:
                    return L10n.T("血猎追击", "Bloodhunt");
                case CampaignContentCatalog.ModeZombie:
                    return L10n.T("末日丧尸", "Zombie Apocalypse");
                case CampaignContentCatalog.ModeFinal:
                    return L10n.T("竞技场决战", "Arena Showdown");
                default:
                    return mode ?? string.Empty;
            }
        }

        private static Color GetStateColor(CampaignChapterState state)
        {
            switch (state)
            {
                case CampaignChapterState.Available: return BossRushUIColors.Accent;
                case CampaignChapterState.ContractActive: return BossRushUIColors.Warning;
                case CampaignChapterState.ReadyToDeliver: return BossRushUIColors.Success;
                case CampaignChapterState.Completed: return BossRushUIColors.RarityLegendary;
                default: return BossRushUIColors.Disabled;
            }
        }

        private static void BuildFooter(Transform parent)
        {
            ZombieModeUIHelper.CreateButton(
                "Close", parent, L10n.T("关闭", "Close"),
                new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(160f, 42f),
                BossRushUIColors.SurfaceRaised, 17f, new Vector2(150f, 34f),
                delegate { Close(); }, true);
        }

        #endregion

        #region 交互回调

        private static void OnAccept(string chapterId)
        {
            try
            {
                if (CampaignProgressService.TryAcceptContract(chapterId))
                {
                    CampaignChapterDef def = CampaignContentCatalog.GetChapter(chapterId);
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("已接取契约：", "Contract accepted: ")
                        + (def != null ? L10n.T(def.TitleCN, def.TitleEN) : chapterId));
                }
                else
                {
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("无法接取：同时只能进行一份契约", "Cannot accept: one contract at a time"));
                }
                Open();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 接约失败: " + e.Message);
            }
        }

        private static void OnAbandon()
        {
            try
            {
                CampaignProgressService.TryAbandonContract();
                ModBehaviour.Instance?.ShowMessage(L10n.T("已放弃契约", "Contract abandoned"));
                Open();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 放弃失败: " + e.Message);
            }
        }

        private static void OnDeliver(string chapterId)
        {
            try
            {
                CampaignChapterDef def = CampaignContentCatalog.GetChapter(chapterId);
                if (!CampaignProgressService.TryDeliver(chapterId))
                {
                    ModBehaviour.Instance?.ShowMessage(L10n.T("交付失败，请稍后重试", "Hand-in failed, try again"));
                    Open();
                    return;
                }

                Close();
                // 关面板再播剧情：对话要接管输入，面板还开着会互相抢
                CampaignDialoguePlayer.PlayChapterDelivered(def);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 交付失败: " + e.Message);
            }
        }

        #endregion
    }

    public partial class ModBehaviour
    {
        /// <summary>公告板交互入口。由 CampaignBoardInteractable 调用。</summary>
        public void OpenCampaignBoardUI()
        {
            try
            {
                if (!IsCampaignConfiguredEnabled()) return;
                CampaignProgressService.EnsureInitialized();
                CampaignBoardView.Open();
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 打开公告板失败: " + e.Message);
            }
        }
    }
}
