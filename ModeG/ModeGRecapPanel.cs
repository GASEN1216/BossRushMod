using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Mode G 终局呈现层（任务 #9 可玩性 UX 补强）。
    ///
    /// 职责：
    /// - 失败横幅/失败 recap 的宿敌预告文案组装（复仇钩子；§15 三分支：
    ///   已写入下局宿敌 Rank / 宿敌记录受版本保护 / 未形成新宿敌）；
    /// - 结算 recap 面板（复用 ZombieModeUIHelper 模态件，不新建 UI 框架）：
    ///   奖励档 near-miss、三轴尝试/破解计数、印章/图鉴 near-miss、累计纪录；
    /// - 弹药禁令归因文案组装（上局弹种威胁占比）。
    ///
    /// 硬约束：
    /// - 全部新文本走 L10n.T("BossRush_ModeG_*") key，代码只持 key；
    /// - 全程防御式 try/catch，呈现失败不得拖崩宿主（静默降级无面板）；
    /// - recap 面板仅终局一次性构建（非热路径），自动倒计时关闭不阻塞官方死亡流程。
    /// </summary>
    internal static class ModeGRecapPanel
    {
        #region Tunables（owner tunable）

        /// <summary>面板宽</summary>
        private const float PanelWidth = 820f;
        /// <summary>建面板时的初始高；排版完成后按内容收口</summary>
        private const float PanelHeight = 620f;
        private const float PadX = 40f;
        private const float ContentWidth = PanelWidth - PadX * 2f;
        private const float PadTop = 24f;
        private const float PadBottom = 24f;
        private const float EmblemSize = 56f;
        /// <summary>卡片之间的间距</summary>
        private const float SectionGap = 10f;
        /// <summary>卡片正文左边距：卡片左侧 3–7 是强调竖条，再留 11</summary>
        private const float SectionPadLeft = 18f;
        private const float SectionPadRight = 16f;
        private const float SectionPadY = 12f;
        /// <summary>三张卡的错峰间隔（每项 0.04–0.06 秒，AGENTS §4.14 动效口径）</summary>
        private const float SectionStagger = 0.05f;
        private static readonly Vector2 CloseButtonSize = new Vector2(220f, 48f);
        /// <summary>失败 recap 自动关闭秒数（不阻塞死亡/回城流程）</summary>
        private const float DefeatAutoCloseSeconds = 14f;
        /// <summary>胜利 recap 自动关闭秒数</summary>
        private const float VictoryAutoCloseSeconds = 24f;
        /// <summary>图鉴里程碑（累计宿敌击败数；owner tunable，纯呈现）</summary>
        private static readonly int[] CodexMilestones = { 1, 3, 5, 10 };

        #endregion

        #region Nemesis Attribution Outcome（失败宿敌预告）

        /// <summary>宿敌归因结果（ModeGDeathRouting 写入/升级后返回）。</summary>
        internal struct NemesisAttribution
        {
            /// <summary>是否成功写入/升级宿敌记录</summary>
            public bool written;
            /// <summary>归因是否因存储故障被阻断（击杀者已知但记录未变更）</summary>
            public bool storeBlocked;
            /// <summary>击杀者 Boss preset key（归因成功或 storeBlocked 时非空）</summary>
            public string bossKey;
            /// <summary>写入后的持久 Rank（仅 written 时有效；读取内存提交后的新值）</summary>
            public int rank;
        }

        /// <summary>
        /// 组装宿敌预告尾行（失败横幅与失败 recap 共用）。
        /// written  -> 下局宿敌 &lt;Boss&gt; N 阶（英文 Rank N）；
        /// blocked  -> 击杀者 &lt;Boss&gt; · 宿敌记录受版本保护，未变更；
        /// 其余     -> 未形成新宿敌。
        /// </summary>
        internal static string ComposeNemesisPreviewLine(NemesisAttribution attribution)
        {
            try
            {
                if (attribution.written && !string.IsNullOrEmpty(attribution.bossKey))
                {
                    return L10n.T("BossRush_ModeG_NextNemesis") + " "
                        + ModeGEncounterVariation.GetManagedBossDisplayName(attribution.bossKey)
                        + " " + string.Format(L10n.T("BossRush_ModeG_RankWord"), attribution.rank);
                }
                if (attribution.storeBlocked && !string.IsNullOrEmpty(attribution.bossKey))
                {
                    return L10n.T("BossRush_ModeG_KillerWord") + " "
                        + ModeGEncounterVariation.GetManagedBossDisplayName(attribution.bossKey)
                        + " · " + L10n.T("BossRush_ModeG_NemesisProtected");
                }
                return L10n.T("BossRush_ModeG_NoNewNemesis");
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 组装失败横幅全文（宿敌未竟 · 第 X/9 波 · Resolve Y/11 · 宿敌预告行）。
        /// </summary>
        internal static string ComposeDefeatBanner(int waveNumber, int resolve, NemesisAttribution attribution)
        {
            try
            {
                string line1 = ModeGRichText.DangerTag + L10n.T("BossRush_ModeG_DefeatTitle") + "</color> "
                    + L10n.T("BossRush_ModeG_WaveWord") + " " + waveNumber + L10n.T("BossRush_ModeG_WaveOfNine")
                    + " · " + L10n.T("决意", "Resolve") + " " + resolve + "/" + ModeGAdaptiveCombat.MaxResolveTotal;
                string line2 = ComposeNemesisPreviewLine(attribution);
                return string.IsNullOrEmpty(line2) ? line1 : line1 + "\n" + line2;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Ammo Ban Attribution（弹药禁令归因文案）

        /// <summary>
        /// 弹药禁令归因行：「上局你的 &lt;弹药名&gt; 贡献了 X% 威胁」。
        /// threatSharePercent &lt;=0 时返回空串（调用方跳过归因行）。
        /// </summary>
        internal static string ComposeBanAttributionLine(string ammoName, int threatSharePercent)
        {
            try
            {
                if (string.IsNullOrEmpty(ammoName) || threatSharePercent <= 0) return string.Empty;
                return L10n.T("BossRush_ModeG_BanAttrPrefix") + ammoName
                    + L10n.T("BossRush_ModeG_BanAttrMid") + threatSharePercent
                    + L10n.T("BossRush_ModeG_BanAttrTail");
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 弹药本地化展示名（metadata 缺失时稳定回退「弹药 #TypeID」，不持有 Item）。
        /// </summary>
        internal static string GetAmmoDisplayName(int ammoTypeId)
        {
            try
            {
                ItemStatsSystem.ItemMetaData meta = ItemStatsSystem.ItemAssetsCollection.GetMetaData(ammoTypeId);
                if (meta.id == ammoTypeId && !string.IsNullOrEmpty(meta.DisplayName))
                {
                    return meta.DisplayName;
                }
            }
            catch { /* metadata 读取失败走稳定回退 */ }
            return L10n.T("BossRush_ModeG_AmmoFallback") + " #" + ammoTypeId;
        }

        #endregion

        #region Recap Panel（终局结算页）

        private static GameObject _activeRoot;

        /// <summary>
        /// 显示终局 recap 面板（Victory/Defeat 各一次；幂等单实例）。
        /// 失败静默降级（无面板不阻塞结算）。
        /// </summary>
        /// <param name="previousBestWave">
        /// 调用方在 RecordRun **之前** 用 <see cref="ReadCurrentBestWave"/> 取的旧最佳波次，
        /// 供「新纪录」判定；传入已含本局的值会让每局都误报新纪录。
        /// </param>
        internal static void Show(ModeGRuntimeModule module, ModeGBattleResult result,
            string nemesisPreviewLine, int previousBestWave)
        {
            try
            {
                if (module == null) return;
                if (result != ModeGBattleResult.Victory && result != ModeGBattleResult.Defeat) return;

                DismissActive();

                GameObject root = BossRushUI.CreateCanvasRoot("ModeG_Recap", BossRushUILayers.ModeGRecap, true).gameObject;
                _activeRoot = root; // 构建中途失败仍由唯一 owner 回收画布。
                UnityEngine.Object.DontDestroyOnLoad(root);

                // 版式（2026-09-23 审美审查 UB-20）：旧版九行居中字写死 y 从 +262 排到 -96，按钮下空 110px，
                // 面板一帧出现、到点一帧消失，关闭钮与面板同色、悬停突然变橙。现在与入场确认页同一套：
                // 近不透明底、徽记 + 标题一行、从顶边往下按内容排，内容分三张卡（奖励 / 三轴与契约 / 纪录与印章），
                // 卡内左对齐、错峰滑入；关闭钮是次级样式并写着自动关闭的倒计时，到点淡出。
                bool victory = result == ModeGBattleResult.Victory;
                GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                    "ModeG_RecapSurface", root.transform, new Vector2(PanelWidth, PanelHeight),
                    BossRushUIColors.WarningText);
                ModeGInteractable.MakeSurfaceOpaque(surface);
                Transform st = surface.transform;

                // 标题：胜利金色、失败红色，左侧 Mode G 徽记
                float cursor = PadTop;
                cursor += ModeGInteractable.PlaceTitleRow(st, ModeGInteractable.CreateEmblem(st, EmblemSize), cursor,
                    victory ? L10n.T("BossRush_ModeG_Recap_VictoryTitle") : L10n.T("BossRush_ModeG_Recap_DefeatTitle"),
                    32f, victory ? BossRushUIColors.WarningText : BossRushUIColors.DangerText,
                    ContentWidth, EmblemSize) + 4f;

                // 概要：第 X/9 波 · Resolve Y/11（± 新纪录）
                int wave = module.State != null ? module.State.waveEpoch + 1 : 0;
                int resolve = module.Adaptive != null ? module.Adaptive.TotalResolve : 0;
                string summary = L10n.T("BossRush_ModeG_WaveWord") + " " + wave + L10n.T("BossRush_ModeG_WaveOfNine")
                    + " · " + L10n.T("决意", "Resolve") + " " + resolve + "/" + ModeGAdaptiveCombat.MaxResolveTotal;
                if (IsNewBestWave(wave, previousBestWave))
                {
                    summary += " · " + ModeGRichText.WarningTag + L10n.T("BossRush_ModeG_NewRecord") + "</color>";
                }
                TextMeshProUGUI summaryText = ZombieModeUIHelper.CreateText("Summary", st, summary,
                    20f, Vector2.zero, new Vector2(ContentWidth, 33f),
                    TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
                cursor += ModeGInteractable.PlaceText(summaryText, ContentWidth, 0f, cursor) + 14f;

                // 卡 1：奖励档 near-miss（当前档件数 + 距下一档还差 X 决意）；失败时带宿敌预告行（复仇钩子）
                string reward = victory
                    ? ComposeRewardGapLine(resolve)
                    : ModeGRichText.SecondaryTag + L10n.T("本局未通关，无通关奖励。", "Run not cleared; no victory rewards.") + "</color>";
                if (!victory && !string.IsNullOrEmpty(nemesisPreviewLine))
                {
                    reward += "\n" + ModeGRichText.DangerTag + nemesisPreviewLine + "</color>";
                }
                // 字号收四级（UI 共识对照审查 B-24）：标题 32 / 概要 20 / 奖励、三轴与按钮 16 / 纪录 14。
                cursor += PlaceSection(st, "RewardCard", 0, cursor, reward, 16f, BossRushUIColors.TextPrimary,
                    victory ? BossRushUIColors.WarningText : BossRushUIColors.DangerText) + SectionGap;

                // 卡 2：三轴本局尝试/破解计数 + 本局契约达成状态
                string axes = ComposeAxisLines(module);
                string contract = ComposeContractLine(module);
                if (!string.IsNullOrEmpty(contract)) axes += "\n" + contract;
                // 强调色只用本模式的金色与状态色（B-24）：三轴卡是中性信息，竖条用描边色，不再混一道薄荷绿。
                cursor += PlaceSection(st, "AxesCard", 1, cursor, axes, 16f, BossRushUIColors.TextPrimary,
                    BossRushUIColors.Stroke) + SectionGap;

                // 卡 3：印章目标 + 图鉴 near-miss + 累计纪录（读 profile 累计数据；读不到的行跳过）
                string records = JoinLines(ComposeSealLine(), ComposeCodexLine(), ComposeProfileLine());
                if (!string.IsNullOrEmpty(records))
                {
                    cursor += PlaceSection(st, "RecordsCard", 2, cursor, records, 14f, BossRushUIColors.TextSecondary,
                        BossRushUIColors.Stroke) + SectionGap;
                }

                // 关闭按钮（另有自动倒计时关闭，按钮上写着还剩几秒）
                cursor += 8f;
                string closeLabel = L10n.T("BossRush_ModeG_Recap_Close");
                Button closeButton = ZombieModeUIHelper.CreateButton(
                    "Close", st, closeLabel,
                    new Vector2(0.5f, 1f), new Vector2(0f, -(cursor + CloseButtonSize.y * 0.5f)), CloseButtonSize,
                    BossRushUIColors.SurfaceRaised, 16f, CloseButtonSize - new Vector2(20f, 8f),
                    () => DismissActive(), true);
                BossRushUIKit.StyleSecondaryButton(closeButton);
                cursor += CloseButtonSize.y + PadBottom;

                surface.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth, Mathf.Ceil(cursor));
                BossRushUI.PlayOpenAnimation(surface);

                // 自动关闭（不阻塞官方死亡/回城流程）
                RecapAutoClose autoClose = root.AddComponent<RecapAutoClose>();
                autoClose.remainingSeconds = victory ? VictoryAutoCloseSeconds : DefeatAutoCloseSeconds;
                Transform closeText = closeButton.transform.Find("Text");
                autoClose.label = closeText != null ? closeText.GetComponent<TextMeshProUGUI>() : null;
                autoClose.baseLabel = closeLabel;
                autoClose.RefreshLabel();
                // ESC / 手柄取消 = 关闭（不再穿透去开官方暂停菜单）
                ModeGModalCancelKey.Attach(root, () => DismissActive());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] recap 面板创建失败（降级无面板）: " + e.Message);
                DismissActive();
            }
        }

        /// <summary>
        /// 关闭当前 recap 面板（幂等 no-throw）。淡出后销毁（UB-32）：引用立刻置空，
        /// 下一局的 Show / StartRun 看到的是「没有面板」，淡出中的旧根自己走完 <paramref name="fadeSeconds"/> 秒。
        /// </summary>
        internal static void DismissActive(float fadeSeconds = BossRushUIKit.CloseSeconds)
        {
            try
            {
                if (_activeRoot != null) BossRushUIKit.PlayCloseAndDestroy(_activeRoot, fadeSeconds);
            }
            catch { /* no-throw */ }
            _activeRoot = null;
        }

        /// <summary>
        /// 卡片化的一段：Card 档底 + 描边 + 左侧强调竖条（共享 CreateCard），正文左对齐、按内容量高，错峰滑入。
        /// 返回卡片高度。
        /// </summary>
        private static float PlaceSection(Transform parent, string name, int index, float top, string text,
            float fontSize, Color textColor, Color accent)
        {
            GameObject card = BossRushUI.CreateCard(name, parent, Vector2.zero,
                new Vector2(ContentWidth, 40f), BossRushUIColors.SurfaceRaised, accent, true);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 1f);
            card.GetComponent<Image>().raycastTarget = false;

            TextMeshProUGUI body = ZombieModeUIHelper.CreateText("Body", card.transform, text, fontSize,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(SectionPadLeft, -SectionPadY),
                new Vector2(ContentWidth - SectionPadLeft - SectionPadRight, 30f),
                TextAlignmentOptions.TopLeft, textColor);
            body.rectTransform.pivot = new Vector2(0f, 1f);
            body.richText = true;
            float height = BossRushUI.MeasureTextHeight(body, ContentWidth - SectionPadLeft - SectionPadRight,
                Mathf.Ceil(fontSize * 1.45f) + 4f);
            float cardHeight = Mathf.Ceil(height + SectionPadY * 2f);
            cardRect.sizeDelta = new Vector2(ContentWidth, cardHeight);
            cardRect.anchoredPosition = new Vector2(0f, -top);
            BossRushUIEntranceAnimation.Play(card, 0.08f + SectionStagger * index, 0.22f, 10f);
            return cardHeight;
        }

        private static string JoinLines(params string[] lines)
        {
            string joined = string.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                joined = joined.Length == 0 ? lines[i] : joined + "\n" + lines[i];
            }
            return joined;
        }

        /// <summary>自动倒计时关闭组件（面板 root 自驱动，无外部 owner 依赖）。关闭钮上写着还剩几秒，整秒变化时才写字。</summary>
        internal sealed class RecapAutoClose : MonoBehaviour
        {
            /// <summary>到点后的淡出时长：比手动关闭（0.12 秒）慢一点，自己消失的面板要让人看见它在走。</summary>
            private const float AutoFadeSeconds = 0.2f;

            internal float remainingSeconds;
            internal TextMeshProUGUI label;
            internal string baseLabel;
            private int _shownSeconds = -1;
            private bool _closing;

            internal void RefreshLabel()
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
                if (seconds == _shownSeconds || label == null) return;
                _shownSeconds = seconds;
                label.text = baseLabel + L10n.T("（", " (") + seconds + L10n.T("）", ")");
            }

            private void Update()
            {
                try
                {
                    if (_closing || BossRushUI.IsGamePaused()) return;
                    // 已被 DismissActive / 下一局的 Show 换下、正在淡出：不能再去关「当前」面板（那是新的一张）。
                    if (!ReferenceEquals(_activeRoot, gameObject)) { _closing = true; return; }
                    remainingSeconds -= Time.unscaledDeltaTime;
                    RefreshLabel();
                    if (remainingSeconds <= 0f)
                    {
                        _closing = true;
                        DismissActive(AutoFadeSeconds);
                    }
                }
                catch { /* no-throw */ }
            }
        }

        #endregion

        #region Line Composition（near-miss 呈现；仅终局调用一次）

        /// <summary>
        /// 本局波次是否刷新个人最佳波次。
        /// 必须与 RecordRun 之前的旧最佳值比较：RecordRun 已把本局波次并入 profile，
        /// 再读 Current 会让任何一局都「追平自己」，平纪录也误报新纪录。
        /// </summary>
        internal static bool IsNewBestWave(int wave, int previousBestWave)
        {
            return wave > 0 && wave > previousBestWave;
        }

        /// <summary>RecordRun 之前读取旧的个人最佳波次；读不到按 0 处理（首局必报新纪录）。</summary>
        internal static int ReadCurrentBestWave()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.Current;
                return profile != null ? profile.bestWaveReached : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 奖励档 near-miss：奖励件数分带 0-2→6 / 3-5→7 / 6-8→8 / 9→9 / 10-11→10。
        /// </summary>
        private static string ComposeRewardGapLine(int resolve)
        {
            int current = ModeGRewardTransaction.GetRewardItemCount(resolve);
            int nextBoundary = GetNextBandBoundary(resolve);
            if (nextBoundary < 0)
            {
                return L10n.T("BossRush_ModeG_Recap_Rewards") + " " + current
                    + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit")
                    + " · " + L10n.T("BossRush_ModeG_Recap_RewardMax");
            }
            int next = ModeGRewardTransaction.GetRewardItemCount(nextBoundary);
            return L10n.T("BossRush_ModeG_Recap_Rewards") + " " + current
                + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit")
                + " · " + L10n.T("BossRush_ModeG_Recap_ResolveGap") + " "
                + (nextBoundary - resolve) + L10n.T(" 点决意 → ", " Resolve → ")
                + next + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit");
        }

        /// <summary>下一奖励档的 Resolve 门槛（3/6/9/10）；已满档返回 -1。</summary>
        private static int GetNextBandBoundary(int resolve)
        {
            if (resolve < 3) return 3;
            if (resolve < 6) return 6;
            if (resolve < 9) return 9;
            if (resolve < 10) return 10;
            return -1;
        }

        /// <summary>
        /// 三轴本局遇上/破解计数（遇上=该轴反制生效的波数；破解=该轴决意数）。
        /// 白话（UI 共识对照审查 B-25）：「距离回声：遇上 2 波 · 破解 1 次」，不再是「2 次尝试 · 1 次破解」。
        /// </summary>
        private static string ComposeAxisLines(ModeGRuntimeModule module)
        {
            ModeGAdaptiveCombat adaptive = module.Adaptive;
            int distanceAttempts = module.AxisAttemptDistance;
            int ammoAttempts = module.AxisAttemptAmmo;
            int attributeAttempts = module.AxisAttemptAttribute;
            int distanceBreaks = adaptive != null ? adaptive.ResolveDistance : 0;
            int ammoBreaks = adaptive != null ? adaptive.ResolveAmmo : 0;
            int attributeBreaks = adaptive != null ? adaptive.ResolveAttribute : 0;

            return ComposeAxisLine("BossRush_ModeG_AxisDistance", distanceAttempts, distanceBreaks)
                + "\n" + ComposeAxisLine("BossRush_ModeG_AxisAmmo", ammoAttempts, ammoBreaks)
                + "\n" + ComposeAxisLine("BossRush_ModeG_AxisAttribute", attributeAttempts, attributeBreaks);
        }

        private static string ComposeAxisLine(string axisKey, int attempts, int breaks)
        {
            return L10n.T(axisKey) + L10n.T("：", ": ")
                + string.Format(L10n.T("BossRush_ModeG_Axis_Attempts"), attempts)
                + " · " + string.Format(L10n.T("BossRush_ModeG_Axis_Breaks"), breaks);
        }

        /// <summary>本局契约达成状态（终局进度快照评估）。</summary>
        private static string ComposeContractLine(ModeGRuntimeModule module)
        {
            try
            {
                int contractId = module.State != null ? module.State.fateContractId : -1;
                if (contractId < 0) return string.Empty;
                ModeGFateContract.ContractDef def = ModeGFateContract.GetById(contractId);
                bool fulfilled = ModeGFateContract.Evaluate(contractId, module.BuildContractProgress());
                return L10n.T("BossRush_ModeG_Recap_Contract") + ": " + def.GetDisplayName()
                    + " · " + (fulfilled
                        ? ModeGRichText.SuccessTag + L10n.T("BossRush_ModeG_Recap_ContractDone") + "</color>"
                        : ModeGRichText.DangerTag + L10n.T("BossRush_ModeG_Recap_ContractFailed") + "</color>");
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// 下一枚印章目标行（入口确认页与 recap 共用：印章条件 + 当前契约连胜）。
        /// 失败返回空串（调用方隐藏该行）。
        /// </summary>
        internal static string ComposeEntrySealLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                int streak = profile != null ? profile.contractStreak : 0;
                return L10n.T("BossRush_ModeG_Seal_EntryCondition")
                    + " · " + L10n.T("BossRush_ModeG_Seal_Streak") + " " + streak;
            }
            catch { return string.Empty; }
        }

        /// <summary>印章目标（契约连胜；下一枚印章 = 再达成 1 次契约）。</summary>
        private static string ComposeSealLine()
        {
            try
            {
                string condition = ComposeEntrySealLine();
                if (string.IsNullOrEmpty(condition)) return string.Empty;
                return L10n.T("BossRush_ModeG_Seal_NextLabel") + ": " + condition;
            }
            catch { return string.Empty; }
        }

        /// <summary>图鉴 near-miss（累计宿敌击败 -> 里程碑；纯呈现，不授予任何奖励）。</summary>
        private static string ComposeCodexLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                int kills = profile != null ? profile.totalNemesisDefeated : 0;
                int nextMilestone = -1;
                for (int i = 0; i < CodexMilestones.Length; i++)
                {
                    if (kills < CodexMilestones[i]) { nextMilestone = CodexMilestones[i]; break; }
                }
                if (nextMilestone < 0)
                {
                    return L10n.T("BossRush_ModeG_Codex") + ": " + L10n.T("BossRush_ModeG_Codex_Complete");
                }
                return L10n.T("BossRush_ModeG_Codex") + ": "
                    + L10n.T("BossRush_ModeG_Codex_Need") + " " + (nextMilestone - kills)
                    + " " + L10n.T("BossRush_ModeG_Codex_More");
            }
            catch { return string.Empty; }
        }

        /// <summary>累计纪录摘要（总场次/胜利/最佳波次/宿敌击败）。</summary>
        private static string ComposeProfileLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                if (profile == null) return string.Empty;
                return L10n.T("BossRush_ModeG_TotalRuns") + " " + profile.totalRuns
                    + " · " + L10n.T("BossRush_ModeG_TotalVictories") + " " + profile.totalVictories
                    + " · " + L10n.T("BossRush_ModeG_BestWave") + " " + profile.bestWaveReached
                    + " · " + L10n.T("BossRush_ModeG_NemesisKills") + " " + profile.totalNemesisDefeated;
            }
            catch { return string.Empty; }
        }

        #endregion
    }
}
