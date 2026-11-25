// ============================================================================
// ArenaExpeditionOrderPanel.cs - 出征令面板（竞技场路牌「出征令」打开）
// ============================================================================
// 页型：一屏表单（docs/architecture/UI制作共识.md）。左边水墨选单三栏单选，右边一张悬赏令写结论：
//   阵容（弹指可灭 / 有点意思 / 无间炼狱）× 战帖（轻松 / 标准 / 困难 / 噩梦）× 征程（短征 / 长征 / 全征）。
// 2026-11-25 owner：选单里卡片下的两行小字和「收起 / 立誓」拿掉，改在右边做一张悬赏令——
//   这档战帖的每条战规（类别朱印 + 名 + 说明）、赏格，以及「等等 / 立誓」都写在令上（「收起」改叫「等等」）。
//   选单卡片只留名字；战帖卡下一排小印数出这档有几条战规（金是宽限，朱是加码）。
// 水墨主题（Common/UI/BossRushInkSkin.cs，owner 2026-11-24：高级、史诗、无塑料感）：
//   打开墨晕散开、标题金笔写出，悬赏令从上轴往下卷开；选中的卡金边 + 金笔下划，令上重新落一遍墨；
//   噩梦卡转暗红、边缘燃金色余烬、响一声古钟，悬赏令纸色发焦、底边也燃起余烬；
//   立誓时朱印落在悬赏令上，墨池收拢后开第一波。
// 选择记在界面本地偏好（BossRushLocalPrefs），不进 ModConfig、不进存档。
// 模态：独占输入租约，ESC / 手柄取消 = 等等；切图时画布随场景销毁，OnDestroy 归还租约。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ArenaExpeditionOrderPanel : MonoBehaviour
    {
        private const string RootName = "BossRush_ArenaExpeditionOrder";

        // 整页 = 左边选单 + 右边悬赏令，一起墨晕淡入。
        private static readonly Vector2 SheetSize = new Vector2(1580f, 900f);
        private static readonly Vector2 PanelSize = new Vector2(1000f, 720f);
        private const float PanelCenterX = -270f;
        private const float CardsCenterX = 52f;
        private const float RowLabelX = -440f;
        private const float CardGap = 22f;
        private const float WideCardWidth = 260f;
        private const float WideCardHeight = 112f;
        private const float TierCardWidth = 190f;
        private const float TierCardHeight = 200f;

        private static readonly Vector2 NoticeSize = new Vector2(480f, 840f);
        private const float NoticeCenterX = 518f;
        /// <summary>悬赏令正文的宽度与最低处（再往下是「等等 / 立誓」）。</summary>
        private const float StackWidth = 400f;
        private const float StackBottom = -322f;
        private const float ButtonsY = -366f;
        private const float UnrollSeconds = 0.6f;

        private static readonly Color SelectedFill = new Color(0.175f, 0.145f, 0.115f, 0.97f);
        private static readonly Color NightmareFill = new Color(0.27f, 0.065f, 0.05f, 0.97f);
        private static readonly Color IdleFrame = new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.28f);

        // 悬赏令：旧宣纸、焦墨字、朱砂印；宽限用古铜印（鎏金压在纸上看不清）。
        private static readonly Color NoticePaper = new Color(0.800f, 0.725f, 0.585f, 0.985f);
        private static readonly Color NoticePaperScorched = new Color(0.735f, 0.620f, 0.490f, 0.985f);
        private static readonly Color NoticeInk = new Color(0.120f, 0.090f, 0.070f, 1f);
        private static readonly Color NoticeInkSoft = new Color(0.300f, 0.240f, 0.185f, 1f);
        private static readonly Color NoticeCinnabar = new Color(0.560f, 0.130f, 0.100f, 1f);
        private static readonly Color NoticeNightmare = new Color(0.470f, 0.075f, 0.055f, 1f);
        private static readonly Color GraceSeal = new Color(0.540f, 0.390f, 0.130f, 1f);
        private static readonly Color RodWood = new Color(0.095f, 0.065f, 0.048f, 1f);
        private static readonly Color RodCap = new Color(0.560f, 0.430f, 0.180f, 1f);
        private static readonly Color Stain = new Color(0.420f, 0.280f, 0.140f, 0.10f);

        private static ArenaExpeditionOrderPanel instance;

        private sealed class Card
        {
            internal Button Button;
            internal RectTransform Rect;
            internal TextMeshProUGUI Name;
            internal TextMeshProUGUI Sub;
            internal RectTransform Underline;
            internal float UnderlineWidth;
            internal bool Selected;
            internal CanvasGroup Pips;
            internal ArenaExpeditionEmberField Embers;
        }

        private Canvas canvas;
        private RectTransform sheet;
        private RectTransform notice;
        private RectTransform unrollMask;
        private RectTransform bottomRod;
        private RectTransform paper;
        private Image paperImage;
        private RectTransform writHolder;
        private RectTransform writ;
        private ArenaExpeditionEmberField paperEmbers;
        private ZombieModeUIHelper.ModalInputLease lease;
        private PetNestCancelKey cancelKey;
        private bool closing;
        private BossRushInteractable source;
        private int poolCount;

        private ArenaExpeditionLineup lineup;
        private ArenaExpeditionTier tier;
        private ArenaExpeditionLength length;

        private readonly Card[] lineupCards = new Card[3];
        private readonly Card[] tierCards = new Card[4];
        private readonly Card[] lengthCards = new Card[3];
        private CanvasGroup lengthRow;
        private float lastBellTime = -100f;
        private float lastPaperTime = -100f;

        internal static bool IsOpen { get { return instance != null; } }

        /// <summary>由竞技场路牌的「出征令」选项打开。启用池为空时只提示，不开空面板。</summary>
        internal static void Open(BossRushInteractable source)
        {
            ModBehaviour host = ModBehaviour.Instance;
            if (host == null || source == null) return;
            if (instance != null) return;
            List<EnemyPresetInfo> pool = null;
            try { pool = host.GetFilteredEnemyPresets(); }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush] [WARNING] 出征令读取 Boss 池失败: " + e.Message); }
            int count = pool != null ? pool.Count : 0;
            if (count == 0)
            {
                host.ShowMessage(L10n.T("一个 Boss 都没启用：按 Ctrl+F10 打开「Boss池设置」，至少启用一个再开战。", "No Boss enabled: press Ctrl+F10 to open Boss Pool Settings and enable at least one."));
                return;
            }
            try
            {
                Canvas root = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.Modal, true);
                instance = root.gameObject.AddComponent<ArenaExpeditionOrderPanel>();
                instance.canvas = root;
                instance.source = source;
                instance.poolCount = count;
                instance.Build();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BossRush] 出征令面板打开失败: " + e.Message);
                Close();
            }
        }

        /// <summary>立即关闭（异常清理 / 卸载）。</summary>
        internal static void Close()
        {
            ArenaExpeditionOrderPanel panelInstance = instance;
            instance = null;
            if (panelInstance == null) return;
            panelInstance.ReleaseInput();
            if (panelInstance.gameObject != null) Destroy(panelInstance.gameObject);
        }

        internal static void ResetStaticCaches()
        {
            Close();
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        private void Build()
        {
            ArenaExpeditionOrder remembered = ArenaExpeditionOrder.LoadRemembered();
            lineup = remembered.Lineup;
            tier = remembered.Tier;
            length = ArenaExpeditionOrder.LoadRememberedLength();

            BossRushUI.CreateBackdrop(canvas.transform);
            GameObject sheetObject = ZombieModeUIHelper.CreateRect("Sheet", canvas.transform, new Vector2(0.5f, 0.5f), SheetSize);
            sheet = sheetObject.GetComponent<RectTransform>();

            BuildSelection(sheet);
            BuildNotice(sheet);
            RefreshAll(false);

            lease = ZombieModeUIHelper.ClaimModalInput(canvas.gameObject, "ArenaExpeditionOrder");
            cancelKey = PetNestCancelKey.Attach(canvas.gameObject, Dismiss, null);
            BossRushInkSkin.PlayInkReveal(sheet);
            PlayUnroll(0.22f);
        }

        // ---- 左边：水墨选单（只留卡片，不写结论） ----

        private void BuildSelection(Transform parent)
        {
            GameObject surface = ZombieModeUIHelper.CreateRect("Surface", parent, new Vector2(0.5f, 0.5f), PanelSize);
            surface.GetComponent<RectTransform>().anchoredPosition = new Vector2(PanelCenterX, 0f);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.raycastTarget = true;
            BossRushInkSkin.ApplyInkPanel(surfaceImage, false);

            RectTransform title = BossRushInkSkin.CreateBrushTitle(surface.transform, "出征令", "War Writ", 48f);
            title.anchoredPosition = new Vector2(0f, 266f);

            BuildLineupRow(surface.transform, 130f);
            BossRushInkSkin.CreateInkDivider(surface.transform, 860f, new Vector2(CardsCenterX, 58f));
            BuildTierRow(surface.transform, -58f);
            BossRushInkSkin.CreateInkDivider(surface.transform, 860f, new Vector2(CardsCenterX, -182f));
            BuildLengthRow(surface.transform, -258f);
        }

        private void BuildLineupRow(Transform parent, float y)
        {
            CreateRowLabel(parent, "阵容", "LINEUP", y);
            for (int i = 0; i < 3; i++)
            {
                ArenaExpeditionLineup value = (ArenaExpeditionLineup)i;
                float x = CardsCenterX + (i - 1) * (WideCardWidth + CardGap);
                lineupCards[i] = CreateWideCard(parent, "Lineup" + i, new Vector2(x, y),
                    ArenaExpeditionTuning.LineupName(value), ArenaExpeditionTuning.LineupNameEn(value),
                    ArenaExpeditionTuning.LineupLine(value), () => SelectLineup(value));
            }
        }

        private void BuildTierRow(Transform parent, float y)
        {
            CreateRowLabel(parent, "战帖", "CHALLENGE", y);
            for (int i = 0; i < 4; i++)
            {
                ArenaExpeditionTier value = (ArenaExpeditionTier)i;
                float x = CardsCenterX + (i - 1.5f) * (TierCardWidth + CardGap);
                tierCards[i] = CreateTierCard(parent, "Tier" + i, new Vector2(x, y), value, () => SelectTier(value));
            }
        }

        private void BuildLengthRow(Transform parent, float y)
        {
            GameObject row = ZombieModeUIHelper.CreateRect("LengthRow", parent, new Vector2(0.5f, 0.5f), new Vector2(PanelSize.x, WideCardHeight + 20f));
            row.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
            lengthRow = row.AddComponent<CanvasGroup>();
            CreateRowLabel(row.transform, "征程", "MARCH", 0f);
            for (int i = 0; i < 3; i++)
            {
                ArenaExpeditionLength value = (ArenaExpeditionLength)i;
                float x = CardsCenterX + (i - 1) * (WideCardWidth + CardGap);
                lengthCards[i] = CreateWideCard(row.transform, "Length" + i, new Vector2(x, 0f),
                    ArenaExpeditionTuning.LengthName(value), ArenaExpeditionTuning.LengthNameEn(value),
                    ArenaExpeditionTuning.LengthCardLine(value, poolCount), () => SelectLength(value));
            }
        }

        private static void CreateRowLabel(Transform parent, string zh, string en, float y)
        {
            bool chinese = L10n.IsChinese;
            string text = chinese ? zh.Substring(0, 1) + "\n" + zh.Substring(1) : en;
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("RowLabel", parent, text, chinese ? 24f : 13f,
                new Vector2(RowLabelX, y + (chinese ? 8f : 0f)), new Vector2(84f, chinese ? 76f : 30f), TextAlignmentOptions.Center, BossRushInkColors.Gold);
            BossRushUI.ApplyGameFont(label);
            label.fontStyle = FontStyles.Bold;
            label.lineSpacing = -10f;
            label.characterSpacing = chinese ? 0f : 6f;
            if (chinese)
            {
                TextMeshProUGUI sub = ZombieModeUIHelper.CreateText("RowLabelEn", parent, en, 10f,
                    new Vector2(RowLabelX, y - 40f), new Vector2(96f, 20f), TextAlignmentOptions.Center, BossRushInkColors.GoldSoft);
                BossRushUI.ApplyGameFont(sub);
                sub.characterSpacing = 4f;
            }
        }

        /// <summary>阵容 / 征程卡：名字一行（中文时右侧带一句「每波几只」），中文下面一行英文小字，底部金笔。</summary>
        private Card CreateWideCard(Transform parent, string name, Vector2 position, string title, string titleEn, string line, Action onClick)
        {
            Card card = CreateCardShell(parent, name, position, new Vector2(WideCardWidth, WideCardHeight), onClick);
            bool chinese = L10n.IsChinese;
            float left = -WideCardWidth * 0.5f + 22f;
            float inner = WideCardWidth - 44f;
            float center = left + inner * 0.5f;
            card.UnderlineWidth = 110f;
            if (chinese)
            {
                card.Name = CreateCardText(card.Rect, "Name", title, 26f, new Vector2(center, 22f), new Vector2(inner, 42f), TextAlignmentOptions.Left, true);
                card.Sub = CreateCardText(card.Rect, "Sub", line, 15f, new Vector2(center, 21f), new Vector2(inner, 26f), TextAlignmentOptions.Right, false);
                TextMeshProUGUI en = CreateCardText(card.Rect, "En", titleEn.ToUpperInvariant(), 10f,
                    new Vector2(center, -8f), new Vector2(inner, 20f), TextAlignmentOptions.Left, false);
                en.color = BossRushInkColors.GoldSoft;
                en.characterSpacing = 5f;
                card.Underline = CreateUnderline(card.Rect, new Vector2(left, -32f), card.UnderlineWidth);
            }
            else
            {
                card.Name = CreateCardText(card.Rect, "Name", title, 20f, new Vector2(center, 18f), new Vector2(inner, 34f), TextAlignmentOptions.Left, true);
                card.Sub = CreateCardText(card.Rect, "Sub", line, 14f, new Vector2(center, -10f), new Vector2(inner, 25f), TextAlignmentOptions.Left, false);
                card.Underline = CreateUnderline(card.Rect, new Vector2(left, -34f), card.UnderlineWidth);
            }
            return card;
        }

        /// <summary>战帖卡：档名、英文、几条战规，底下一排小印（金是宽限，朱是加码）。</summary>
        private Card CreateTierCard(Transform parent, string name, Vector2 position, ArenaExpeditionTier value, Action onClick)
        {
            Card card = CreateCardShell(parent, name, position, new Vector2(TierCardWidth, TierCardHeight), onClick);
            bool chinese = L10n.IsChinese;
            float inner = TierCardWidth - 20f;
            card.Name = CreateCardText(card.Rect, "Name", ArenaExpeditionTuning.TierName(value), chinese ? 36f : 24f,
                new Vector2(0f, 50f), new Vector2(inner, chinese ? 58f : 40f), TextAlignmentOptions.Center, true);
            card.Name.characterSpacing = chinese ? 16f : 3f;
            if (chinese)
            {
                TextMeshProUGUI en = CreateCardText(card.Rect, "En", ArenaExpeditionTuning.TierNameEn(value).ToUpperInvariant(), 10.5f,
                    new Vector2(0f, 16f), new Vector2(inner, 20f), TextAlignmentOptions.Center, false);
                en.color = BossRushInkColors.GoldSoft;
                en.characterSpacing = 7f;
            }
            card.Sub = CreateCardText(card.Rect, "Sub", ArenaExpeditionTuning.TierCardLine(value), 15f,
                new Vector2(0f, chinese ? -26f : -14f), new Vector2(inner, 26f), TextAlignmentOptions.Center, false);
            card.UnderlineWidth = 96f;
            card.Underline = CreateUnderline(card.Rect, new Vector2(-48f, chinese ? 2f : 22f), card.UnderlineWidth);
            card.Pips = CreateRulePips(card.Rect, value, chinese ? -64f : -56f);
            if (value == ArenaExpeditionTier.Nightmare)
            {
                card.Embers = card.Rect.gameObject.AddComponent<ArenaExpeditionEmberField>();
                card.Embers.Bind(card.Rect);
            }
            return card;
        }

        private static CanvasGroup CreateRulePips(RectTransform card, ArenaExpeditionTier value, float y)
        {
            IList<ArenaExpeditionRule> rules = ArenaExpeditionTuning.RulesFor(value);
            GameObject row = ZombieModeUIHelper.CreateRect("Pips", card, new Vector2(0.5f, 0.5f), new Vector2(TierCardWidth - 20f, 16f));
            row.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
            CanvasGroup group = row.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            const float step = 17f;
            float start = -(rules.Count - 1) * step * 0.5f;
            for (int i = 0; i < rules.Count; i++)
            {
                GameObject pip = ZombieModeUIHelper.CreateRect("Pip", row.transform, new Vector2(0.5f, 0.5f), new Vector2(10f, 10f));
                RectTransform rect = pip.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(start + i * step, 0f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Image image = pip.AddComponent<Image>();
                image.sprite = BossRushInkSkin.GetSealSprite();
                image.raycastTarget = false;
                image.color = rules[i] != null && rules[i].Grace ? BossRushInkColors.Gold : BossRushInkColors.Cinnabar;
            }
            return group;
        }

        private Card CreateCardShell(Transform parent, string name, Vector2 position, Vector2 size, Action onClick)
        {
            Button button = ZombieModeUIHelper.CreateButton(name, parent, string.Empty, new Vector2(0.5f, 0.5f), position, size,
                BossRushInkColors.SurfaceRaised, 16f, size, () => { if (!closing && onClick != null) onClick(); }, true);
            BossRushInkSkin.StyleInkButton(button, BossRushInkButtonRole.Secondary);
            Card card = new Card { Button = button, Rect = button.GetComponent<RectTransform>() };
            return card;
        }

        private static TextMeshProUGUI CreateCardText(RectTransform parent, string name, string text, float size, Vector2 position,
            Vector2 box, TextAlignmentOptions alignment, bool bold)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(name, parent, text, size, position, box, alignment, BossRushInkColors.PaperDim);
            BossRushUI.ApplyGameFont(label);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            if (bold) label.fontStyle = FontStyles.Bold;
            return label;
        }

        /// <summary>选中时从左写出的金色一笔（左端固定在 <paramref name="left"/>）。</summary>
        private static RectTransform CreateUnderline(RectTransform parent, Vector2 left, float width)
        {
            RectTransform stroke = BossRushInkSkin.CreateBrushStroke(parent, "Underline", new Vector2(width, 8f), Vector2.zero, BossRushInkColors.Gold);
            stroke.pivot = new Vector2(0f, 0.5f);
            stroke.anchoredPosition = left;
            stroke.sizeDelta = new Vector2(0f, 8f);
            return stroke;
        }

        // ---- 右边：悬赏令（上下两根轴，纸从上往下卷开） ----

        private void BuildNotice(Transform parent)
        {
            GameObject root = ZombieModeUIHelper.CreateRect("Notice", parent, new Vector2(0.5f, 0.5f), new Vector2(NoticeSize.x + 80f, NoticeSize.y + 60f));
            notice = root.GetComponent<RectTransform>();
            notice.anchoredPosition = new Vector2(NoticeCenterX, 0f);
            float top = NoticeSize.y * 0.5f;

            // 卷开的遮罩从上轴往下长；纸本身尺寸固定、贴着遮罩顶边，所以是「露出来」而不是「拉长」。
            GameObject maskObject = ZombieModeUIHelper.CreateRect("Unroll", notice, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, top), new Vector2(NoticeSize.x + 40f, NoticeSize.y), new Vector2(0.5f, 1f));
            unrollMask = maskObject.GetComponent<RectTransform>();
            maskObject.AddComponent<RectMask2D>();

            GameObject paperObject = ZombieModeUIHelper.CreateRect("Paper", unrollMask, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, NoticeSize, new Vector2(0.5f, 1f));
            paper = paperObject.GetComponent<RectTransform>();
            paperImage = paperObject.AddComponent<Image>();
            paperImage.sprite = BossRushInkSkin.GetSurfaceSprite();
            paperImage.type = Image.Type.Sliced;
            paperImage.pixelsPerUnitMultiplier = 1.3f;
            paperImage.color = NoticePaper;
            paperImage.raycastTarget = true;

            Image fiber = CreatePaperLayer(paper, "Fiber", BossRushInkSkin.GetFiberSprite(), new Color(0.30f, 0.20f, 0.10f, 0.20f), 10f);
            fiber.type = Image.Type.Tiled;
            CreateStain(paper, new Vector2(-150f, 300f), 230f, 18f);
            CreateStain(paper, new Vector2(165f, -250f), 280f, -32f);
            Image border = CreatePaperLayer(paper, "Border", BossRushInkSkin.GetFrameSprite(), Fade(NoticeInk, 0.62f), 18f);
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.pixelsPerUnitMultiplier = 1.5f;
            Image inner = CreatePaperLayer(paper, "BorderInner", BossRushInkSkin.GetFrameSprite(), Fade(NoticeInk, 0.22f), 30f);
            inner.type = Image.Type.Sliced;
            inner.fillCenter = false;
            inner.pixelsPerUnitMultiplier = 2.4f;

            GameObject holder = ZombieModeUIHelper.CreateRect("WritHolder", paper, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            writHolder = holder.GetComponent<RectTransform>();

            Button wait = ZombieModeUIHelper.CreateButton("Wait", paper, L10n.T("等等", "Wait"),
                new Vector2(0.5f, 0.5f), new Vector2(-104f, ButtonsY), new Vector2(168f, 52f), BossRushInkColors.SurfaceRaised,
                20f, new Vector2(150f, 38f), Dismiss, true);
            BossRushInkSkin.StyleInkButton(wait, BossRushInkButtonRole.Secondary);
            Button swear = ZombieModeUIHelper.CreateButton("Swear", paper, L10n.T("立誓", "Swear"),
                new Vector2(0.5f, 0.5f), new Vector2(100f, ButtonsY), new Vector2(212f, 56f), BossRushInkColors.CinnabarFill,
                24f, new Vector2(190f, 42f), Swear, true);
            BossRushInkSkin.StyleInkButton(swear, BossRushInkButtonRole.Primary);

            paperEmbers = paperObject.AddComponent<ArenaExpeditionEmberField>();
            paperEmbers.Bind(paper);

            // 上下两根轴在遮罩外，压在纸边上。
            CreateRod("RodTop", top + 2f);
            bottomRod = CreateRod("RodBottom", -top - 2f);
        }

        private RectTransform CreateRod(string name, float y)
        {
            float width = NoticeSize.x + 44f;
            GameObject rodObject = ZombieModeUIHelper.CreateRect(name, notice, new Vector2(0.5f, 0.5f), new Vector2(width, 20f));
            RectTransform rod = rodObject.GetComponent<RectTransform>();
            rod.anchoredPosition = new Vector2(0f, y);
            Image wood = rodObject.AddComponent<Image>();
            wood.sprite = BossRushInkSkin.GetSurfaceSprite();
            wood.type = Image.Type.Sliced;
            wood.pixelsPerUnitMultiplier = 3.2f;
            wood.color = RodWood;
            wood.raycastTarget = false;
            BossRushInkSkin.CreateBrushStroke(rod, "Sheen", new Vector2(width - 30f, 3f), new Vector2(0f, 4f), Fade(BossRushInkColors.Gold, 0.30f));
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject capObject = ZombieModeUIHelper.CreateRect("Cap", rod, new Vector2(0.5f, 0.5f), new Vector2(16f, 32f));
                capObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(side * (width * 0.5f + 6f), 0f);
                Image cap = capObject.AddComponent<Image>();
                cap.sprite = BossRushInkSkin.GetSurfaceSprite();
                cap.type = Image.Type.Sliced;
                cap.pixelsPerUnitMultiplier = 4f;
                cap.color = RodCap;
                cap.raycastTarget = false;
            }
            return rod;
        }

        private static Image CreatePaperLayer(RectTransform parent, string name, Sprite sprite, Color color, float inset)
        {
            GameObject layer = ZombieModeUIHelper.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = layer.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void CreateStain(RectTransform parent, Vector2 position, float size, float angle)
        {
            GameObject stain = ZombieModeUIHelper.CreateRect("Stain", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size * 0.8f));
            RectTransform rect = stain.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = stain.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetBlotSprite();
            image.color = Stain;
            image.raycastTarget = false;
        }

        /// <summary>悬赏令卷开：上轴不动，下轴带着纸往下走，响一声纸。</summary>
        private void PlayUnroll(float delay)
        {
            if (notice == null || unrollMask == null || bottomRod == null) return;
            float full = NoticeSize.y;
            float top = full * 0.5f;
            RectTransform mask = unrollMask;
            RectTransform rod = bottomRod;
            mask.sizeDelta = new Vector2(mask.sizeDelta.x, 0f);
            rod.anchoredPosition = new Vector2(0f, top - 2f);
            BossRushInkMotion motion = BossRushInkMotion.For(notice.gameObject);
            motion.Add(delay, UnrollSeconds, true, t =>
            {
                float h = full * t;
                if (mask != null) mask.sizeDelta = new Vector2(mask.sizeDelta.x, h);
                if (rod != null) rod.anchoredPosition = new Vector2(0f, top - h - 2f);
            });
            motion.Add(delay, 0.01f, false, t => { }, () => BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn));
        }

        // ---- 悬赏令正文：页眉（战帖名）→ 行程 → 战规 → 赏格；选择一变就整张重新落墨 ----

        private void RebuildNotice(bool animate)
        {
            if (writHolder == null) return;
            if (writ != null) Destroy(writ.gameObject);
            bool chinese = L10n.IsChinese;
            bool nightmare = tier == ArenaExpeditionTier.Nightmare;

            GameObject writObject = ZombieModeUIHelper.CreateRect("Writ", writHolder, new Vector2(0.5f, 0.5f), NoticeSize);
            writ = writObject.GetComponent<RectTransform>();
            CanvasGroup group = writObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            if (paperImage != null) paperImage.color = nightmare ? NoticePaperScorched : NoticePaper;
            if (paperEmbers != null) paperEmbers.SetBurning(nightmare);

            if (nightmare)
            {
                // 噩梦：底边一片焦痕，余烬从这里升起。
                GameObject scorch = ZombieModeUIHelper.CreateRect("Scorch", writ, new Vector2(0.5f, 0.5f), new Vector2(NoticeSize.x * 1.15f, 320f));
                scorch.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -NoticeSize.y * 0.5f + 40f);
                Image burn = scorch.AddComponent<Image>();
                burn.sprite = BossRushInkSkin.GetBlotSprite();
                burn.color = new Color(0.26f, 0.06f, 0.035f, 0.26f);
                burn.raycastTarget = false;
            }

            // 页眉：一行朱字「战帖」，下面大字档名，再一道焦墨笔。
            TextMeshProUGUI kicker = NoticeText(writ, "Kicker", L10n.T("战　帖", "WRIT OF WAR"), 14f, NoticeCinnabar, TextAlignmentOptions.Center, false);
            kicker.fontStyle = FontStyles.Bold;
            kicker.characterSpacing = chinese ? 12f : 8f;
            kicker.rectTransform.anchoredPosition = new Vector2(0f, 376f);
            float kickerHalf = Mathf.Min(120f, kicker.GetPreferredValues(kicker.text).x * 0.5f);
            for (int side = -1; side <= 1; side += 2)
            {
                BossRushInkSkin.CreateBrushStroke(writ, "KickerStroke", new Vector2(54f, 4f),
                    new Vector2(side * (kickerHalf + 42f), 376f), Fade(NoticeCinnabar, 0.7f));
            }

            TextMeshProUGUI title = NoticeText(writ, "TierTitle", ArenaExpeditionTuning.TierName(tier), chinese ? 52f : 40f,
                nightmare ? NoticeNightmare : NoticeInk, TextAlignmentOptions.Center, false);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = chinese ? 30f : 8f;
            title.rectTransform.anchoredPosition = new Vector2(0f, chinese ? 318f : 316f);
            if (chinese)
            {
                TextMeshProUGUI caption = NoticeText(writ, "TierCaption", ArenaExpeditionTuning.TierNameEn(tier).ToUpperInvariant(), 12f,
                    NoticeInkSoft, TextAlignmentOptions.Center, false);
                caption.characterSpacing = 14f;
                caption.rectTransform.anchoredPosition = new Vector2(0f, 272f);
            }
            float strokeY = chinese ? 252f : 276f;
            RectTransform stroke = BossRushInkSkin.CreateBrushStroke(writ, "TitleStroke", new Vector2(300f, 10f),
                new Vector2(0f, strokeY), Fade(nightmare ? NoticeNightmare : NoticeInk, 0.85f));

            // 正文从上往下排，量着每段的实际高度；排不下就整段等比缩一点，不截字。
            float stackTop = strokeY - 16f;
            GameObject stackObject = ZombieModeUIHelper.CreateRect("Stack", writ, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, stackTop), new Vector2(StackWidth, 0f), new Vector2(0.5f, 1f));
            RectTransform stack = stackObject.GetComponent<RectTransform>();
            float left = -StackWidth * 0.5f;
            float y = 0f;

            TextMeshProUGUI course = NoticeText(stack, "Course", ArenaExpeditionTuning.CourseLine(lineup, length, poolCount), 14.5f,
                NoticeInkSoft, TextAlignmentOptions.Top, true);
            y -= PlaceBlock(course, left, y, StackWidth) + 14f;

            y -= SectionHeader(stack, "战　规", "RULES", y) + 8f;
            IList<ArenaExpeditionRule> rules = ArenaExpeditionTuning.RulesFor(tier);
            if (rules.Count == 0)
            {
                TextMeshProUGUI none = NoticeText(stack, "NoRules", L10n.T("照旧规矩，一条不加", "House rules. Nothing added."), 14f,
                    NoticeInkSoft, TextAlignmentOptions.Top, true);
                none.fontStyle = FontStyles.Italic;
                y -= PlaceBlock(none, left, y, StackWidth) + 10f;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null) continue;
                y -= PlaceRule(stack, rules[i], y) + 4f;
            }

            y -= 8f;
            y -= SectionHeader(stack, "赏　格", "REWARDS", y) + 8f;
            List<KeyValuePair<string, bool>> rewards = ArenaExpeditionTuning.RewardLines(tier, lineup, length, poolCount);
            for (int i = 0; i < rewards.Count; i++)
            {
                y -= PlaceReward(stack, rewards[i].Key, rewards[i].Value, y) + 3f;
            }

            float used = -y;
            float room = stackTop - StackBottom;
            if (used > room) stack.localScale = Vector3.one * (room / used);

            if (!animate) return;
            RectTransform rect = writ;
            group.alpha = 0f;
            rect.anchoredPosition = new Vector2(0f, -6f);
            BossRushInkMotion motion = BossRushInkMotion.For(writObject);
            motion.Add(0f, 0.26f, true, t =>
            {
                if (rect == null) return;
                group.alpha = t;
                rect.anchoredPosition = new Vector2(0f, -6f * (1f - t));
            });
            BossRushInkSkin.PlayBrushWrite(stroke, 0.04f, 0.34f, motion);
        }

        /// <summary>一条战规：左边类别印（英文界面是一行朱字），右边名字粗体、说明在下。返回占用高度。</summary>
        private static float PlaceRule(Transform parent, ArenaExpeditionRule rule, float top)
        {
            bool chinese = L10n.IsChinese;
            float left = -StackWidth * 0.5f;
            float indent = chinese ? 54f : 64f;
            Color mark = rule.Grace ? GraceSeal : NoticeCinnabar;
            if (chinese)
            {
                RectTransform seal = BossRushInkSkin.CreateSeal(parent, ArenaExpeditionTuning.KindSeal(rule.Kind), 40f);
                seal.anchoredPosition = new Vector2(left + 20f, top - 22f);
                seal.localRotation = Quaternion.Euler(0f, 0f, rule.Grace ? 4f : -4f);
                Image sealImage = seal.GetComponent<Image>();
                if (sealImage != null) sealImage.color = mark;
            }
            else
            {
                TextMeshProUGUI kind = NoticeText(parent, "Kind", ArenaExpeditionTuning.KindLabelEn(rule.Kind), 11f, mark, TextAlignmentOptions.Left, false);
                kind.fontStyle = FontStyles.Bold;
                kind.characterSpacing = 2f;
                PlaceBlock(kind, left, top - 4f, indent - 6f);
            }
            float width = StackWidth - indent;
            TextMeshProUGUI name = NoticeText(parent, "RuleName", rule.Name, 17f, NoticeInk, TextAlignmentOptions.Left, false);
            name.fontStyle = FontStyles.Bold;
            PlaceBlock(name, left + indent, top + 3f, width);
            TextMeshProUGUI line = NoticeText(parent, "RuleLine", rule.Line, 13f, NoticeInkSoft, TextAlignmentOptions.TopLeft, true);
            float lineHeight = PlaceBlock(line, left + indent, top - 22f, width);
            return Mathf.Max(44f, 22f + lineHeight);
        }

        /// <summary>赏格一行：小墨点 + 一句；吃亏的那句用朱字。</summary>
        private static float PlaceReward(Transform parent, string text, bool negative, float top)
        {
            float left = -StackWidth * 0.5f;
            Color color = negative ? NoticeCinnabar : NoticeInk;
            GameObject dot = ZombieModeUIHelper.CreateRect("Dot", parent, new Vector2(0.5f, 0.5f), new Vector2(7f, 7f));
            RectTransform dotRect = dot.GetComponent<RectTransform>();
            dotRect.anchoredPosition = new Vector2(left + 8f, top - 11f);
            dotRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image dotImage = dot.AddComponent<Image>();
            dotImage.sprite = BossRushInkSkin.GetSealSprite();
            dotImage.color = color;
            dotImage.raycastTarget = false;
            TextMeshProUGUI label = NoticeText(parent, "Reward", text, 14f, color, TextAlignmentOptions.TopLeft, true);
            return PlaceBlock(label, left + 22f, top, StackWidth - 22f);
        }

        /// <summary>小节标题：居中粗体，两侧各一道淡墨笔。返回占用高度。</summary>
        private static float SectionHeader(Transform parent, string zh, string en, float top)
        {
            bool chinese = L10n.IsChinese;
            TextMeshProUGUI head = NoticeText(parent, "Section", L10n.T(zh, en), 15f, NoticeInk, TextAlignmentOptions.Center, false);
            head.fontStyle = FontStyles.Bold;
            head.characterSpacing = chinese ? 10f : 6f;
            float height = LineBox(15f);
            float middle = top - height * 0.5f;
            head.rectTransform.sizeDelta = new Vector2(220f, height);
            head.rectTransform.anchoredPosition = new Vector2(0f, middle);
            float half = Mathf.Min(90f, head.GetPreferredValues(head.text).x * 0.5f);
            float strokeWidth = StackWidth * 0.5f - half - 24f;
            for (int side = -1; side <= 1; side += 2)
            {
                BossRushInkSkin.CreateBrushStroke(parent, "SectionStroke", new Vector2(strokeWidth, 5f),
                    new Vector2(side * (half + 16f + strokeWidth * 0.5f), middle), Fade(NoticeInk, 0.5f));
            }
            return height;
        }

        private static TextMeshProUGUI NoticeText(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment, bool wrap)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(name, parent, text ?? string.Empty, size, Vector2.zero,
                new Vector2(StackWidth, LineBox(size)), alignment, color);
            BossRushUI.ApplyGameFont(label);
            label.raycastTarget = false;
            label.enableWordWrapping = wrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>按实际排版高度摆一段字（左上角在 left / top），返回占用高度。单行框高至少 1.45×字号 + 4。</summary>
        private static float PlaceBlock(TextMeshProUGUI label, float left, float top, float width)
        {
            float height = LineBox(label.fontSize);
            if (label.enableWordWrapping)
            {
                float preferred = label.GetPreferredValues(label.text, width, 0f).y;
                height = Mathf.Max(height, Mathf.Ceil(preferred) + 4f);
            }
            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left + width * 0.5f, top - height * 0.5f);
            return height;
        }

        private static float LineBox(float size)
        {
            return Mathf.Ceil(size * 1.45f) + 4f;
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // ------------------------------------------------------------------
        // 选择
        // ------------------------------------------------------------------

        private void SelectLineup(ArenaExpeditionLineup value)
        {
            if (lineup == value) return;
            lineup = value;
            RefreshAll(true);
        }

        private void SelectTier(ArenaExpeditionTier value)
        {
            if (tier == value) return;
            tier = value;
            if (value == ArenaExpeditionTier.Nightmare && Time.unscaledTime - lastBellTime > 3f)
            {
                lastBellTime = Time.unscaledTime;
                lastPaperTime = Time.unscaledTime;
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxTempleBell);
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxEmberFlare);
            }
            RefreshAll(true);
        }

        private void SelectLength(ArenaExpeditionLength value)
        {
            if (length == value || lineup == ArenaExpeditionLineup.Hell) return;
            length = value;
            RefreshAll(true);
        }

        private void RefreshAll(bool animate)
        {
            bool hell = lineup == ArenaExpeditionLineup.Hell;
            for (int i = 0; i < lineupCards.Length; i++) ApplyCardState(lineupCards[i], i == (int)lineup, false, animate);
            for (int i = 0; i < tierCards.Length; i++)
            {
                ApplyCardState(tierCards[i], i == (int)tier, i == (int)ArenaExpeditionTier.Nightmare, animate);
            }
            for (int i = 0; i < lengthCards.Length; i++)
            {
                ApplyCardState(lengthCards[i], !hell && i == (int)length, false, animate);
                if (lengthCards[i] != null && lengthCards[i].Button != null) lengthCards[i].Button.interactable = !hell;
            }
            if (lengthRow != null)
            {
                lengthRow.alpha = hell ? 0.35f : 1f;
                lengthRow.blocksRaycasts = !hell;
            }
            if (animate && Time.unscaledTime - lastPaperTime > 0.2f)
            {
                lastPaperTime = Time.unscaledTime;
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
            }
            RebuildNotice(animate);
        }

        private void ApplyCardState(Card card, bool selected, bool nightmare, bool animate)
        {
            if (card == null || card.Button == null) return;
            bool burning = selected && nightmare;
            ZombieModeUIHelper.SetButtonBaseColor(card.Button, burning ? NightmareFill : selected ? SelectedFill : BossRushInkColors.SurfaceRaised);
            BossRushInkSkin.SetFrameColor(card.Button.targetGraphic, burning ? BossRushInkColors.Ember : selected ? BossRushInkColors.Gold : IdleFrame);
            if (card.Name != null) card.Name.color = selected ? BossRushInkColors.Paper : BossRushInkColors.PaperDim;
            if (card.Sub != null) card.Sub.color = selected ? (burning ? BossRushInkColors.CinnabarText : BossRushInkColors.Paper) : BossRushInkColors.PaperDim;
            if (card.Pips != null) card.Pips.alpha = selected ? 1f : 0.5f;
            if (card.Embers != null) card.Embers.SetBurning(burning);

            bool wasSelected = card.Selected;
            card.Selected = selected;
            if (card.Underline == null) return;
            RectTransform underline = card.Underline;
            float full = card.UnderlineWidth;
            if (!selected)
            {
                underline.sizeDelta = new Vector2(0f, underline.sizeDelta.y);
                return;
            }
            if (!animate)
            {
                underline.sizeDelta = new Vector2(full, underline.sizeDelta.y);
                return;
            }
            if (wasSelected) return;
            underline.sizeDelta = new Vector2(0f, underline.sizeDelta.y);
            BossRushInkMotion.For(card.Rect.gameObject).Add(0f, 0.32f, true, t =>
            {
                if (underline != null) underline.sizeDelta = new Vector2(full * t, underline.sizeDelta.y);
            });
        }

        // ------------------------------------------------------------------
        // 结论
        // ------------------------------------------------------------------

        private void Swear()
        {
            if (closing) return;
            closing = true;
            ArenaExpeditionOrder order = new ArenaExpeditionOrder(lineup, tier, length);
            order.Remember(length);

            // 朱印落在悬赏令的赏格上，落定后墨池收拢，再开第一波。
            if (paper != null)
            {
                RectTransform seal = BossRushInkSkin.CreateSeal(paper, ArenaExpeditionTuning.TierSeal(tier), 112f);
                seal.anchoredPosition = new Vector2(118f, -250f);
                seal.localRotation = Quaternion.Euler(0f, 0f, -11f);
                if (tier == ArenaExpeditionTier.Nightmare)
                {
                    Image sealImage = seal.GetComponent<Image>();
                    if (sealImage != null) sealImage.color = new Color(0.52f, 0.10f, 0.08f, 1f);
                }
                BossRushInkSkin.StampSeal(seal, notice, 0f);
            }

            BossRushInteractable starter = source;
            BossRushInkMotion.For(gameObject).Add(0.55f, 0.01f, false, t => { }, () =>
            {
                ReleaseInput();
                if (instance == this) instance = null;
                BossRushInkSkin.PlayInkDismiss(gameObject, sheet);
                try
                {
                    if (starter != null) starter.BeginFromExpeditionOrder(order);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BossRush] 出征令立誓后开波失败: " + e.Message);
                }
            });
        }

        /// <summary>「等等」：不开波，收起出征令（ESC / 手柄取消同此）。</summary>
        private void Dismiss()
        {
            if (closing) return;
            closing = true;
            ReleaseInput();
            if (instance == this) instance = null;
            BossRushInkSkin.PlayInkDismiss(gameObject, sheet);
        }

        private void ReleaseInput()
        {
            try
            {
                if (cancelKey != null)
                {
                    cancelKey.Detach();
                    cancelKey = null;
                }
                if (lease != null)
                {
                    lease.Release();
                    lease = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令归还输入失败: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            ReleaseInput();
            if (instance == this) instance = null;
        }
    }

    /// <summary>
    /// 噩梦的余烬：卡片 / 悬赏令边缘不断升起几点金红火星（最多 14 点同屏），只在噩梦被选中时燃。
    /// 走 unscaled 时间，官方暂停菜单开着时冻结；宿主销毁火星一起销毁。
    /// </summary>
    internal sealed class ArenaExpeditionEmberField : MonoBehaviour
    {
        private const int MaxMotes = 14;
        private const float SpawnInterval = 0.11f;

        private RectTransform area;
        private bool burning;
        private float timer;
        private int alive;

        internal void Bind(RectTransform card)
        {
            area = card;
        }

        internal void SetBurning(bool value)
        {
            if (value && !burning) timer = 0f;
            burning = value;
        }

        private void Update()
        {
            if (!burning || area == null || BossRushUI.IsGamePaused()) return;
            timer -= Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (timer > 0f || alive >= MaxMotes) return;
            timer = SpawnInterval;
            SpawnMote();
        }

        private void SpawnMote()
        {
            Vector2 size = area.rect.size;
            float side = UnityEngine.Random.value;
            Vector2 start;
            if (side < 0.55f) start = new Vector2(UnityEngine.Random.Range(-0.48f, 0.48f) * size.x, -size.y * 0.48f);
            else if (side < 0.775f) start = new Vector2(-size.x * 0.48f, UnityEngine.Random.Range(-0.45f, 0.2f) * size.y);
            else start = new Vector2(size.x * 0.48f, UnityEngine.Random.Range(-0.45f, 0.2f) * size.y);

            float moteSize = UnityEngine.Random.Range(5f, 11f);
            GameObject mote = ZombieModeUIHelper.CreateRect("Ember", area, new Vector2(0.5f, 0.5f), new Vector2(moteSize, moteSize));
            RectTransform rect = mote.GetComponent<RectTransform>();
            rect.anchoredPosition = start;
            Image image = mote.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetGlowSprite();
            image.raycastTarget = false;
            Color tint = Color.Lerp(BossRushInkColors.Ember, BossRushInkColors.Gold, UnityEngine.Random.value * 0.6f);
            image.color = new Color(tint.r, tint.g, tint.b, 0f);

            float rise = UnityEngine.Random.Range(36f, 92f);
            float drift = UnityEngine.Random.Range(-18f, 18f);
            float life = UnityEngine.Random.Range(1.0f, 1.7f);
            alive++;
            BossRushInkMotion.For(mote).Add(0f, life, false, t =>
            {
                if (rect == null) return;
                rect.anchoredPosition = start + new Vector2(drift * t + Mathf.Sin(t * 9f) * 3f, rise * t);
                float a = t < 0.2f ? t / 0.2f : 1f - (t - 0.2f) / 0.8f;
                image.color = new Color(tint.r, tint.g, tint.b, 0.9f * a);
            }, () =>
            {
                alive = Mathf.Max(0, alive - 1);
                if (mote != null) Destroy(mote);
            });
        }
    }
}
