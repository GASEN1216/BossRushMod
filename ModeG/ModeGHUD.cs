using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
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
        private const int CanvasSortOrder = 900;

        private readonly ModeGRuntimeModule _module;
        private readonly StringBuilder _builder = new StringBuilder(128);

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
                    22f,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(16f, -16f),
                    new Vector2(420f, 150f),
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
                string text = BuildStatusText(state);
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

        private string BuildStatusText(ModeGRunState state)
        {
            _builder.Length = 0;

            // 行 1：模式名 + 幕·波次（"II · 5/9"）
            _builder.Append("<color=#B8860B>")
                .Append(L10n.T("宿命回响", "Fate Echo"))
                .Append("</color> ")
                .Append(GetActRoman(state.actIndex))
                .Append(" · ")
                .Append(state.waveEpoch + 1)
                .Append("/9");

            // 行 2：唯一反制目标（Last Stand 整数秒 > 休整倒计时 > 本波反制轴/宿敌）
            _builder.Append('\n');
            if (state.lastStandActive)
            {
                _builder.Append("<color=#FF3333>")
                    .Append(L10n.T("最后处决 ", "Last Stand "))
                    .Append(Mathf.CeilToInt(Mathf.Max(0f, state.lastStandTimer)))
                    .Append(L10n.T(" 秒", "s"))
                    .Append("</color>");
            }
            else if (state.intermissionActive)
            {
                _builder.Append(L10n.T("休整 · 下一波 ", "Intermission · next wave "))
                    .Append(Mathf.CeilToInt(Mathf.Max(0f, state.intermissionTimer)))
                    .Append(L10n.T(" 秒", "s"));
            }
            else
            {
                _builder.Append(L10n.T("反制: ", "Counter: "));
                ModeGWavePlan.WaveSlot wave = _module.WavePlan != null
                    ? _module.WavePlan.GetWave(state.waveEpoch)
                    : null;
                if (wave != null && wave.isNemesisWave)
                {
                    _builder.Append("<color=#FF8C00>")
                        .Append(L10n.T("宿敌降临", "Nemesis Descends"))
                        .Append("</color>");
                }
                else
                {
                    _builder.Append(GetAxisDisplayName(ModeGAdaptiveCombat.GetAxisForWave(state.waveEpoch)));
                }
            }

            // 行 3：Resolve X/11
            int resolve = _module.Adaptive != null ? _module.Adaptive.TotalResolve : 0;
            _builder.Append('\n')
                .Append("Resolve ")
                .Append(resolve)
                .Append('/')
                .Append(ModeGAdaptiveCombat.MaxResolveTotal);

            // 行 4：本波 Boss 进度（击杀/已提交）
            int committed = state.SlotCommitted;
            int alive = _module.SpawnTransaction != null ? _module.SpawnTransaction.ActiveBossCount : 0;
            if (committed > 0)
            {
                _builder.Append('\n')
                    .Append(L10n.T("目标 ", "Targets "))
                    .Append(Mathf.Max(0, committed - alive))
                    .Append('/')
                    .Append(committed);
            }

            return _builder.ToString();
        }

        private static string GetAxisDisplayName(ModeGCounterAxis axis)
        {
            switch (axis)
            {
                case ModeGCounterAxis.Distance:
                    return L10n.T("距离回声", "Distance Echo");
                case ModeGCounterAxis.Ammo:
                    return L10n.T("弹药点名", "Ammo Mark");
                case ModeGCounterAxis.Attribute:
                    return L10n.T("属性封锁", "Attribute Lock");
                default:
                    return L10n.T("宿命试探", "Fate Probe");
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
