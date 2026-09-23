// ============================================================================
// ZombieModeTemporaryNpcServiceView.cs - 丧尸模式补给 / 医疗终端的交互体与服务面板
// ============================================================================
// 模块说明：
//   从 ZombieModeRewards.cs（宿主 partial）原样拆出后按 2026-09-23 审美审查重做（UC-07 / 09 / 10 / 21 / 25 / 26 / 27 / 29）。
//   拆出的原因：宿主 partial 的行数预算没有余量，视图与交互体本来就不是宿主职责（AGENTS §4.15）。
//   - 货架格：卡片档圆角底图 + 描边 + 共享按钮三态（禁用色由 GetDisabledColor 派生，不再等于常态色）；
//     名称 / 价格（净化点不够时 DangerText）/ 剩余分层；卖完的格子保留并写「售罄」（主流商店口径，不只是变灰）。
//   - 购买后原地刷新文字与可点状态，不删遮罩与面板，滚动位置不归零；滚动条走 ConfigureScrollRect。
//   - 医疗终端的服务改成横条（640×64），五项不用滚动也放得下。
//   - 净化点不够时点格子：格子上方就地提示「还差 N 净化点」并抖一下（官方 Toast 被 30000 层遮罩压着看不见）。
//   - ESC 关闭；关闭先还输入再淡出。
//   购买 / 治疗的判据与扣点仍在宿主（TryPurchaseZombieModeMerchantStock / TryUseZombieModeNurseService），这里只预判余额做提示。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public sealed class ZombieModeTemporaryNpcInteractable : InteractableBase
    {
        private int runId;
        private string serviceType = string.Empty;

        public void Initialize(int newRunId, string newServiceType)
        {
            runId = newRunId;
            serviceType = newServiceType ?? string.Empty;
            ApplyInteractName();
        }

        protected override void Awake()
        {
            ApplyInteractName();
            try
            {
                interactCollider = GetComponent<Collider>();
                // 交互提示挂在终端模型的头顶（真 NPC 模型约 1.2 m 高，见 ZombieModeServiceTerminalLook）。
                interactMarkerOffset = new Vector3(0f, 1.1f, 0f);
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[ZombieMode] TemporaryNpc");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc Awake collider 获取失败: " + e.Message);
            }

            try { base.Awake(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Awake 失败: " + e.Message); }
        }

        protected override void Start()
        {
            try { base.Start(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Start 失败: " + e.Message); }
            ApplyInteractName();
        }

        protected override bool IsInteractable()
        {
            return ModBehaviour.Instance != null && runId > 0;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnInteractStart 失败: " + e.Message);
            }

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnTimeOut 失败: " + e.Message);
            }
        }

        private void ApplyInteractName()
        {
            string key = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_InteractNurse"
                : "BossRush_ZombieMode_Npc_InteractMerchant";
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = key;
                InteractName = key;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc InteractName 设置失败: " + e.Message);
            }
        }
    }

    public sealed class ZombieModeTemporaryNpcServiceView : MonoBehaviour
    {
        private sealed class ServiceCell
        {
            public int Index;
            public int BasePrice;
            public bool Nurse;
            public Button Button;
            public Image Accent;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Price;
            public TextMeshProUGUI Remaining;
        }

        private int runId;
        private ModBehaviour owner;
        private string serviceType = string.Empty;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private bool closing;
        private Color serviceAccent;
        private TextMeshProUGUI subtitleText;
        private readonly System.Collections.Generic.List<ServiceCell> cells = new System.Collections.Generic.List<ServiceCell>();

        public void Initialize(int newRunId, ModBehaviour newOwner, string newServiceType)
        {
            runId = newRunId;
            owner = newOwner;
            serviceType = newServiceType ?? string.Empty;
            Build();
            ClaimInputAndPause();
        }

        private bool IsNurse
        {
            get { return string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal); }
        }

        private void Build()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ZombieService;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 服务强调色走 token（UC-25）：医疗 SuccessText，补给 WarningText。
            serviceAccent = IsNurse ? BossRushUIColors.SuccessText : BossRushUIColors.WarningText;
            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(820f, 620f),
                serviceAccent);

            // ── 标题栏（不垫直角色条：圆角外会露出方角，UC-21）──
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));

            string titleKey = IsNurse
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T(titleKey), 28,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(700f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -headerH), 2f, new Color(serviceAccent.r, serviceAccent.g, serviceAccent.b, 0.6f));

            // ── 副标题（净化点余额，购买后原地更新）──
            subtitleText = ZombieModeUIHelper.CreateText(
                "Subtitle",
                panel.transform,
                string.Empty,
                15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH + 22f)), new Vector2(-40f, 30f),
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);

            Transform body = CreateScrollableBody(panel.transform);
            if (IsNurse)
            {
                BuildNurseServices(body);
            }
            else
            {
                BuildMerchantStock(body);
            }

            CreateCloseButton(panel.transform);
            RefreshCells();
            BossRushUI.PlayOpenAnimation(panel);
        }

        private Transform CreateScrollableBody(Transform parent)
        {
            // 使用 anchor-based 布局适配新的分层面板
            // 标题栏64 + 装饰线2 + 副标题30 + 间距 = 约 110px 顶部偏移
            // 底部留 70px 给关闭按钮
            GameObject body = ZombieModeUIHelper.CreateRect(
                "Body",
                parent,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform bodyRect = body.GetComponent<RectTransform>();
            bodyRect.offsetMin = new Vector2(20f, 70f);   // 底部留给关闭按钮
            bodyRect.offsetMax = new Vector2(-20f, -110f); // 顶部留给标题栏+副标题

            ScrollRect scrollRect = body.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            GameObject viewport = ZombieModeUIHelper.CreateRect(
                "Viewport",
                body.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.offsetMin = new Vector2(10f, 10f);
            viewportRect.offsetMax = new Vector2(-10f, -10f);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            GameObject content = ZombieModeUIHelper.CreateRect(
                "Content",
                viewport.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            // 右侧留 20 给滚动条（ConfigureScrollRect 的约定）。
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(-20f, 0f);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            BossRushUI.ConfigureScrollRect(scrollRect);
            return content.transform;
        }

        private void BuildMerchantStock(Transform parent)
        {
            // 列数固定为 4：列数变少会让内容变高，把靠后的头盔项推到关闭按钮上面
            // （见 ZombieModeStarterEquipmentAndNpcUiGuard 记录的重叠问题）。
            GridLayoutGroup grid = parent.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.cellSize = new Vector2(168f, 102f);
            grid.spacing = new Vector2(12f, 12f);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 4, 4);

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.MerchantStockEntry[] stock = owner != null
                ? owner.GetZombieModeMerchantStock(runId, serviceType)
                : new ZombieModeNpcCatalog.MerchantStockEntry[0];
            for (int i = 0; i < stock.Length && i < ZombieModeNpcCatalog.MaxMerchantStockButtons; i++)
            {
                ZombieModeNpcCatalog.MerchantStockEntry entry = stock[i];
                CreateServiceButton(parent, "Merchant_" + i, i, entry.BasePrice, L10n.T(entry.DisplayKey), false);
            }
        }

        private void BuildNurseServices(Transform parent)
        {
            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(18, 18, 6, 6);

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.NurseServiceEntry[] services = owner != null
                ? owner.GetZombieModeNurseServices(runId, serviceType)
                : new ZombieModeNpcCatalog.NurseServiceEntry[0];
            for (int i = 0; i < services.Length; i++)
            {
                ZombieModeNpcCatalog.NurseServiceEntry entry = services[i];
                CreateServiceButton(parent, "Nurse_" + i, i, entry.BasePrice, L10n.T(entry.ServiceKey), true);
            }
        }

        /// <summary>
        /// 一个服务格。货架格 168×102（名称两行、左下价格、右下剩余），医疗横条 640×64（左名称、右价格与剩余）。
        /// 文字与可点状态由 <see cref="RefreshCells"/> 填，购买后原地刷新。
        /// </summary>
        private void CreateServiceButton(Transform parent, string name, int index, int basePrice, string label, bool nurse)
        {
            Vector2 size = nurse ? new Vector2(640f, 64f) : new Vector2(168f, 102f);
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            LayoutElement layoutElement = obj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            Image image = obj.AddComponent<Image>();
            image.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyFramedPanelSkin(image, 10, BossRushUISkinPart.Card);
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.SurfaceRaised);

            // 左侧状态细条
            GameObject accent = ZombieModeUIHelper.CreateRect(
                "Accent",
                obj.transform,
                new Vector2(0f, 0.20f),
                new Vector2(0f, 0.80f),
                new Vector2(3f, 0f),
                new Vector2(3f, 0f),
                new Vector2(0f, 0.5f));
            Image accentImage = accent.AddComponent<Image>();
            accentImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(accentImage, 2, BossRushUISkinPart.Hairline);

            ServiceCell cell = new ServiceCell();
            cell.Index = index;
            cell.BasePrice = basePrice;
            cell.Nurse = nurse;
            cell.Button = button;
            cell.Accent = accentImage;
            if (nurse)
            {
                cell.Name = CreateCellText("Name", obj.transform, label, 16, new Vector2(0f, 0f), new Vector2(0.62f, 1f),
                    new Vector2(8f, 0f), new Vector2(-24f, -12f), TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);
                cell.Price = CreateCellText("Price", obj.transform, string.Empty, 15, new Vector2(0.62f, 0.5f), new Vector2(1f, 1f),
                    new Vector2(-8f, -2f), new Vector2(-16f, -6f), TextAlignmentOptions.BottomRight, BossRushUIColors.WarningText);
                cell.Remaining = CreateCellText("Remaining", obj.transform, string.Empty, 13, new Vector2(0.62f, 0f), new Vector2(1f, 0.5f),
                    new Vector2(-8f, 2f), new Vector2(-16f, -6f), TextAlignmentOptions.TopRight, BossRushUIColors.TextSecondary);
            }
            else
            {
                cell.Name = CreateCellText("Name", obj.transform, label, 15, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(3f, -30f), new Vector2(-30f, 44f), TextAlignmentOptions.TopLeft, BossRushUIColors.TextPrimary);
                cell.Name.enableWordWrapping = true;
                cell.Price = CreateCellText("Price", obj.transform, string.Empty, 14, new Vector2(0f, 0f), new Vector2(0.62f, 0f),
                    new Vector2(8f, 18f), new Vector2(-16f, 22f), TextAlignmentOptions.MidlineLeft, BossRushUIColors.WarningText);
                cell.Remaining = CreateCellText("Remaining", obj.transform, string.Empty, 12, new Vector2(0.62f, 0f), new Vector2(1f, 0f),
                    new Vector2(-8f, 18f), new Vector2(-10f, 22f), TextAlignmentOptions.MidlineRight, BossRushUIColors.TextSecondary);
            }
            cells.Add(cell);

            button.onClick.AddListener(delegate { OnCellClicked(cell); });
        }

        private static TextMeshProUGUI CreateCellText(string name, Transform parent, string text, float fontSize,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, TextAlignmentOptions alignment, Color color)
        {
            TextMeshProUGUI tmp = ZombieModeUIHelper.CreateText(name, parent, text, fontSize, anchorMin, anchorMax, position, size, alignment, color);
            // 固定字号 + 省略号：格与格字号一致（UC-29）；框高都 ≥ 字号×1.45+4，不会整行清空。
            tmp.enableAutoSizing = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.margin = Vector4.zero;
            return tmp;
        }

        /// <summary>按当前净化点、价格与剩余刷新每一格的文字与可点状态（购买后原地调，不重建面板）。</summary>
        private void RefreshCells()
        {
            int points = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            if (subtitleText != null)
            {
                string subtitleKey = IsNurse
                    ? "BossRush_ZombieMode_Npc_NurseSubtitle"
                    : "BossRush_ZombieMode_Npc_MerchantSubtitle";
                subtitleText.text = string.Format(L10n.T(subtitleKey), points.ToString("N0"));
            }

            for (int i = 0; i < cells.Count; i++)
            {
                ServiceCell cell = cells[i];
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, cell.Index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, cell.BasePrice) : cell.BasePrice;
                bool soldOut = remaining <= 0;
                bool affordable = points >= price;

                // 售罄的格子保留并写「售罄」（主流商店口径），不可点；钱不够照样可点，点了就地提示差多少。
                cell.Button.interactable = !soldOut;
                cell.Name.color = soldOut ? BossRushUIColors.TextSecondary : BossRushUIColors.TextPrimary;
                if (soldOut)
                {
                    cell.Price.text = L10n.T("BossRush_ZombieMode_Npc_SoldOut");
                    cell.Price.color = BossRushUIColors.TextSecondary;
                    cell.Remaining.text = string.Empty;
                    cell.Accent.color = BossRushUIColors.Divider;
                    continue;
                }

                cell.Price.text = string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price);
                cell.Price.color = affordable ? BossRushUIColors.WarningText : BossRushUIColors.DangerText;
                cell.Remaining.text = string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining);
                cell.Accent.color = affordable ? serviceAccent : BossRushUIColors.DangerText;
            }
        }

        private void OnCellClicked(ServiceCell cell)
        {
            if (owner == null || closing || cell == null)
            {
                return;
            }

            int shortBy = owner.GetZombieModeNpcServicePrice(runId, cell.BasePrice) - owner.GetZombieModePurificationPoints(runId);
            if (shortBy > 0)
            {
                ZombieModeUiNudge.Flash(cell.Button, string.Format(L10n.T("BossRush_ZombieMode_Notify_PointsShort"), shortBy));
                return;
            }

            bool done = cell.Nurse
                ? owner.TryUseZombieModeNurseService(runId, serviceType, cell.Index)
                : owner.TryPurchaseZombieModeMerchantStock(runId, serviceType, cell.Index);
            if (done)
            {
                RefreshCells();
            }
        }

        private void CreateCloseButton(Transform parent)
        {
            // 固定到面板底部；关闭是次级操作。
            Button button = ZombieModeUIHelper.CreateButton(
                "Close",
                parent,
                L10n.T("BossRush_ZombieMode_Npc_Close"),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 36f),
                new Vector2(180f, 44f),
                BossRushUIColors.SurfaceRaised,
                17,
                new Vector2(168f, 36f),
                null,
                true);
            BossRushUIKit.StyleSecondaryButton(button);
            button.onClick.AddListener(Close);
        }

        /// <summary>关闭：先还输入，再 0.12 s 淡出（UC-07）。</summary>
        private void Close()
        {
            if (closing)
            {
                return;
            }
            closing = true;
            RestoreInputState();
            BossRushUIKit.PlayCloseAndDestroy(gameObject);
        }

        /// <summary>ESC 关闭（UC-26）。</summary>
        private void Update()
        {
            if (!closing && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "TemporaryNpcService");
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

    }
}
