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

        private const float CardPortraitSize = 124f;
        /// <summary>
        /// 统计行框高。统计行关了自动缩字（一列卡片里字号不能忽大忽小），单行框高按 字号×1.45+4 给足：
        /// 15 号 ≈ 25.75，取 26（旧值 18 配 12–13 号字，最后一行离卡片底边只剩 1px，审美审查 UD-31）。
        /// </summary>
        private const float CardTextHeight = 26f;
        private const float DetailPanelWidth = 520f;
        private const float DetailPanelHeight = 650f;
        private const float DetailPortraitSize = 180f;
        private bool _onlyMissing;

        /// <summary>
        /// 本次要铺的条目。2026-09-20 起取消分页：整册一次铺完，靠滚动查看
        /// （owner 定：底部「1/5 · 下一页」整条删掉）。目录上限 CodexTuning.MaxEntries=256，
        /// 卡片是纯 Image+TMP，一次建完仍是开面板时的一次性成本，不进 Update。
        /// </summary>
        private readonly List<CodexBossInfo> _pageEntries = new List<CodexBossInfo>();

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
                    card.SetActive(false);
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
            _pageEntries.Clear();
            for (int i = 0; i < catalog.Count; i++)
            {
                CodexBossInfo info = catalog[i];
                if (info == null || string.IsNullOrEmpty(info.Key)) continue;
                CodexEntry entry = data != null ? data.Find(info.Key) : null;
                if (_onlyMissing && entry != null && entry.Kills > 0) continue;
                _pageEntries.Add(info);
            }
            if (_pageEntries.Count == 0)
            {
                CreateEmptyHint();
                return;
            }

            for (int i = 0; i < _pageEntries.Count; i++)
            {
                CodexBossInfo info = _pageEntries[i];

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

        /// <summary>分段「全部」。切换视图才回到顶部；点已选中的那颗只刷新、不跳位置。</summary>
        private void ShowAllEntries()
        {
            SetMissingFilter(false);
        }

        /// <summary>分段「待收集」。</summary>
        private void ShowMissingEntries()
        {
            SetMissingFilter(true);
        }

        private void SetMissingFilter(bool onlyMissing)
        {
            bool changed = _onlyMissing != onlyMissing;
            _onlyMissing = onlyMissing;
            RefreshAll();
            if (changed && _scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
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
                _onlyMissing ? L10n.T("当前目录已收集齐全。", "All current entries collected.")
                    : L10n.T("目录暂不可用，请稍后重新打开。", "Catalog unavailable. Please reopen later."),
                BodyFontSize,
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
            BossRushUI.ApplyFramedPanelSkin(background, 10, BossRushUISkinPart.Card);

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
            // 字号（A-20 收成四级）：名字 17（长名最小缩到 14）、击杀与最快同为正文 15。
            // 旧值 15 / 13 / 12 在 1080p 下要凑近屏幕才看得清。卡片高随之从 210 加到 236（CodexTuning.CardHeight）。
            TextMeshProUGUI nameText = ZombieModeUIHelper.CreateText(
                "Name",
                card.transform,
                displayName,
                NameFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(CardPortraitSize + 27f)),
                new Vector2(-12f, 36f),
                TextAlignmentOptions.Center,
                locked ? BossRushUIColors.TextSecondary : BossRushUIColors.TextPrimary);
            nameText.fontStyle = locked ? FontStyles.Normal : FontStyles.Bold;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = NoteFontSize;
            nameText.fontSizeMax = NameFontSize;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.raycastTarget = false;

            // 未解锁卡只留剪影与名字（A-19）：旧版再写一行「未记录」和一道「—」占位，满屏都是没信息的字。
            if (locked)
            {
                return card;
            }

            TextMeshProUGUI killsText = ZombieModeUIHelper.CreateText(
                "Kills",
                card.transform,
                L10n.T("击杀 ", "Kills ") + entry.Kills.ToString(),
                BodyFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -183f),
                new Vector2(-8f, CardTextHeight),
                TextAlignmentOptions.Center,
                BossRushUIColors.Accent);
            killsText.enableAutoSizing = false;
            killsText.raycastTarget = false;

            TextMeshProUGUI fastestText = ZombieModeUIHelper.CreateText(
                "Fastest",
                card.transform,
                L10n.T("最快 ", "Best ") + FormatFastest(entry.FastestKillSeconds),
                BodyFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -210f),
                new Vector2(-8f, CardTextHeight),
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);
            fastestText.enableAutoSizing = false;
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
            // 真正的圆（审美审查 UD-33）：旧写法是 124px 方块配 32px 圆角的九宫格，看起来像头像加载失败。
            // 半径 32 的圆角图本身就是 66px 见方的圆（中间只有 2px 直边），按 Simple + 保持宽高比铺满正方形即是圆盘；
            // 同尺寸的描边环叠一圈 Stroke，深色卡片上也分得出轮廓。
            Image badge = holder.AddComponent<Image>();
            badge.sprite = BossRushUI.GetRoundedSprite(32);
            badge.type = Image.Type.Simple;
            badge.preserveAspect = true;
            badge.color = locked ? BossRushUIColors.Surface : BossRushUIColors.Header;
            badge.raycastTarget = false;

            GameObject ring = ZombieModeUIHelper.CreateRect(
                "Ring", holder.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image ringImage = ring.AddComponent<Image>();
            ringImage.sprite = BossRushUI.GetStrokeSprite(32);
            ringImage.type = Image.Type.Simple;
            ringImage.preserveAspect = true;
            Color ringColor = BossRushUIColors.Stroke;
            ringColor.a *= locked ? 0.35f : 0.6f;
            ringImage.color = ringColor;
            ringImage.raycastTarget = false;

            Color initialColor = BossRushUIColors.TextSecondary;
            if (locked) initialColor.a = 0.35f;   // 锁定态仍是「剪影」：看得出有个字，但不抢眼
            TextMeshProUGUI initial = ZombieModeUIHelper.CreateText(
                "Initial",
                holder.transform,
                ResolveInitial(displayName),
                size * 0.36f,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                TextAlignmentOptions.Center,
                initialColor);
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
                BossRushUI.ApplyFramedPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);

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
                    TitleFontSize,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -(DetailPortraitSize + 56f)),
                    new Vector2(-24f, 44f),
                    TextAlignmentOptions.Center,
                    BossRushUIColors.TextPrimary);
                title.fontStyle = FontStyles.Bold;
                title.enableAutoSizing = true;
                title.fontSizeMin = NameFontSize;
                title.fontSizeMax = TitleFontSize;
                title.overflowMode = TextOverflowModes.Ellipsis;

                ZombieModeUIHelper.CreateSeparator(
                    "DetailDivider",
                    surface.transform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -(DetailPortraitSize + 92f)),
                    2f,
                    BossRushUIColors.Divider);

                float rowTop = DetailPortraitSize + 116f;
                CreateDetailRow(surface.transform, rowTop, L10n.T("分类", "Category"), FormatCategory(info));
                CreateDetailRow(surface.transform, rowTop + 34f, L10n.T("累计击杀", "Total kills"),
                    locked ? "—" : entry.Kills.ToString());
                CreateDetailRow(surface.transform, rowTop + 68f, L10n.T("最快击杀", "Fastest kill"),
                    locked ? "—" : FormatFastest(entry.FastestKillSeconds));
                CreateDetailRow(surface.transform, rowTop + 102f, L10n.T("初见日期", "First seen"),
                    locked ? "—" : FormatFirstSeen(entry.FirstKillTicks));
                CreateDetailRow(surface.transform, rowTop + 136f, L10n.T("初见场景", "First seen at"),
                    locked ? "—" : FormatFirstScene(entry));

                Button closeButton = ZombieModeUIHelper.CreateButton(
                    "DetailClose",
                    surface.transform,
                    L10n.T("关闭", "Close"),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 30f),
                    new Vector2(140f, 38f),
                    BossRushUIColors.SurfaceRaised,
                    BodyFontSize,
                    new Vector2(140f, 38f),
                    HideDetail,
                    true);
                // 「关闭」不是主操作：走全 Mod 的次级按钮口径（SurfaceRaised + Stroke 描边）
                BossRushUIKit.StyleSecondaryButton(closeButton);

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
                // 引用先置空（IsDetailOpen 立刻为假、Esc 下一下就关主面板），再 0.12 秒淡出后销毁；
                // 重新打开详情会新建一个 Canvas，不复用正在淡出的这个（审美审查 UD-03）。
                // 销毁路径上直接 Destroy：这时起淡出组件也会随宿主一起被销毁，不如一步到位。
                if (_destroying)
                {
                    Destroy(root);
                }
                else
                {
                    BossRushUIKit.PlayCloseAndDestroy(root);
                }
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
                BodyFontSize,
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
                BodyFontSize,
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
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            if (seconds < 0.1f) return L10n.T("<0.1 秒", "<0.1s");
            return seconds.ToString("F1") + L10n.T(" 秒", "s");
        }

        /// <summary>
        /// 初见场景显示名。优先用落档的场景名经**官方本地化**解析
        /// （owner 2026-09-20：不能把 Zeroarea_basic_01 这种裸场景名直接摆给玩家）；
        /// 老档没有场景字段时回落到旧的模式名，两者都没有才显示破折号。
        /// </summary>
        private string FormatFirstScene(CodexEntry entry)
        {
            if (entry == null) return "—";

            string scene = CodexSceneNames.Resolve(entry.FirstScene);
            if (!string.IsNullOrEmpty(scene)) return scene;

            // 老档兼容：v1 只存了模式 id，没有场景
            string mode = FormatModeName(entry.FirstMode);
            return string.IsNullOrEmpty(mode) ? "—" : mode;
        }

        /// <summary>模式 id → 显示名。未知 id 返回破折号（老档兼容）。</summary>
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
                    return L10n.T("百战留痕", "Black Market Duck Cup");
                case CodexTuning.ModeIdZombie:
                    return L10n.T("末日丧尸", "Zombie Tide");
                case CodexTuning.ModeIdRaid:
                    return L10n.T("撤离行动", "Raid");
                default:
                    return "—";
            }
        }

        /// <summary>
        /// 条目分类标签。2026-09-20 起以官方 Boss 名单（CodexOfficialBossRegistry）为准：
        /// 名单里的才叫「官方 Boss」，名单外的官方生物叫「官方精英」，其余是本 Mod 加的。
        /// 旧实现把整张过滤池一律标成「官方 Boss」，连官方明确不是 Boss 的精英怪也算进去了。
        /// </summary>
        private string FormatCategory(CodexBossInfo info)
        {
            if (info == null) return "—";
            if (info.IsZombieBoss) return L10n.T("模组 Boss · 末日丧尸", "Mod boss · Zombie Tide");
            if (info.IsCustomBoss) return L10n.T("模组 Boss · 自定义", "Mod boss · Custom");
            if (CodexOfficialBossRegistry.IsOfficialBoss(info.Key)) return L10n.T("官方 Boss", "Official boss");
            if (CodexOfficialBossRegistry.IsOfficialCreature(info.Key)) return L10n.T("官方精英", "Official elite");
            if (info.IsHistoricalOnly) return L10n.T("历史记录", "Historical");
            return L10n.T("模组 Boss", "Mod boss");
        }

        #endregion
    }
}
