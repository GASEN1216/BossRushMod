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
    /// Mode G 运行时 HUD（规格 §15/§17 重写版）。
    ///
    /// 硬约束：
    /// - 运行时 Canvas（ScreenSpaceOverlay）一次创建，Dispose 幂等销毁；
    /// - 文本刷新 <=4Hz（0.25s 节流），字符串仅在内容变化时赋值；
    /// - 行规格：幕·波次（"II · 5/9"）/ 唯一反制目标（本波轴或宿敌）/
    ///   Resolve X/11 / Last Stand 整数秒倒计时（休整倒计时同行替代）；
    /// - 全程防御式 try/catch，Canvas 创建失败降级为静默无 HUD（不影响 run）。
    /// </summary>
    internal sealed class ModeGHUD : IDisposable
    {
        /// <summary>刷新间隔（4Hz 上限，规格 §15/§17 冻结）</summary>
        private const float RefreshIntervalSeconds = 0.25f;
        private const string RootName = "ModeG_Hud";
        private const int CanvasSortOrder = BossRushUILayers.ModeGHud;

        private readonly ModeGRuntimeModule _module;
        private readonly StringBuilder _builder = new StringBuilder(256);

        private GameObject _root;
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
                GameObject root = new GameObject(RootName);
                UnityEngine.Object.DontDestroyOnLoad(root);

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = CanvasSortOrder;

                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

                root.AddComponent<CanvasRenderer>();

                // 状态文本：左上角锚定，富文本（color 标签）
                _statusText = ZombieModeUIHelper.CreateText(
                    "ModeG_Status",
                    root.transform,
                    string.Empty,
                    20f,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(16f, -16f),
                    new Vector2(560f, 210f),
                    TextAlignmentOptions.TopLeft,
                    new Color(0.92f, 0.94f, 0.95f, 1f));
                _statusText.richText = true;
                _statusText.enableWordWrapping = false;

                _root = root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] HUD Canvas 创建失败（降级无 HUD）: " + e.Message);
                _root = null;
                _statusText = null;
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

            SetVisible(true);

            _refreshTimer += deltaTime;
            if (_refreshTimer < RefreshIntervalSeconds) return;
            _refreshTimer = 0f;

            try
            {
                string text = BuildStatusText(_module.BuildHudModel());
                if (!string.Equals(text, _lastText, StringComparison.Ordinal))
                {
                    _lastText = text;
                    if (_statusText != null) _statusText.text = text;
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

        #endregion

        #region Text Composition（§15 行规格）

        private string BuildStatusText(ModeGHudModel m)
        {
            _builder.Length = 0;

            // 行 1：模式名 + 幕·波次（"II · 5/9"）
            _builder.Append("<color=#B8860B>")
                .Append(L10n.T("BossRush_ModeG_Preview"))
                .Append("</color> ")
                .Append(GetActRoman(m.actIndex))
                .Append(" · ")
                .Append(m.waveNumber)
                .Append("/9");

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
                    _builder.Append(" · <color=#FFD700>")
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
                _builder.Append("\n<color=#FF3333>")
                    .Append(L10n.T("BossRush_ModeG_Hud_LastStand"))
                    .Append(' ')
                    .Append(m.lastStandSeconds)
                    .Append(L10n.T("BossRush_ModeG_Hud_Seconds"))
                    .Append("</color>");
            }

            // Resolve X/11 + 本局契约短标题（详情只在入口/recap 展开）
            _builder.Append("\nResolve ")
                .Append(m.resolve)
                .Append('/')
                .Append(m.resolveMax);
            if (!string.IsNullOrEmpty(m.contractTitle))
            {
                _builder.Append(" · <color=#9FB4C7>").Append(m.contractTitle).Append("</color>");
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
            _builder.Append(" · <color=#FF8C00>");
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
                    return "<color=#B22222>" + L10n.T("BossRush_ModeG_Hud_Invalid") + "</color>";
                case ModeGObjectiveState.NoAmmoCandidate:
                    return L10n.T("BossRush_ModeG_Hud_NoAmmoCandidate");
                case ModeGObjectiveState.AmmoViolated:
                    return L10n.T("BossRush_ModeG_Hud_BanPrefix") + m.bannedAmmoName
                        + " · <color=#B22222>" + L10n.T("BossRush_ModeG_Hud_BanViolated") + "</color>";
            }

            if (m.axis == ModeGCounterAxis.Ammo)
            {
                return L10n.T("BossRush_ModeG_Hud_BanPrefix") + m.bannedAmmoName
                    + " · <color=#2E8B57>" + L10n.T("BossRush_ModeG_Hud_BanClean") + "</color>";
            }

            if (m.objectiveState == ModeGObjectiveState.ThresholdsMet)
            {
                return "<color=#2E8B57>" + L10n.T("BossRush_ModeG_Hud_WillBreak") + "</color>";
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
            _statusText = null;
            _lastText = string.Empty;
        }
    }
}
