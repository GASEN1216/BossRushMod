using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 玩家在地图选择确认后、场景切换前可设置投入金额。100 现金 = 1 净化点数（向下取整）。
        // 投入 0 合法（直接跳过）；现金不足时拒绝并保留弹窗。
        public bool ConfigureZombieModePendingCashInvestment(long requestedAmount, out string failureReasonKey)
        {
            failureReasonKey = null;
            if (zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.SelectingMap)
            {
                failureReasonKey = "BossRush_ZombieMode_NotInitialized";
                return false;
            }

            if (requestedAmount < 0L)
            {
                requestedAmount = 0L;
            }

            if (requestedAmount > 0L)
            {
                try
                {
                    Duckov.Economy.Cost preview = new Duckov.Economy.Cost();
                    preview.money = requestedAmount;
                    if (!Duckov.Economy.EconomyManager.IsEnough(preview, true, true))
                    {
                        failureReasonKey = "BossRush_ZombieMode_CashPrompt_NotEnough";
                        return false;
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] ConfigureZombieModePendingCashInvestment: " + e.Message);
                    failureReasonKey = "BossRush_ZombieMode_CashPrompt_NotEnough";
                    return false;
                }
            }

            zombieModeRunState.PendingCashInvestment = requestedAmount;
            return true;
        }

        public long GetZombieModePendingCashInvestment()
        {
            return zombieModeRunState != null ? zombieModeRunState.PendingCashInvestment : 0L;
        }

        public int PreviewZombieModeInitialPurificationPoints()
        {
            long amount = GetZombieModePendingCashInvestment();
            if (amount <= 0L)
            {
                return 0;
            }
            return (int)System.Math.Min(int.MaxValue, amount / ZombieModeTuning.CashToPurificationRatio);
        }

        // 由地图选择 UI 在玩家点击末日丧尸地图条目时调用，弹出现金投入对话框。
        // onConfirmed：玩家确认投入额（含 0），由调用方推进 Phase1 状态机进入加载场景。
        // onCancelled：玩家选择"返回"，调用方应仅清掉「弹窗已开」标记，不要 Close 整个 MapSelectionView。
        public void ShowZombieModeCashInvestmentPrompt(System.Action onConfirmed, System.Action onCancelled = null)
        {
            GameObject root = new GameObject("ZombieMode_CashInvestmentPrompt");
            ZombieModeCashInvestmentView view = root.AddComponent<ZombieModeCashInvestmentView>();
            view.Initialize(this, onConfirmed, onCancelled);
        }
    }

    public sealed class ZombieModeCashInvestmentView : MonoBehaviour
    {
        // 2026-09-23 审美审查（UC-11 / UC-12 / UC-21 / UC-25 / UC-26 / UC-07）：
        // 旧版自带 14 色私有调色板（「返回」是危险色的红、确认悬停白字只有 3.05:1）、输入框靠两层直角方块拼边、「Max」写死英文。
        // 现在颜色全走 token：确认是本屏唯一主操作（AccentFill），放在最右；返回走共享次级样式放左边；
        // 输入框走按钮档圆角底图 + 描边（超额时描边换 DangerText）；标题行不垫直角色条；ESC 等同「返回」。
        // 2026-09-24 UI 共识对照审查 B-26：去掉「跳过（投入 0）」——金额默认 0，主按钮按金额改写成「不投入，直接出发」/「投入并出发」；
        // 字号收成四级：标题 26 / 输入与预览 18 / 正文、标签与按钮 16 / 快捷键与错误 14。
        private ModBehaviour owner;
        private System.Action onConfirmed;
        private System.Action onCancelled;
        private bool dispatched; // 防止 Confirm/Cancel 重入
        private ZombieModeUIHelper.ModalInputLease inputLease;

        private TMP_InputField amountField;
        private TextMeshProUGUI errorText;
        private TextMeshProUGUI previewText;
        private TextMeshProUGUI balanceText;
        private TextMeshProUGUI confirmLabel;
        private Image inputFrame;

        public void Initialize(ModBehaviour newOwner, System.Action newOnConfirmed, System.Action newOnCancelled)
        {
            owner = newOwner;
            onConfirmed = newOnConfirmed;
            onCancelled = newOnCancelled;
            Build();
            ClaimInputAndPause();
            UpdatePreview();
            if (amountField != null)
            {
                amountField.Select();
                amountField.ActivateInputField();
            }
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 比 MapSelectionView 高一层，但避免压住游戏顶层错误提示。
            canvas.sortingOrder = BossRushUILayers.ZombieModalInput;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(780f, 460f),
                BossRushUIColors.Accent);

            // ────────────────────────────────────────────────────────────
            // 以下所有子元素均使用「top-stretch」锚定（anchorMin(0,1) anchorMax(1,1)）
            // Y 偏移从面板顶部向下计算，CanvasScaler 负责分辨率缩放。
            // ────────────────────────────────────────────────────────────

            // ── 标题行（不垫底色条：直角色条会在圆角外露出方角） ──
            float yPos = 0f;
            float headerH = 68f;
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", panel.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_Title"), 26,
                new Vector2(0f, 1f), new Vector2(0.64f, 1f),
                new Vector2(8f, -(headerH * 0.5f)), new Vector2(-40f, 48f),
                TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;

            balanceText = ZombieModeUIHelper.CreateText("Balance", panel.transform,
                GetBalanceLabel(), 16,
                new Vector2(0.64f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -(headerH * 0.5f)), new Vector2(-32f, 40f),
                TextAlignmentOptions.MidlineRight, BossRushUIColors.WarningText);

            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f,
                new Color(BossRushUIColors.Accent.r, BossRushUIColors.Accent.g, BossRushUIColors.Accent.b, 0.6f));
            yPos += 8f;

            // ── 正文说明 ──
            float bodyH = 64f;
            ZombieModeUIHelper.CreateText("Body", panel.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_Body"), 16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + bodyH * 0.5f)), new Vector2(-56f, bodyH),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            yPos += bodyH + 10f;

            // ── 输入行（行本身不垫底色，靠留白与分隔线分区） ──
            float rowH = 48f;
            GameObject row = ZombieModeUIHelper.CreateRect("InputRow", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + rowH * 0.5f)), new Vector2(-40f, rowH), new Vector2(0.5f, 0.5f));

            // 标签（行内左侧 2%~18%）
            ZombieModeUIHelper.CreateText("Label", row.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_AmountLabel"), 16,
                new Vector2(0.02f, 0f), new Vector2(0.18f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);

            // 输入框（行内 20%~50%）：按钮档圆角底图 + 描边，描边在超额时换成 DangerText。
            GameObject inputObj = ZombieModeUIHelper.CreateRect("Input", row.transform,
                new Vector2(0.20f, 0.10f), new Vector2(0.50f, 0.90f),
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image inputBg = inputObj.AddComponent<Image>();
            inputBg.color = BossRushUIColors.Surface;
            inputFrame = BossRushUI.ApplyFramedPanelSkin(inputBg, 6, BossRushUISkinPart.Button);
            amountField = inputObj.AddComponent<TMP_InputField>();
            amountField.contentType = TMP_InputField.ContentType.IntegerNumber;

            GameObject textArea = ZombieModeUIHelper.CreateRect("TextArea", inputObj.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-16f, -4f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI inputText = ZombieModeUIHelper.CreateTMPText(textArea, "0", 18,
                TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);
            inputText.raycastTarget = false;
            amountField.targetGraphic = inputBg;
            amountField.textComponent = inputText;
            amountField.textViewport = textArea.GetComponent<RectTransform>();
            amountField.lineType = TMP_InputField.LineType.SingleLine;
            amountField.customCaretColor = true;
            amountField.caretColor = BossRushUIColors.Accent;
            amountField.selectionColor = new Color(BossRushUIColors.Accent.r, BossRushUIColors.Accent.g, BossRushUIColors.Accent.b, 0.35f);
            amountField.text = "0";
            amountField.onValueChanged.AddListener(delegate { UpdatePreview(); });

            // 快捷按钮（行内 55%/70%/85%）；「全部」走本地化，不再半中半英。
            CreateQuickButton(row.transform, "+100", "+100", 0.55f, QuickAmountAdd(100));
            CreateQuickButton(row.transform, "+1000", "+1000", 0.70f, QuickAmountAdd(1000));
            CreateQuickButton(row.transform, "Max", L10n.T("BossRush_ZombieMode_CashPrompt_Max"), 0.85f, QuickAmountMax());

            yPos += rowH + 10f;

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep1", panel.transform,
                new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, -yPos), 1f,
                BossRushUIColors.Divider);
            yPos += 8f;

            // ── 预览条（带高亮背景） ──
            float previewH = 34f;
            Color success = BossRushUIColors.Success;
            previewText = ZombieModeUIHelper.CreateHighlightBar("Preview", panel.transform, "", 18,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + previewH * 0.5f)), new Vector2(-40f, previewH),
                TextAlignmentOptions.Center, BossRushUIColors.SuccessText, new Color(success.r, success.g, success.b, 0.14f));
            yPos += previewH + 6f;

            // ── 错误条 ──
            float errH = 24f;
            errorText = ZombieModeUIHelper.CreateText("Err", panel.transform, "", 14,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + errH * 0.5f)), new Vector2(-40f, errH),
                TextAlignmentOptions.Center, BossRushUIColors.DangerText);
            yPos += errH + 8f;

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep2", panel.transform,
                new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, -yPos), 1f,
                BossRushUIColors.Divider);
            yPos += 14f;

            // ── 底部按钮：固定到底部按钮栏，避免内容高度变化时被压到面板外 ──
            GameObject actionRow = ZombieModeUIHelper.CreateRect("ActionButtonRow", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 42f), new Vector2(-40f, 56f), new Vector2(0.5f, 0.5f));
            HorizontalLayoutGroup buttonLayout = actionRow.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 22f;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlWidth = false;
            buttonLayout.childControlHeight = false;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.childForceExpandHeight = false;

            // 返回在左、主操作在右（布局组按子物体顺序从左到右排）。主按钮文案随金额改写，见 UpdatePreview。
            CreateActionButton(actionRow.transform, "Cancel",
                L10n.T("BossRush_ZombieMode_CashPrompt_Cancel"), false, 2);
            confirmLabel = CreateActionButton(actionRow.transform, "Confirm",
                L10n.T("BossRush_ZombieMode_CashPrompt_SkipZero"), true, 0);

            BossRushUI.PlayOpenAnimation(panel);
            ModBehaviour.DevLog("[ZombieMode] 现金投入弹窗已创建底部操作按钮");
        }

        private string GetBalanceLabel()
        {
            try
            {
                long money = Duckov.Economy.EconomyManager.Money;
                return string.Format("{0}: {1:n0}", L10n.T("BossRush_ZombieMode_CashPrompt_Balance"), money);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void UpdatePreview()
        {
            if (previewText == null)
            {
                return;
            }

            long amount = 0L;
            if (amountField != null && !string.IsNullOrEmpty(amountField.text))
            {
                long.TryParse(amountField.text, out amount);
            }
            if (amount < 0L)
            {
                amount = 0L;
            }

            long points = amount > 0L ? (amount / ZombieModeTuning.CashToPurificationRatio) : 0L;
            previewText.text = string.Format(L10n.T("BossRush_ZombieMode_CashPrompt_Preview"), amount, points);

            bool affordable = true;
            try
            {
                affordable = amount <= Duckov.Economy.EconomyManager.Money;
            }
            catch { /* Keep the preview usable when economy state is unavailable. */ }
            previewText.color = affordable ? BossRushUIColors.SuccessText : BossRushUIColors.DangerText;
            if (inputFrame != null)
            {
                inputFrame.color = affordable ? BossRushUIColors.Stroke : BossRushUIColors.DangerText;
            }
            if (confirmLabel != null)
            {
                confirmLabel.text = L10n.T(amount > 0L ? "BossRush_ZombieMode_CashPrompt_Confirm" : "BossRush_ZombieMode_CashPrompt_SkipZero");
            }

            if (errorText != null)
            {
                errorText.text = string.Empty;
            }
        }

        private System.Action QuickAmountAdd(long delta)
        {
            return delegate
            {
                if (amountField == null) return;
                long current = 0L;
                long.TryParse(amountField.text, out current);
                current += delta;
                if (current < 0L) current = 0L;
                amountField.text = current.ToString();
            };
        }

        private System.Action QuickAmountMax()
        {
            return delegate
            {
                if (amountField == null) return;
                try
                {
                    long money = Duckov.Economy.EconomyManager.Money;
                    if (money < 0L) money = 0L;
                    amountField.text = money.ToString();
                }
                catch { /* Keep the current amount when economy state is unavailable. */ }
            };
        }

        /// <summary>ESC 等同「返回」（UC-26）。输入框正在编辑非零金额时让给输入框（它自己用 ESC 退出编辑）。</summary>
        private void Update()
        {
            if (dispatched || !Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }
            if (amountField != null && amountField.isFocused &&
                !string.IsNullOrEmpty(amountField.text) && amountField.text != "0")
            {
                return;
            }
            OnButton(2);
        }

        private void OnButton(int code)
        {
            if (dispatched)
            {
                return;
            }

            // 返回按钮：只关闭这一个弹窗，不再 Close 整个 MapSelectionView，也不 Cancel Phase1。
            // 由调用方在 onCancelled 里释放 cashPromptOpen 标记，让玩家可继续在地图选择 UI 里挑选其他地图。
            // 关闭一律先还输入、再淡出（UC-07）：淡出只是表现，输入与时间流速这一帧就恢复。
            if (code == 2)
            {
                dispatched = true;
                System.Action cancelCb = onCancelled;
                RestoreInputState();
                BossRushUIKit.PlayCloseAndDestroy(gameObject);
                if (cancelCb != null)
                {
                    cancelCb();
                }
                return;
            }

            if (owner == null)
            {
                RestoreInputState();
                BossRushUIKit.PlayCloseAndDestroy(gameObject);
                return;
            }

            long amount = 0L;
            if (code == 0 && amountField != null && !string.IsNullOrEmpty(amountField.text))
            {
                long.TryParse(amountField.text, out amount);
            }

            string failureKey;
            if (!owner.ConfigureZombieModePendingCashInvestment(amount, out failureKey))
            {
                if (errorText != null && !string.IsNullOrEmpty(failureKey))
                {
                    errorText.text = L10n.T(failureKey);
                    ZombieModeUiNudge.Flash(amountField, string.Empty);
                }
                return;
            }

            dispatched = true;
            System.Action callback = onConfirmed;
            RestoreInputState();
            BossRushUIKit.PlayCloseAndDestroy(gameObject);
            if (callback != null)
            {
                callback();
            }
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "CashInvestment");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }

        // ===================== 通用 UI 创建工具 =====================

        /// <summary>
        /// 创建底部操作按钮。按钮所在行由 HorizontalLayoutGroup 负责排布。
        /// 主操作（确认）用 AccentFill 实色，其余走共享次级样式；三态、音效与按下回弹都由共享入口派生。返回按钮的标签。
        /// </summary>
        private TextMeshProUGUI CreateActionButton(Transform parent, string name, string text, bool primary, int code)
        {
            int captured = code;
            float btnW = primary ? 220f : 170f;
            float btnH = 44f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(btnW, btnH),
                primary ? BossRushUIColors.AccentFill : BossRushUIColors.SurfaceRaised, 16,
                new Vector2(btnW - 12f, btnH - 8f),
                delegate { OnButton(captured); },
                true);
            LayoutElement layoutElement = button.gameObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = button.gameObject.AddComponent<LayoutElement>();
            }
            layoutElement.minWidth = btnW;
            layoutElement.preferredWidth = btnW;
            layoutElement.minHeight = btnH;
            layoutElement.preferredHeight = btnH;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            if (!primary)
            {
                BossRushUIKit.StyleSecondaryButton(button);
            }
            Transform label = button.transform.Find("Text");
            return label != null ? label.GetComponent<TextMeshProUGUI>() : null;
        }

        /// <summary>
        /// 创建快捷金额按钮。xPercent 为行内宽度百分比（0~1）。
        /// </summary>
        private void CreateQuickButton(Transform parent, string name, string text, float xPercent, System.Action onClick)
        {
            System.Action captured = onClick;
            float btnW = 64f;
            float btnH = 32f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(xPercent, 0.5f),
                Vector2.zero,
                new Vector2(btnW, btnH),
                BossRushUIColors.SurfaceRaised, 14,
                new Vector2(btnW - 6f, btnH - 4f),
                delegate
                {
                    if (captured != null) captured();
                    UpdatePreview();
                },
                true);
            BossRushUIKit.StyleSecondaryButton(button);
        }
    }
}
