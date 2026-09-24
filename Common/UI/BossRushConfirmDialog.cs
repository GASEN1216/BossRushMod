// ============================================================================
// BossRushConfirmDialog.cs - 全 Mod 共用的确认弹窗（UI 制作共识第 4、11 节）
// ============================================================================
// 2026-09-24 UI 共识对照审查：词缀解锁、鸭王杯放弃赛季 / 押品锁盘 / 战痕替换、Boss 池重置等
// 不可逆操作都没有确认。遗种巢已有两个自绘确认框，第三个出现时按共识抽成这一份共享件；
// 遗种巢的放生 / 亡命出发两个自绘框随后也迁了过来（入口在 PetNestUI 的「确认弹窗」一节），不再各写一份。
//
// 形态（照遗种巢原先的放生确认框）：标题问句 → 对象 → 正文 → 后果（红 / 黄字）→ 左「确认」右「取消」。
//   - 危险确认（Danger=true）：确认键 Danger 实心——这是「进确认之后」的那颗，允许实心红；非危险确认用 AccentFill；
//   - 画布层默认 BossRushUILayers.ModalConfirm，压在各模式面板之上；丧尸模式的模态层更高，调用方显式传层级常量；
//   - 接管输入：独占模态租约；ESC / 手柄取消 = 取消（订 UIInputManager.OnCancelEarly 并用掉事件，
//     底下还开着的面板在自己的 ESC 判据里查 IsOpen 让位）；
//   - Anchor：调用方面板被销毁或停用时自动按「取消」收场，不留抢着租约的孤儿；
//   - 一次只存在一个；再次 Show 会先按「取消」收掉上一个；关闭即销毁，不常驻。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>共享确认弹窗。一次只存在一个。</summary>
    internal sealed class BossRushConfirmDialog : MonoBehaviour
    {
        /// <summary>弹窗内容。文字都要已本地化（中英双语，只用 GBK 字形）。</summary>
        internal sealed class Options
        {
            /// <summary>标题问句（「放弃本赛季？」）。</summary>
            public string Title;
            /// <summary>作用对象（「霜狼 → 风暴海」「3 件押品」）；空表示不画。</summary>
            public string Target;
            /// <summary>正文说明；空表示不画。</summary>
            public string Body;
            /// <summary>后果（红 / 黄字）；空表示不画。</summary>
            public string Warning;
            /// <summary>确认键文案：动词 + 对象（「放弃赛季」），不用「确定」。</summary>
            public string ConfirmLabel;
            /// <summary>取消键文案；空时用「再想想」。</summary>
            public string CancelLabel;
            /// <summary>危险确认：确认键 Danger 实心、后果用 DangerText；否则 AccentFill、后果用 WarningText。</summary>
            public bool Danger = true;
            /// <summary>点确认后调用（弹窗先关、再调）。</summary>
            public Action OnConfirm;
            /// <summary>取消、ESC、Anchor 失效时调用；可空。</summary>
            public Action OnCancel;
            /// <summary>画布层级，必须是 BossRushUILayers 常量。</summary>
            public int SortingOrder = BossRushUILayers.ModalConfirm;
            /// <summary>调用方面板的根物体；它被销毁或停用时弹窗自动按取消收场。可空。</summary>
            public GameObject Anchor;
            /// <summary>
            /// 「对象」那一行建好后回调，调用方借它给那行字挂表现层小件（遗种巢：异色崽名字的流光）。
            /// 可空；Target 为空时不调；回调抛异常只记日志，弹窗照常打开。2026-09-24 迁遗种巢确认框时加的可选项。
            /// </summary>
            public Action<TMP_Text> DecorateTarget;
        }

        private const string RootName = "BossRush_ConfirmDialog";
        private const float PanelWidth = 680f;
        private const float TextWidth = 600f;

        private static BossRushConfirmDialog _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private Options _options;
        private bool _hasAnchor;
        private bool _closing;

        /// <summary>弹窗是否开着。底下面板的 ESC 判据要查它，开着时让位。</summary>
        internal static bool IsOpen
        {
            get { return _instance != null; }
        }

        /// <summary>打开确认弹窗。options 为空或没有 OnConfirm 时不弹。</summary>
        internal static void Show(Options options)
        {
            if (options == null || options.OnConfirm == null) return;
            try
            {
                if (_instance != null) _instance.Finish(false);
                GameObject host = new GameObject(RootName + "_Host");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<BossRushConfirmDialog>();
                _instance._options = options;
                _instance._hasAnchor = options.Anchor != null;
                _instance.Build();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ConfirmDialog] 打开失败: " + e.Message);
                Close();
            }
        }

        /// <summary>立即关闭，不回调。幂等。切图、卸载、宿主销毁走这里。</summary>
        internal static void Close()
        {
            BossRushConfirmDialog closing = _instance;
            _instance = null;
            if (closing == null) return;
            try
            {
                closing._closing = true;
                closing.ReleaseLease();
                if (closing._cancelKey != null) closing._cancelKey.Detach();
                if (closing.gameObject != null) UnityEngine.Object.Destroy(closing.gameObject);
            }
            catch (Exception)
            {
                // 销毁失败也已丢掉引用，避免二次 Close
            }
        }

        /// <summary>静态缓存重置（Mod 卸载），经 BossRushUI.ResetStaticCaches 调用。</summary>
        internal static void ResetStaticCaches()
        {
            Close();
        }

        private void Build()
        {
            Options o = _options;
            _canvas = BossRushUI.CreateCanvasRoot(RootName, o.SortingOrder, true);
            _canvas.transform.SetParent(transform, false);
            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(PanelWidth, 300f));
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);

            // 自上而下：标题 → 对象 → 正文 → 后果 → 按钮。高度按实测撑开，面板跟着定高。
            float y = 28f;
            TextMeshProUGUI title = CreateBlock(surface.transform, "Title", o.Title, 28f,
                BossRushUIColors.TextPrimary, ref y, 40f);
            if (title != null) title.fontStyle = FontStyles.Bold;
            y += 6f;
            TextMeshProUGUI target = CreateBlock(surface.transform, "Target", o.Target, 22f, BossRushUIColors.TextPrimary, ref y, 30f);
            if (target != null)
            {
                y += 8f;
                if (o.DecorateTarget != null)
                {
                    try { o.DecorateTarget(target); }
                    catch (Exception e) { ModBehaviour.DevLog("[ConfirmDialog] 对象行装饰失败: " + e.Message); }
                }
            }
            if (CreateBlock(surface.transform, "Body", o.Body, 18f, BossRushUIColors.TextSecondary, ref y, 26f) != null) y += 8f;
            CreateBlock(surface.transform, "Warning", o.Warning, 18f,
                o.Danger ? BossRushUIColors.DangerText : BossRushUIColors.WarningText, ref y, 26f);
            y += 22f;

            float panelHeight = Mathf.Max(240f, y + 48f + 28f);
            surface.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth, panelHeight);

            // 危险在左、取消在右，中间拉开 40（沿用遗种巢原先放生 / 亡命出发确认框的排法）
            float buttonY = -panelHeight * 0.5f + 28f + 24f;
            ZombieModeUIHelper.CreateButton(
                "Confirm", surface.transform,
                string.IsNullOrEmpty(o.ConfirmLabel) ? L10n.T("确认", "Confirm") : o.ConfirmLabel,
                new Vector2(0.5f, 0.5f), new Vector2(-130f, buttonY), new Vector2(220f, 48f),
                o.Danger ? BossRushUIColors.Danger : BossRushUIColors.AccentFill, 19f, new Vector2(210f, 44f),
                delegate { Finish(true); }, true);
            Button cancel = ZombieModeUIHelper.CreateButton(
                "Cancel", surface.transform,
                string.IsNullOrEmpty(o.CancelLabel) ? L10n.T("再想想", "Not now") : o.CancelLabel,
                new Vector2(0.5f, 0.5f), new Vector2(130f, buttonY), new Vector2(220f, 48f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(210f, 44f),
                delegate { Finish(false); }, true);
            BossRushUIKit.StyleSecondaryButton(cancel);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "ConfirmDialog");
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, delegate { Finish(false); }, null);
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>从面板顶边往下排一段居中文字；text 为空时不建、返回 null。</summary>
        private static TextMeshProUGUI CreateBlock(Transform parent, string name, string text, float fontSize,
            Color color, ref float y, float minHeight)
        {
            if (string.IsNullOrEmpty(text)) return null;
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                name, parent, text, fontSize,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(TextWidth, minHeight),
                TextAlignmentOptions.Center, color);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -y);
            BossRushUI.ApplyGameFont(label);
            y += BossRushUI.MeasureTextHeight(label, TextWidth, minHeight);
            return label;
        }

        /// <summary>Anchor 失效就按取消收场。只在有 Anchor 时每帧判一次（弹窗开着才存在）。</summary>
        private void Update()
        {
            if (!_hasAnchor || _closing || _options == null) return;
            GameObject anchor = _options.Anchor;
            if (anchor == null || !anchor.activeInHierarchy) Finish(false);
        }

        private void Finish(bool confirmed)
        {
            if (_closing) return;
            _closing = true;
            Options o = _options;
            ReleaseLease();
            if (_cancelKey != null) _cancelKey.Detach();
            if (_instance == this) _instance = null;
            try
            {
                PetNestUI.FadeOutAndDestroy(gameObject, _canvas);
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
            try
            {
                Action callback = confirmed ? o.OnConfirm : o.OnCancel;
                if (callback != null) callback();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ConfirmDialog] 回调失败: " + e.Message);
            }
        }

        private void ReleaseLease()
        {
            try
            {
                if (_modalLease != null)
                {
                    _modalLease.Release();
                    _modalLease = null;
                }
            }
            catch (Exception)
            {
                // 释放失败也要丢引用，避免二次 Release
            }
        }

        private void OnDestroy()
        {
            ReleaseLease();
            if (_cancelKey != null) _cancelKey.Detach();
            if (_instance == this) _instance = null;
        }
    }
}
