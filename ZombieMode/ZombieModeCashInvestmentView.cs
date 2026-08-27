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
        // ==================== 生存模式配色方案 ====================
        // 标题栏：中性深灰，避免全屏界面被单一蓝色占满。
        private static readonly Color HeaderColor = new Color(0.09f, 0.12f, 0.13f, 0.12f);
        // 标题装饰线
        private static readonly Color AccentLineColor = new Color(0.20f, 0.72f, 0.67f, 0.82f);
        // 输入行底色
        private static readonly Color RowColor = new Color(0.08f, 0.10f, 0.14f, 0.18f);
        // 输入框底色
        private static readonly Color InputBgColor = new Color(0.04f, 0.05f, 0.08f, 0.64f);
        // 输入框边框
        private static readonly Color InputBorderColor = new Color(0.25f, 0.35f, 0.55f, 0.34f);
        // 预览条底色：暗金色调
        private static readonly Color PreviewBarColor = new Color(0.12f, 0.14f, 0.08f, 0.20f);
        // 正文说明色：降低纯白刺眼感
        private static readonly Color BodyTextColor = new Color(0.78f, 0.82f, 0.88f, 1.00f);
        // 余额金色
        private static readonly Color BalanceGoldColor = new Color(1.00f, 0.85f, 0.40f, 1.00f);
        // 预览文本色：明亮黄绿
        private static readonly Color PreviewTextColor = new Color(0.75f, 0.92f, 0.45f, 1.00f);

        // 按钮配色：更柔和的现代色调
        private static readonly Color ConfirmColor = new Color(0.16f, 0.50f, 0.35f, 0.72f);
        private static readonly Color ConfirmHoverColor = new Color(0.22f, 0.62f, 0.42f, 0.94f);
        private static readonly Color SkipColor = new Color(0.38f, 0.34f, 0.16f, 0.68f);
        private static readonly Color SkipHoverColor = new Color(0.50f, 0.44f, 0.22f, 0.92f);
        private static readonly Color CancelColor = new Color(0.35f, 0.16f, 0.18f, 0.62f);
        private static readonly Color CancelHoverColor = new Color(0.48f, 0.22f, 0.24f, 0.90f);
        private static readonly Color QuickColor = new Color(0.16f, 0.22f, 0.32f, 0.50f);
        private static readonly Color QuickHoverColor = new Color(0.24f, 0.34f, 0.50f, 0.84f);

        private ModBehaviour owner;
        private System.Action onConfirmed;
        private System.Action onCancelled;
        private bool dispatched; // 防止 Confirm/Skip/Cancel 重入
        private ZombieModeUIHelper.ModalInputLease inputLease;

        private TMP_InputField amountField;
        private TextMeshProUGUI errorText;
        private TextMeshProUGUI previewText;
        private TextMeshProUGUI balanceText;
        private Image inputBorderImage;

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
                AccentLineColor);

            // ────────────────────────────────────────────────────────────
            // 以下所有子元素均使用「top-stretch」锚定（anchorMin(0,1) anchorMax(1,1)）
            // Y 偏移从面板顶部向下计算，CanvasScaler 负责分辨率缩放。
            // ────────────────────────────────────────────────────────────

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 68f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_Title"), 26,
                new Vector2(0f, 0f), new Vector2(0.65f, 1f),
                new Vector2(20f, 0f), new Vector2(0f, 0f),
                TextAlignmentOptions.Left, ZombieModeUIHelper.TextPrimaryColor);
            titleText.fontStyle = FontStyles.Bold;

            balanceText = ZombieModeUIHelper.CreateText("Balance", header.transform,
                GetBalanceLabel(), 18,
                new Vector2(0.65f, 0f), new Vector2(1f, 1f),
                new Vector2(-20f, 0f), new Vector2(0f, 0f),
                TextAlignmentOptions.Right, BalanceGoldColor);

            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 正文说明 ──
            float bodyH = 64f;
            ZombieModeUIHelper.CreateText("Body", panel.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_Body"), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + bodyH * 0.5f)), new Vector2(-40f, bodyH),
                TextAlignmentOptions.TopLeft, BodyTextColor);
            yPos += bodyH + 10f;

            // ── 输入行 ──
            float rowH = 48f;
            GameObject row = ZombieModeUIHelper.CreateRect("InputRow", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + rowH * 0.5f)), new Vector2(-40f, rowH), new Vector2(0.5f, 0.5f));
            Image rowImage = row.AddComponent<Image>();
            rowImage.color = RowColor;

            // 标签（行内左侧 2%~18%）
            ZombieModeUIHelper.CreateText("Label", row.transform,
                L10n.T("BossRush_ZombieMode_CashPrompt_AmountLabel"), 17,
                new Vector2(0.02f, 0f), new Vector2(0.18f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Left, Color.white);

            // 输入框边框（行内 20%~50%）
            GameObject inputBorder = ZombieModeUIHelper.CreateRect("InputBorder", row.transform,
                new Vector2(0.20f, 0.10f), new Vector2(0.50f, 0.90f),
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            inputBorderImage = inputBorder.AddComponent<Image>();
            inputBorderImage.color = InputBorderColor;

            // 输入框内部
            GameObject inputObj = ZombieModeUIHelper.CreateRect("Input", inputBorder.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-4f, -4f), new Vector2(0.5f, 0.5f));
            Image inputBg = inputObj.AddComponent<Image>();
            inputBg.color = InputBgColor;
            amountField = inputObj.AddComponent<TMP_InputField>();
            amountField.contentType = TMP_InputField.ContentType.IntegerNumber;

            GameObject textArea = ZombieModeUIHelper.CreateRect("TextArea", inputObj.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-10f, -4f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI inputText = ZombieModeUIHelper.CreateTMPText(textArea, "0", 18,
                TextAlignmentOptions.MidlineLeft, Color.white);
            inputText.raycastTarget = false;
            amountField.targetGraphic = inputBg;
            amountField.textComponent = inputText;
            amountField.textViewport = textArea.GetComponent<RectTransform>();
            amountField.lineType = TMP_InputField.LineType.SingleLine;
            amountField.customCaretColor = true;
            amountField.caretColor = AccentLineColor;
            amountField.selectionColor = new Color(0.20f, 0.72f, 0.67f, 0.35f);
            amountField.text = "0";
            amountField.onValueChanged.AddListener(delegate { UpdatePreview(); });

            // 快捷按钮（行内 55%/70%/85%）
            CreateQuickButton(row.transform, "+100",  0.55f, QuickAmountAdd(100));
            CreateQuickButton(row.transform, "+1000", 0.70f, QuickAmountAdd(1000));
            CreateQuickButton(row.transform, "Max",   0.85f, QuickAmountMax());

            yPos += rowH + 10f;

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep1", panel.transform,
                new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, -yPos), 1f,
                new Color(0.25f, 0.35f, 0.50f, 0.35f));
            yPos += 8f;

            // ── 预览条（带高亮背景） ──
            float previewH = 34f;
            previewText = ZombieModeUIHelper.CreateHighlightBar("Preview", panel.transform, "", 16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + previewH * 0.5f)), new Vector2(-40f, previewH),
                TextAlignmentOptions.Center, PreviewTextColor, PreviewBarColor);
            yPos += previewH + 6f;

            // ── 错误条 ──
            float errH = 24f;
            errorText = ZombieModeUIHelper.CreateText("Err", panel.transform, "", 14,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + errH * 0.5f)), new Vector2(-40f, errH),
                TextAlignmentOptions.Center, Color.white);
            errorText.color = new Color(1f, 0.45f, 0.45f, 1f);
            yPos += errH + 8f;

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep2", panel.transform,
                new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, -yPos), 1f,
                new Color(0.25f, 0.35f, 0.50f, 0.35f));
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

            CreateActionButton(actionRow.transform, "Confirm",
                L10n.T("BossRush_ZombieMode_CashPrompt_Confirm"),
                ConfirmColor, ConfirmHoverColor, 0);
            CreateActionButton(actionRow.transform, "SkipZero",
                L10n.T("BossRush_ZombieMode_CashPrompt_SkipZero"),
                SkipColor, SkipHoverColor, 1);
            CreateActionButton(actionRow.transform, "Cancel",
                L10n.T("BossRush_ZombieMode_CashPrompt_Cancel"),
                CancelColor, CancelHoverColor, 2);

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
            previewText.color = affordable ? PreviewTextColor : new Color(1f, 0.48f, 0.42f, 1f);
            if (inputBorderImage != null)
            {
                inputBorderImage.color = affordable ? InputBorderColor : ZombieModeUIHelper.DangerHoverColor;
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

        private void OnButton(int code)
        {
            if (dispatched)
            {
                return;
            }

            // 返回按钮：只关闭这一个弹窗，不再 Close 整个 MapSelectionView，也不 Cancel Phase1。
            // 由调用方在 onCancelled 里释放 cashPromptOpen 标记，让玩家可继续在地图选择 UI 里挑选其他地图。
            if (code == 2)
            {
                dispatched = true;
                System.Action cancelCb = onCancelled;
                RestoreInputState();
                Destroy(gameObject);
                if (cancelCb != null)
                {
                    cancelCb();
                }
                return;
            }

            if (owner == null)
            {
                RestoreInputState();
                Destroy(gameObject);
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
                }
                return;
            }

            dispatched = true;
            System.Action callback = onConfirmed;
            RestoreInputState();
            Destroy(gameObject);
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
        /// </summary>
        private void CreateActionButton(Transform parent, string name, string text,
            Color baseColor, Color hoverColor, int code)
        {
            int captured = code;
            float btnW = 170f;
            float btnH = 44f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(btnW, btnH),
                baseColor, 17,
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

            ZombieModeUIHelper.ApplyButtonColors(button, baseColor, hoverColor, baseColor * 0.6f);
        }

        /// <summary>
        /// 创建快捷金额按钮。xPercent 为行内宽度百分比（0~1）。
        /// </summary>
        private void CreateQuickButton(Transform parent, string text, float xPercent, System.Action onClick)
        {
            System.Action captured = onClick;
            float btnW = 60f;
            float btnH = 30f;
            Button button = ZombieModeUIHelper.CreateButton(
                text, parent, text,
                new Vector2(xPercent, 0.5f),
                Vector2.zero,
                new Vector2(btnW, btnH),
                QuickColor, 13,
                new Vector2(btnW - 6f, btnH - 4f),
                delegate
                {
                    if (captured != null) captured();
                    UpdatePreview();
                },
                true);
            ZombieModeUIHelper.ApplyButtonColors(button, QuickColor, QuickHoverColor, QuickColor * 0.6f);
        }
    }
}
