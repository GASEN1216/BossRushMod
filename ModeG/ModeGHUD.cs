using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 本波唯一反制目标的呈现状态（§15）。
    /// </summary>
    internal enum ModeGObjectiveState
    {
        /// <summary>本波无反制轴（第 1 波，或距离/属性未形成有效目标）</summary>
        NoCounter,
        /// <summary>反制有效，进度可得分</summary>
        Active,
        /// <summary>双门槛已达标，波次完成即破解</summary>
        ThresholdsMet,
        /// <summary>本波挑战无效（污染 / telemetry 溢出）：不显示可得分进度</summary>
        Invalid,
        /// <summary>宿敌未学会新弹药（样本不足 / 无新候选）</summary>
        NoAmmoCandidate,
        /// <summary>弹药禁令已违规，本波不可再得分</summary>
        AmmoViolated
    }

    /// <summary>
    /// Mode G HUD 只读视图模型（§15）。
    ///
    /// 由 <see cref="ModeGRuntimeModule.BuildHudModel"/> 在 4Hz 节流后构建一次，
    /// HUD 只负责格式化，不反向读取 RunState/遥测/自适应对象。
    /// 进度数值全部来自 <see cref="ModeGAxisProgress"/>，与破解结算同一口径。
    /// </summary>
    internal struct ModeGHudModel
    {
        public int actIndex;
        public int waveNumber;              // 1-based
        public int resolve;
        public int resolveMax;

        public ModeGCounterAxis axis;
        public ModeGObjectiveState objectiveState;
        public ModeGAxisProgress progress;

        /// <summary>距离轴目标极端带（Close = 需贴近 / Far = 需拉开）</summary>
        public ModeGDistanceVerdict distanceTargetBand;
        /// <summary>属性轴被封锁侧</summary>
        public ModeGDirectDamageClass attributeLockedFamily;
        /// <summary>被点名弹药展示名（弹药轴）</summary>
        public string bannedAmmoName;

        public bool isNemesisWave;
        public string nemesisName;
        public int nemesisRank;
        public ModeGNemesisTemperament nemesisTemperament;

        public string contractTitle;

        public bool lastStandActive;
        public int lastStandSeconds;
        public bool intermissionActive;
        public int intermissionSeconds;
        /// <summary>休整最后 2 秒的 CalmGate 停火倒计时</summary>
        public bool calmGateActive;
        /// <summary>休整期下一波反制预告文本（可为空）</summary>
        public string nextWavePreview;

        public int targetsKilled;
        public int targetsCommitted;
    }

    /// <summary>
    /// Mode G 富文本配色：token 预先转成的开标签（HUD、终局横幅、recap 共用），类型初始化时算一次，不每帧拼。
    /// 旧写法是 7 种网页命名色（#B8860B / #FFD700 / #FF3333 / #FF8C00 / #B22222 / #2E8B57 / #9FB4C7），
    /// 自成一套配色，#B22222 压在深底上只有约 3.1:1（2026-09-23 审美审查 UB-05 / UB-20）。
    /// </summary>
    internal static class ModeGRichText
    {
        internal static readonly string WarningTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        internal static readonly string DangerTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";
        internal static readonly string SuccessTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText) + ">";
        internal static readonly string SecondaryTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">";
    }

    /// <summary>
    /// Mode G 运行时 HUD（规格 §15/§17 重写版）。
    ///
    /// 硬约束：
    /// - 运行时 Canvas（ScreenSpaceOverlay）一次创建，Dispose 幂等销毁；
    /// - 文本刷新 <=4Hz（0.25s 节流），字符串仅在内容变化时赋值；
    /// - 行规格：幕·波次（"II · 5/9"）/ 唯一反制目标（本波轴或宿敌）/
    ///   Resolve X/11 / Last Stand 整数秒倒计时（休整倒计时同行替代）；
    /// - 全程防御式 try/catch，Canvas 创建失败降级为静默无 HUD（不影响 run）。
    ///
    /// 【底板与版式（2026-09-23 审美审查 UB-05）】旧版是一块没有底板的裸字：亮地面、雪地上白字全靠运气，
    /// 自动缩字又开着，宿敌名或目标行一变长整块字号就缩一档。现在文字压在一张 Card 档卡片上
    /// （共享库的描边、投影与左侧强调竖条），不缩字、自动换行，卡片高度只在文本变化时按内容量一次。
    /// 位置 (16, -140)：左上角是官方时间显示，照随机事件徽章（RandomEventsTuning.HudBadgeMarginY）的口径让开它；
    /// Mode G 不 roll 变异词条，左上 -240 以下的词条面板不会同时出现。
    /// </summary>
    internal sealed class ModeGHUD : IDisposable
    {
        /// <summary>刷新间隔（4Hz 上限，规格 §15/§17 冻结）</summary>
        private const float RefreshIntervalSeconds = 0.25f;
        private const string RootName = "ModeG_Hud";
        private const int CanvasSortOrder = BossRushUILayers.ModeGHud;
        private const float CardLeft = 16f;
        private const float CardTop = 140f;
        private const float CardWidth = 440f;
        /// <summary>文字左边距：卡片左侧 3–7 是强调竖条，再留 11。</summary>
        private const float TextInsetLeft = 18f;
        private const float TextInsetRight = 14f;
        private const float TextInsetY = 10f;
        /// <summary>正文 16、首行（模式名 · 幕波次）18，常驻 HUD 正文档（AGENTS §4.14 字号梯度）。</summary>
        private const float BodyFontSize = 16f;

        private readonly ModeGRuntimeModule _module;
        private readonly StringBuilder _builder = new StringBuilder(256);

        private GameObject _root;
        private RectTransform _cardRect;
        private TextMeshProUGUI _statusText;
        private string _lastText = string.Empty;
        private float _refreshTimer;
        private bool _disposed;

        /// <summary>当前 HUD 文本快照（诊断用；可见渲染以 Canvas 为准）</summary>
        public string CurrentText { get { return _lastText; } }

        public ModeGHUD(ModeGRuntimeModule module)
        {
            if (module == null) throw new ArgumentNullException("module");
            _module = module;
            _refreshTimer = RefreshIntervalSeconds; // 首帧立即刷新
            TryCreateCanvas();
        }

        #region Canvas Construction

        private void TryCreateCanvas()
        {
            try
            {
                Canvas canvas = BossRushUI.CreateCanvasRoot(RootName, CanvasSortOrder, false);
                GameObject root = canvas.gameObject;
                _root = root; // 创建后立即归属 owner，后续构建失败也能销毁。
                UnityEngine.Object.DontDestroyOnLoad(root);

                // 底板：Surface 压到 0.85，仍在共享库投影的门槛（0.75）之上；强调竖条取 Mode G 的金色。
                Color surface = BossRushUIColors.Surface;
                surface.a = 0.85f;
                GameObject card = BossRushUI.CreateCard("ModeG_HudCard", root.transform, Vector2.zero,
                    new Vector2(CardWidth, 64f), surface, BossRushUIColors.WarningText, true);
                _cardRect = card.GetComponent<RectTransform>();
                _cardRect.anchorMin = _cardRect.anchorMax = _cardRect.pivot = new Vector2(0f, 1f);
                _cardRect.anchoredPosition = new Vector2(CardLeft, -CardTop);
                card.GetComponent<Image>().raycastTarget = false;

                // 状态文本：卡片左上角锚定，富文本（color / size 标签）；不缩字、自动换行，高度由 FitCard 按内容量。
                _statusText = ZombieModeUIHelper.CreateText(
                    "ModeG_Status",
                    card.transform,
                    string.Empty,
                    BodyFontSize,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(TextInsetLeft, -TextInsetY),
                    new Vector2(CardWidth - TextInsetLeft - TextInsetRight, 30f),
                    TextAlignmentOptions.TopLeft,
                    BossRushUIColors.TextPrimary);
                _statusText.rectTransform.pivot = new Vector2(0f, 1f);
                _statusText.richText = true;
                card.SetActive(false); // 第一次拿到文本再出现，不闪一张空卡
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] HUD Canvas 创建失败（降级无 HUD）: " + e.Message);
                Dispose();
            }
        }

        #endregion

        #region Frame Drive（4Hz 节流）

        public void Update(float deltaTime)
        {
            if (_disposed) return;

            ModeGRunState state = _module.State;
            if (state == null || state.lifecyclePhase == ModeGLifecyclePhase.None)
            {
                SetVisible(false);
                return;
            }

            // 常驻 HUD 跟随官方界面与暂停菜单收起（ModeGHud 900 在 sortingOrder 100 的背包 / 地图上面，2026-09-14）。
            // 隐藏期间不刷新文本，关掉背包后按 4Hz 节奏补上。
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            SetVisible(visible);
            if (!visible) return;

            _refreshTimer += deltaTime;
            if (_refreshTimer < RefreshIntervalSeconds) return;
            _refreshTimer = 0f;

            try
            {
                string text = BuildStatusText(_module.BuildHudModel());
                if (!string.Equals(text, _lastText, StringComparison.Ordinal))
                {
                    _lastText = text;
                    if (_statusText != null)
                    {
                        _statusText.text = text;
                        FitCard();
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] HUD 刷新异常: " + e.Message);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            if (_root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>
        /// 卡片收到文本高度。只在文本变化时调（≤4Hz），不是每帧路径：MeasureTextHeight 关掉自动缩字、打开换行，
        /// 按实际内容量高，宿敌名再长也只是多一行，不再整块缩一档字号。
        /// </summary>
        private void FitCard()
        {
            if (_cardRect == null) return;
            if (!_cardRect.gameObject.activeSelf) _cardRect.gameObject.SetActive(true);
            float height = BossRushUI.MeasureTextHeight(_statusText,
                CardWidth - TextInsetLeft - TextInsetRight, Mathf.Ceil(BodyFontSize * 1.45f) + 4f);
            _cardRect.sizeDelta = new Vector2(CardWidth, Mathf.Ceil(height + TextInsetY * 2f));
        }

        #endregion

        #region Text Composition（§15 行规格）

        private string BuildStatusText(ModeGHudModel m)
        {
            _builder.Length = 0;

            // 行 1：模式名 + 幕·波次（"II · 5/9"），比正文大一档
            _builder.Append("<size=18>").Append(ModeGRichText.WarningTag)
                .Append(L10n.T("BossRush_ModeG_Preview"))
                .Append("</color> ")
                .Append(GetActRoman(m.actIndex))
                .Append(" · ")
                .Append(m.waveNumber)
                .Append("/9</size>");

            if (m.intermissionActive)
            {
                // 休整：倒计时 + 下一波反制预告（波开始前的可操作提前量）
                _builder.Append('\n')
                    .Append(L10n.T("BossRush_ModeG_Hud_Intermission"))
                    .Append(' ')
                    .Append(m.intermissionSeconds)
                    .Append(L10n.T("BossRush_ModeG_Hud_Seconds"));
                if (m.calmGateActive)
                {
                    _builder.Append(" · ").Append(ModeGRichText.WarningTag)
                        .Append(L10n.T("BossRush_ModeG_Hud_CalmGate"))
                        .Append("</color>");
                }
                if (!string.IsNullOrEmpty(m.nextWavePreview))
                {
                    _builder.Append('\n')
                        .Append(L10n.T("BossRush_ModeG_Hud_NextWave"))
                        .Append(": ")
                        .Append(m.nextWavePreview);
                }
            }
            else
            {
                // 行 2：本波唯一反制目标（宿敌波同时给出宿敌身份与 Rank）
                _builder.Append('\n')
                    .Append(L10n.T("BossRush_ModeG_Hud_Counter"))
                    .Append(' ')
                    .Append(GetAxisDisplayName(m.axis));
                if (m.isNemesisWave) AppendNemesis(m);

                // 行 3：唯一目标的可验证进度 / 无效原因
                string objective = ComposeObjectiveLine(m);
                if (!string.IsNullOrEmpty(objective)) _builder.Append('\n').Append(objective);
            }

            // Last Stand 只作为短时倒计时覆盖在反制目标下方，不构成第二份常驻目标清单
            if (m.lastStandActive)
            {
                _builder.Append('\n').Append(ModeGRichText.DangerTag)
                    .Append(L10n.T("BossRush_ModeG_Hud_LastStand"))
                    .Append(' ')
                    .Append(m.lastStandSeconds)
                    .Append(L10n.T("BossRush_ModeG_Hud_Seconds"))
                    .Append("</color>");
            }

            // 决意 X/11 + 本局契约短标题（详情只在入口/recap 展开）；中文界面统一叫「决意」，英文保留 Resolve
            _builder.Append('\n').Append(L10n.T("决意", "Resolve")).Append(' ')
                .Append(m.resolve)
                .Append('/')
                .Append(m.resolveMax);
            if (!string.IsNullOrEmpty(m.contractTitle))
            {
                _builder.Append(" · ").Append(ModeGRichText.SecondaryTag).Append(m.contractTitle).Append("</color>");
            }

            // 本波 Boss 进度（击杀/已提交）
            if (m.targetsCommitted > 0)
            {
                _builder.Append('\n')
                    .Append(L10n.T("BossRush_ModeG_Hud_Targets"))
                    .Append(' ')
                    .Append(m.targetsKilled)
                    .Append('/')
                    .Append(m.targetsCommitted);
            }

            return _builder.ToString();
        }

        /// <summary>宿敌身份与本次登场 Rank（§15：HUD 需显示宿敌名称与 Rank）。</summary>
        private void AppendNemesis(ModeGHudModel m)
        {
            _builder.Append(" · ").Append(ModeGRichText.DangerTag);
            _builder.Append(string.IsNullOrEmpty(m.nemesisName)
                ? L10n.T("BossRush_ModeG_Hud_Nemesis")
                : m.nemesisName);
            if (m.nemesisRank > 0)
            {
                _builder.Append(' ').Append(L10n.T("BossRush_ModeG_RankWord"))
                    .Append(' ').Append(m.nemesisRank);
            }
            _builder.Append("</color>");
            string temperament = ModeGAdaptiveCombat.GetTemperamentDisplayName(m.nemesisTemperament);
            if (!string.IsNullOrEmpty(temperament)) _builder.Append(" · ").Append(temperament);
        }

        /// <summary>
        /// 唯一目标行（§15）。无效/无反制状态一律不显示可得分进度。
        /// 百分比向下取整，保证「显示达标」与「实际达标」永不错位。
        /// </summary>
        private static string ComposeObjectiveLine(ModeGHudModel m)
        {
            switch (m.objectiveState)
            {
                case ModeGObjectiveState.NoCounter:
                    return L10n.T("BossRush_ModeG_Hud_NoCounter");
                case ModeGObjectiveState.Invalid:
                    return ModeGRichText.DangerTag + L10n.T("BossRush_ModeG_Hud_Invalid") + "</color>";
                case ModeGObjectiveState.NoAmmoCandidate:
                    return L10n.T("BossRush_ModeG_Hud_NoAmmoCandidate");
                case ModeGObjectiveState.AmmoViolated:
                    return L10n.T("BossRush_ModeG_Hud_BanPrefix") + m.bannedAmmoName
                        + " · " + ModeGRichText.DangerTag + L10n.T("BossRush_ModeG_Hud_BanViolated") + "</color>";
            }

            if (m.axis == ModeGCounterAxis.Ammo)
            {
                return L10n.T("BossRush_ModeG_Hud_BanPrefix") + m.bannedAmmoName
                    + " · " + ModeGRichText.SuccessTag + L10n.T("BossRush_ModeG_Hud_BanClean") + "</color>";
            }

            // 属性轴双门槛达标后仍需相反武器系末击，继续显示具体收尾要求。
            if (m.objectiveState == ModeGObjectiveState.ThresholdsMet
                && m.axis == ModeGCounterAxis.Distance)
            {
                return ModeGRichText.SuccessTag + L10n.T("BossRush_ModeG_Hud_WillBreak") + "</color>";
            }

            string prefix;
            if (m.axis == ModeGCounterAxis.Distance)
            {
                prefix = m.distanceTargetBand == ModeGDistanceVerdict.Far
                    ? L10n.T("BossRush_ModeG_Hud_NeedFar")
                    : L10n.T("BossRush_ModeG_Hud_NeedClose");
            }
            else
            {
                // 属性轴：先说明被封锁侧，再给出相反系的双门槛进度与终结要求
                string locked = m.attributeLockedFamily == ModeGDirectDamageClass.Gun
                    ? L10n.T("BossRush_ModeG_Hud_FamilyGun")
                    : L10n.T("BossRush_ModeG_Hud_FamilyMelee");
                string needed = m.attributeLockedFamily == ModeGDirectDamageClass.Gun
                    ? L10n.T("BossRush_ModeG_Hud_FamilyMelee")
                    : L10n.T("BossRush_ModeG_Hud_FamilyGun");
                prefix = locked + L10n.T("BossRush_ModeG_Hud_LockedSuffix") + " · " + needed;
            }

            return prefix + " " + FormatGate(m.progress.share, m.progress.shareTarget)
                + " · " + L10n.T("BossRush_ModeG_Hud_ContribWord") + " "
                + FormatGate(m.progress.contribution, m.progress.contributionTarget)
                + (m.axis == ModeGCounterAxis.Attribute
                    ? " · " + L10n.T("BossRush_ModeG_Hud_NeedTerminal")
                    : string.Empty);
        }

        /// <summary>门槛进度 "24%/35%"；当前值向下取整，避免显示达标而实际未达标。</summary>
        private static string FormatGate(float current, float target)
        {
            int shown = Mathf.Clamp(Mathf.FloorToInt(current * 100f), 0, 999);
            return shown + "%/" + Mathf.RoundToInt(target * 100f) + "%";
        }

        private static string GetAxisDisplayName(ModeGCounterAxis axis)
        {
            switch (axis)
            {
                case ModeGCounterAxis.Distance:
                    return L10n.T("BossRush_ModeG_AxisDistance");
                case ModeGCounterAxis.Ammo:
                    return L10n.T("BossRush_ModeG_AxisAmmo");
                case ModeGCounterAxis.Attribute:
                    return L10n.T("BossRush_ModeG_AxisAttribute");
                default:
                    return L10n.T("BossRush_ModeG_Hud_FateProbe");
            }
        }

        /// <summary>幕序号罗马数字（I/II/III，越界钳制）</summary>
        private static string GetActRoman(int actIndex)
        {
            if (actIndex <= 0) return "I";
            if (actIndex == 1) return "II";
            return "III";
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                }
            }
            catch { /* no-throw */ }
            _root = null;
            _cardRect = null;
            _statusText = null;
            _lastText = string.Empty;
        }
    }
}
