// ============================================================================
// CodexView_Grid.cs - 鸭皇图鉴主面板（网格卡片 + 详情弹层 + 文本格式化）
// ============================================================================
// CodexView 的同名 partial 续篇。拆文件的唯一理由是单文件 1200 行预算
// （AGENTS.md 协作约定），职责边界：
//   - CodexView.cs      骨架、生命周期、头部、进度条、滚动容器
//   - CodexView_Grid.cs 卡片网格、锁定剪影、详情弹层、显示格式化
//
// 呈现契约：
//   - 立绘三级占位链：bundle sprite → CharacterRandomPreset.GetCharacterIcon()
//     → 名字首字 + 圆底。第三级**不是可选项**：官方 characterIconType == none 时
//     GetCharacterIcon() 就是返回 null，没有第三级会出现一片空白卡。
//   - 锁定条目用**同一张立绘**压成剪影（Image.color = LockedPortraitTint），
//     不换图、不换布局，解锁前后卡片轮廓一致，解锁的反差才有意义；
//     名字照常显示（这是"我还差谁"的信息），统计数字一律隐藏。
//   - 详情弹层走独立 Canvas，层级用 BossRushUILayers.ModalConfirm 常量压住主面板。
//   - 全部文本 TMP + 共享库字体，颜色只用 BossRushUIColors token（AGENTS.md 4.14）。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class CodexView
    {
        #region 卡片布局常量

        private const float CardPortraitSize = 140f;
        private const float CardTextHeight = 20f;
        private const float DetailPanelWidth = 460f;
        private const float DetailPanelHeight = 540f;
        private const float DetailPortraitSize = 220f;

        #endregion

        #region 详情弹层状态

        private GameObject _detailCanvasRoot;

        /// <summary>详情弹层是否开着。</summary>
        internal bool IsDetailOpen
        {
            get { return _detailCanvasRoot != null; }
        }

        #endregion

        #region 网格

        /// <summary>清空现有卡片。重建网格与销毁面板时都要调。</summary>
        private void ClearCards()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                GameObject card = _cards[i];
                if (card != null)
                {
                    Destroy(card);
                }
            }
            _cards.Clear();
        }

        /// <summary>
        /// 按目录顺序重建卡片网格。只在 RefreshAll 里被调用，绝不进 Update。
        /// 目录是唯一的行顺序真值；存档里只有击杀数据，没有"该显示谁"的信息。
        /// </summary>
        private void PopulateGrid(CodexData data)
        {
            if (_contentContainer == null) return;

            ClearCards();

            IList<CodexBossInfo> catalog = CodexBossCatalog.All;
            if (catalog == null || catalog.Count == 0)
            {
                CreateEmptyHint();
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                CodexBossInfo info = catalog[i];
                if (info == null || string.IsNullOrEmpty(info.Key)) continue;

                CodexEntry entry = data != null ? data.Find(info.Key) : null;
                try
                {
                    GameObject card = CreateCardFor(info, entry, _contentContainer);
                    if (card != null)
                    {
                        _cards.Add(card);
                    }
                }
                catch (Exception e)
                {
                    // 单张卡片失败不该让整页空白
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 卡片创建失败 "
                        + info.Key + ": " + e.Message);
                }
            }

            Canvas.ForceUpdateCanvases();
        }

        /// <summary>目录为空时的占位提示（Boss 池被全筛掉时会出现）。</summary>
        private void CreateEmptyHint()
        {
            GameObject hintRoot = ZombieModeUIHelper.CreateRect(
                "EmptyHint",
                _contentContainer,
                new Vector2(0.5f, 0.5f),
                new Vector2(CodexTuning.CardWidth * 2f, CodexTuning.CardHeight * 0.5f));

            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                hintRoot,
                L10n.T("暂无可收录的 Boss。检查 Boss 筛选器是否把全部条目都关掉了。",
                    "No boss entries yet. Check whether the boss filter has disabled everything."),
                15f,
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);
            text.raycastTarget = false;

            _cards.Add(hintRoot);
        }

        /// <summary>创建一张 Boss 卡片。entry 为 null 或击杀数为 0 即锁定态。</summary>
        private GameObject CreateCardFor(CodexBossInfo info, CodexEntry entry, Transform parent)
        {
            bool locked = entry == null || entry.Kills <= 0;
            string displayName = ResolveCardName(info, entry);

            GameObject card = ZombieModeUIHelper.CreateRect(
                "Card_" + info.Key,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(CodexTuning.CardWidth, CodexTuning.CardHeight));

            Image background = card.AddComponent<Image>();
            background.color = locked ? BossRushUIColors.Surface : BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(background, 10);

            Button button = card.AddComponent<Button>();
            button.targetGraphic = background;
            ZombieModeUIHelper.ApplyButtonColors(
                button,
                background.color,
                Color.Lerp(background.color, BossRushUIColors.Accent, 0.22f),
                BossRushUIColors.Disabled);

            // 闭包捕获的是本地副本，卡片销毁后不会回指已销毁的 UI 对象
            CodexBossInfo capturedInfo = info;
            CodexEntry capturedEntry = entry;
            button.onClick.AddListener(delegate { ShowDetail(capturedInfo, capturedEntry); });

            CreatePortraitBlock(
                card.transform,
                info,
                displayName,
                locked,
                new Vector2(0f, -6f),
                CardPortraitSize);

            // 名字：锁定态也照常显示，"我还差谁"本身就是图鉴要给的信息
            TextMeshProUGUI nameText = ZombieModeUIHelper.CreateText(
                "Name",
                card.transform,
                displayName,
                15f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(CardPortraitSize + 10f)),
                new Vector2(-8f, CardTextHeight + 4f),
                TextAlignmentOptions.Center,
                locked ? BossRushUIColors.TextSecondary : BossRushUIColors.TextPrimary);
            nameText.fontStyle = locked ? FontStyles.Normal : FontStyles.Bold;

            // 统计数字：锁定态一律隐藏成占位符
            TextMeshProUGUI killsText = ZombieModeUIHelper.CreateText(
                "Kills",
                card.transform,
                locked
                    ? L10n.T("未记录", "Unrecorded")
                    : L10n.T("击杀 ", "Kills ") + entry.Kills.ToString(),
                13f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(CardPortraitSize + 10f + CardTextHeight + 4f)),
                new Vector2(-8f, CardTextHeight),
                TextAlignmentOptions.Center,
                locked ? BossRushUIColors.Disabled : BossRushUIColors.Accent);
            killsText.raycastTarget = false;

            TextMeshProUGUI fastestText = ZombieModeUIHelper.CreateText(
                "Fastest",
                card.transform,
                locked
                    ? "—"
                    : L10n.T("最快 ", "Best ") + FormatFastest(entry.FastestKillSeconds),
                12f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(CardPortraitSize + 10f + (CardTextHeight + 4f) * 2f)),
                new Vector2(-8f, CardTextHeight),
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);
            fastestText.raycastTarget = false;

            return card;
        }

        /// <summary>
        /// 立绘块：三级占位链。返回创建出来的容器，调用方不需要再处理占位。
        /// </summary>
        private void CreatePortraitBlock(
            Transform parent,
            CodexBossInfo info,
            string displayName,
            bool locked,
            Vector2 anchoredPosition,
            float size)
        {
            GameObject holder = ZombieModeUIHelper.CreateRect(
                "Portrait",
                parent,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                anchoredPosition,
                new Vector2(size, size),
                new Vector2(0.5f, 1f));

            Sprite portrait = ResolvePortrait(info);
            if (portrait != null)
            {
                Image image = holder.AddComponent<Image>();
                image.sprite = portrait;
                image.preserveAspect = true;
                image.raycastTarget = false;
                ApplyLockedTint(image, locked);
                return;
            }

            // 第三级占位：名字首字 + 圆底。官方 characterIconType == none 时
            // GetCharacterIcon() 返回 null，这一级必须实装，否则出现空白卡。
            Image badge = holder.AddComponent<Image>();
            badge.sprite = BossRushUI.GetRoundedSprite(32);
            badge.type = Image.Type.Sliced;
            badge.color = locked ? BossRushUIColors.Disabled : BossRushUIColors.Header;
            badge.raycastTarget = false;

            TextMeshProUGUI initial = ZombieModeUIHelper.CreateText(
                "Initial",
                holder.transform,
                ResolveInitial(displayName),
                size * 0.42f,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                TextAlignmentOptions.Center,
                locked ? BossRushUIColors.Surface : BossRushUIColors.Accent);
            initial.fontStyle = FontStyles.Bold;
            initial.raycastTarget = false;
        }

        /// <summary>立绘：bundle → 官方角色图标 → null（由调用方画首字圆底）。</summary>
        private Sprite ResolvePortrait(CodexBossInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Key)) return null;

            Sprite portrait = CodexPortraitCache.GetPortrait(info.Key);
            if (portrait != null) return portrait;

            return CodexPortraitCache.GetOfficialIcon(info.Key);
        }

        /// <summary>锁定态把同一张立绘压成剪影；解锁态恢复本色。</summary>
        private void ApplyLockedTint(Image portrait, bool locked)
        {
            if (portrait == null) return;
            portrait.color = locked ? CodexTuning.LockedPortraitTint : Color.white;
        }

        #endregion

        #region 详情弹层

        /// <summary>放大立绘 + 全部统计。锁定条目也可以打开（看到的是剪影与占位）。</summary>
        private void ShowDetail(CodexBossInfo info, CodexEntry entry)
        {
            if (info == null) return;

            try
            {
                HideDetail();

                bool locked = entry == null || entry.Kills <= 0;
                string displayName = ResolveCardName(info, entry);

                Canvas canvas = BossRushUI.CreateCanvasRoot(
                    "CodexDetailCanvas", BossRushUILayers.ModalConfirm, true);
                canvas.transform.SetParent(transform, false);
                _detailCanvasRoot = canvas.gameObject;

                Image backdrop = BossRushUI.CreateBackdrop(canvas.transform);
                Button backdropButton = backdrop.gameObject.AddComponent<Button>();
                backdropButton.transition = Selectable.Transition.None;
                backdropButton.onClick.AddListener(HideDetail);

                GameObject surface = ZombieModeUIHelper.CreateRect(
                    "DetailPanel",
                    canvas.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(DetailPanelWidth, DetailPanelHeight),
                    new Vector2(0.5f, 0.5f));

                Image surfaceImage = surface.AddComponent<Image>();
                surfaceImage.color = BossRushUIColors.Surface;
                BossRushUI.ApplyPanelSkin(surfaceImage, 14);

                Button surfaceButton = surface.AddComponent<Button>();
                surfaceButton.transition = Selectable.Transition.None;

                CreatePortraitBlock(
                    surface.transform,
                    info,
                    displayName,
                    locked,
                    new Vector2(0f, -24f),
                    DetailPortraitSize);

                TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                    "DetailName",
                    surface.transform,
                    displayName,
                    24f,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -(DetailPortraitSize + 40f)),
                    new Vector2(-24f, 32f),
                    TextAlignmentOptions.Center,
                    BossRushUIColors.TextPrimary);
                title.fontStyle = FontStyles.Bold;

                ZombieModeUIHelper.CreateSeparator(
                    "DetailDivider",
                    surface.transform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -(DetailPortraitSize + 78f)),
                    2f,
                    BossRushUIColors.Divider);

                float rowTop = DetailPortraitSize + 92f;
                CreateDetailRow(surface.transform, rowTop, L10n.T("分类", "Category"), FormatCategory(info));
                CreateDetailRow(surface.transform, rowTop + 34f, L10n.T("累计击杀", "Total kills"),
                    locked ? "—" : entry.Kills.ToString());
                CreateDetailRow(surface.transform, rowTop + 68f, L10n.T("最快击杀", "Fastest kill"),
                    locked ? "—" : FormatFastest(entry.FastestKillSeconds));
                CreateDetailRow(surface.transform, rowTop + 102f, L10n.T("初见日期", "First seen"),
                    locked ? "—" : FormatFirstSeen(entry.FirstKillTicks));
                CreateDetailRow(surface.transform, rowTop + 136f, L10n.T("初见模式", "First mode"),
                    locked ? "—" : FormatModeName(entry.FirstMode));

                Button closeButton = ZombieModeUIHelper.CreateButton(
                    "DetailClose",
                    surface.transform,
                    L10n.T("关闭", "Close"),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 30f),
                    new Vector2(140f, 38f),
                    BossRushUIColors.Header,
                    16f,
                    new Vector2(140f, 38f),
                    HideDetail,
                    true);
                ZombieModeUIHelper.ApplyButtonColors(
                    closeButton,
                    BossRushUIColors.Header,
                    Color.Lerp(BossRushUIColors.Header, BossRushUIColors.Accent, 0.28f),
                    BossRushUIColors.Disabled);

                BossRushUI.PlayOpenAnimation(surface);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 详情弹层创建失败: " + e.Message);
                HideDetail();
            }
        }

        /// <summary>收起详情弹层。幂等。</summary>
        private void HideDetail()
        {
            if (_detailCanvasRoot == null) return;

            GameObject root = _detailCanvasRoot;
            _detailCanvasRoot = null;
            try
            {
                Destroy(root);
            }
            catch (Exception)
            {
                // 销毁失败静默：不得阻断关闭路径
            }
        }

        /// <summary>详情里的一行「标签 : 值」。</summary>
        private void CreateDetailRow(Transform parent, float topOffset, string label, string value)
        {
            TextMeshProUGUI labelText = ZombieModeUIHelper.CreateText(
                "Row_" + label,
                parent,
                label,
                15f,
                new Vector2(0f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(20f, -topOffset),
                new Vector2(-20f, 28f),
                TextAlignmentOptions.Left,
                BossRushUIColors.TextSecondary);
            labelText.raycastTarget = false;

            TextMeshProUGUI valueText = ZombieModeUIHelper.CreateText(
                "Value_" + label,
                parent,
                value,
                15f,
                new Vector2(0.5f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -topOffset),
                new Vector2(-20f, 28f),
                TextAlignmentOptions.Right,
                BossRushUIColors.TextPrimary);
            valueText.raycastTarget = false;
        }

        #endregion

        #region 显示格式化

        /// <summary>
        /// 卡片显示名。目录名优先；目录查不到就用落档快照名；再不行退回 key，
        /// 绝不返回空串（空白卡片是最难排查的 UI 故障）。
        /// </summary>
        private string ResolveCardName(CodexBossInfo info, CodexEntry entry)
        {
            if (info != null && !string.IsNullOrEmpty(info.DisplayName)) return info.DisplayName;
            if (entry != null && !string.IsNullOrEmpty(entry.DisplayName)) return entry.DisplayName;
            if (info != null && !string.IsNullOrEmpty(info.Key)) return info.Key;
            return "?";
        }

        /// <summary>名字首字。空名给 "?"。</summary>
        private string ResolveInitial(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "?";
            return displayName.Substring(0, 1);
        }

        /// <summary>初见日期。0 表示未解锁，显示破折号。</summary>
        private string FormatFirstSeen(long ticks)
        {
            if (ticks <= 0L) return "—";
            try
            {
                DateTime utc = new DateTime(ticks, DateTimeKind.Utc);
                return utc.ToLocalTime().ToString("yyyy-MM-dd");
            }
            catch (Exception)
            {
                // 脏存档里的越界 ticks 会抛：当作未记录处理，不让面板崩
                return "—";
            }
        }

        /// <summary>最快击杀。&lt;=0 表示未记录。</summary>
        private string FormatFastest(float seconds)
        {
            if (seconds <= 0f) return "—";
            return seconds.ToString("F1") + L10n.T(" 秒", "s");
        }

        /// <summary>模式 id → 显示名。未知 id 原样返回（老档兼容）。</summary>
        private string FormatModeName(string modeId)
        {
            if (string.IsNullOrEmpty(modeId)) return "—";

            switch (modeId)
            {
                case CodexTuning.ModeIdArena:
                    return L10n.T("标准竞技场", "Arena");
                case CodexTuning.ModeIdHell:
                    return L10n.T("无间炼狱", "Infinite Hell");
                case CodexTuning.ModeIdModeD:
                    return L10n.T("白手起家", "From Nothing");
                case CodexTuning.ModeIdModeE:
                    return L10n.T("划地为营", "Hold the Line");
                case CodexTuning.ModeIdModeF:
                    return L10n.T("血猎追击", "Bloodhunt");
                case CodexTuning.ModeIdModeG:
                    return L10n.T("宿命回响", "Fate Echo");
                case CodexTuning.ModeIdModeH:
                    return L10n.T("斗蛐蛐", "Cricket Fight");
                case CodexTuning.ModeIdZombie:
                    return L10n.T("末日丧尸", "Zombie Tide");
                case CodexTuning.ModeIdRaid:
                    return L10n.T("撤离行动", "Raid");
                default:
                    return modeId;
            }
        }

        /// <summary>条目分类标签。</summary>
        private string FormatCategory(CodexBossInfo info)
        {
            if (info == null) return "—";
            if (info.IsZombieBoss) return L10n.T("末日丧尸", "Zombie Tide");
            if (info.IsCustomBoss) return L10n.T("自定义 Boss", "Custom boss");
            if (info.IsHistoricalOnly) return L10n.T("历史记录", "Historical");
            return L10n.T("官方 Boss", "Official boss");
        }

        #endregion
    }
}
