using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // SPEC 18 §5 / SPEC 14 §3.3 / SPEC 17 §7.4
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
        // 关闭对话框（确认 / 跳过）后再调用 MarkZombieModeMapConfirmedPhase1()。
        public void ShowZombieModeCashInvestmentPrompt(System.Action onConfirmed)
        {
            GameObject root = new GameObject("ZombieMode_CashInvestmentPrompt");
            ZombieModeCashInvestmentView view = root.AddComponent<ZombieModeCashInvestmentView>();
            view.Initialize(this, onConfirmed);
        }
    }

    // SPEC 18 §5 现金投入弹窗
    public sealed class ZombieModeCashInvestmentView : MonoBehaviour
    {
        private ModBehaviour owner;
        private System.Action onConfirmed;
        private TMP_InputField amountField;
        private TextMeshProUGUI errorText;

        public void Initialize(ModBehaviour newOwner, System.Action newOnConfirmed)
        {
            owner = newOwner;
            onConfirmed = newOnConfirmed;
            Build();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30100;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(620f, 360f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.88f);

            CreateText("Title", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_Title"), 26, new Vector2(0f, 130f), new Vector2(560f, 50f));
            CreateText("Body", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_Body"), 16, new Vector2(0f, 50f), new Vector2(560f, 100f));
            CreateText("Label", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_AmountLabel"), 18, new Vector2(-180f, -25f), new Vector2(180f, 36f));

            GameObject inputObj = CreateRect("Input", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(220f, 36f));
            inputObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(40f, -25f);
            Image inputBg = inputObj.AddComponent<Image>();
            inputBg.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);
            amountField = inputObj.AddComponent<TMP_InputField>();
            amountField.contentType = TMP_InputField.ContentType.IntegerNumber;
            TextMeshProUGUI inputText = CreateText("Text", inputObj.transform, "0", 16, Vector2.zero, new Vector2(200f, 30f));
            amountField.targetGraphic = inputBg;
            amountField.textComponent = inputText;
            amountField.textViewport = inputObj.GetComponent<RectTransform>();

            errorText = CreateText("Err", panel.transform, string.Empty, 14, new Vector2(0f, -70f), new Vector2(560f, 26f));
            errorText.color = new Color(1f, 0.55f, 0.55f, 1f);

            CreateButton("Confirm", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_Confirm"), new Vector2(-180f, -130f), 0);
            CreateButton("SkipZero", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_SkipZero"), new Vector2(0f, -130f), 1);
            CreateButton("Cancel", panel.transform, L10n.T("BossRush_ZombieMode_CashPrompt_Cancel"), new Vector2(180f, -130f), 2);
        }

        private void OnButton(int code)
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            if (code == 2)
            {
                owner.CancelZombieModeMapSelectionPhase1();
                ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
                Duckov.UI.MapSelectionView mapView = Duckov.UI.MapSelectionView.Instance;
                if (mapView != null)
                {
                    mapView.Close();
                }
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

            System.Action callback = onConfirmed;
            Destroy(gameObject);
            if (callback != null)
            {
                callback();
            }
        }

        private GameObject CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return obj;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            obj.GetComponent<RectTransform>().anchoredPosition = position;
            return ZombieModeUIHelper.CreateTMPText(
                obj,
                text,
                fontSize,
                TextAlignmentOptions.Center,
                Color.white);
        }

        private void CreateButton(string name, Transform parent, string text, Vector2 position, int code)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(160f, 50f));
            obj.GetComponent<RectTransform>().anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            Color baseColor = code == 2 ? new Color(0.36f, 0.18f, 0.18f, 0.95f)
                : code == 1 ? new Color(0.32f, 0.32f, 0.18f, 0.95f)
                : new Color(0.18f, 0.36f, 0.22f, 0.95f);
            image.color = baseColor;
            Button button = obj.AddComponent<Button>();
            CreateText("Text", obj.transform, text, 18, Vector2.zero, new Vector2(150f, 40f));
            int captured = code;
            button.onClick.AddListener(delegate { OnButton(captured); });
        }
    }
}
