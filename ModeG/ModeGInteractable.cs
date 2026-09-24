using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Mode G 场内交互入口（规格 §15 重写版）。
    ///
    /// 契约：
    /// - 继承 InteractableBase（MonoBehaviour 链），可被 AddComponent&lt;ModeGInteractable&gt;()（任务 #7 接线）；
    /// - IsInteractable 统一受 Availability（生产闸/开发闸）、MapSupportRegistry、
    ///   PresentationAssetCache 预检与 IsModeGEntryBlocked 门控；
    /// - OnTimeOut 只创建/复用不可变 ModeGEntryPreview 并打开确认页；
    ///   不引用 ConfigureBossRushMode/StartFirstWave（Legacy 推进路径隔离）；
    /// - 确认页确认后唯一调用 host.TryStartModeG()。
    /// </summary>
    public sealed class ModeGInteractable : InteractableBase
    {
        private static ModeGInteractable _activeConfirmation;

        // ── 确认页版式（2026-09-23 按 owner 实测第 16 条重排）──────────────────
        // 旧版每段写死一个中心 y：徽记（+142..+226）同时压住标题（+195..+255）和规则（+109..+187），
        // 横幅按 preserveAspect 缩成中间一小块、alpha 0.28 几乎看不见，按钮下面空一截。
        // 现在从面板顶边往下逐段排，每段文字按实际内容量高，面板最后收到内容高度——换语言、换契约都不会再叠字。
        private const float ModalWidth = 780f;
        /// <summary>建面板时的初始高度；排版完成后按内容收口（见 OpenConfirmPage 末尾）。</summary>
        private const float ModalInitialHeight = 560f;
        private const float PadX = 36f;
        private const float ContentWidth = ModalWidth - PadX * 2f;
        /// <summary>主视觉横幅离面板边的距离：留出面板自己那圈描边与圆角。</summary>
        private const float HeroInset = 10f;
        private const float HeroHeight = 160f;
        private const int HeroRadius = 10;
        /// <summary>
        /// 横幅裁切带的中心（占原图高度的比例，从上往下量）。1024×576 的横幅铺满 760 宽时高约 428，
        /// 160 高的裁切带露出 0.33–0.70：宿敌的眼睛、回声队列与小鸭的围巾都在框里。
        /// </summary>
        private const float HeroFocusY = 0.515f;
        private const float EmblemSize = 64f;
        private const float EmblemGap = 14f;
        private const float CardGap = 16f;
        private const float CardPadX = 14f;
        private const float CardPadY = 12f;
        private const int CardRadius = 10;
        /// <summary>
        /// 选中卡片向 WarningText（本模式的金色强调色）染色的比例（UI 共识对照审查 B-24：选中态统一金色描边，
        /// 不再混第二种强调色 Accent）。0.10 时白字 14.6:1、次级字 7.2:1（线性亮度实算）。
        /// </summary>
        private const float CardSelectedTint = 0.10f;
        /// <summary>
        /// 卡片悬停提亮比例。不用 BossRushUI.GetHoverColor 的 0.22：那会把 16px 次级说明字压到 4.4:1，
        /// 0.12 时常态卡 6.3:1、选中卡 4.9:1，都过正文 4.5:1。
        /// </summary>
        private const float CardHoverLift = 0.12f;
        private const float BadgeHeight = 24f;
        private const float ButtonWidth = 240f;
        private const float ButtonHeight = 52f;
        private const float ButtonGap = 20f;
        private const float BottomPad = 24f;
        /// <summary>
        /// 单行文本框的最小高度系数：游戏中文字体一行约 1.45 倍字号。框比一行矮时 TMP 的 Ellipsis
        /// 会把整串清空，所以每个文本框至少给一行高（MeasureTextHeight 另加 4px 余量）。
        /// </summary>
        private const float LineHeightFactor = 1.45f;
        private const float CardStagger = 0.06f;
        private const float CardEntrance = 0.22f;
        private const float CardRise = 10f;

        private GameObject _modalRoot;
        private ZombieModeUIHelper.ModalInputLease _inputLease;
        private int _selectedCandidateIndex;
        private Button[] _cardButtons;
        private Image[] _cardStrokes;
        private GameObject[] _cardBadges;
        private ModeGEntryPreview _modalPreview;
        private ModBehaviour _entryHost;
        private ModeGModalCancelKey _cancelKey;
        private bool _confirmed;
        private bool _autoPresenter;

        internal static bool IsConfirmationOpen
        {
            get { return _activeConfirmation != null && _activeConfirmation._modalRoot != null; }
        }

        /// <summary>过图/关停时幂等关闭仍存活的入场确认页。</summary>
        internal static void CloseActiveConfirmation()
        {
            try
            {
                if (_activeConfirmation != null) _activeConfirmation.CloseModal();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 入场确认页清理失败: " + e.Message);
            }
        }

        internal static bool LastConfirmationAttemptedStart { get; private set; }

        /// <summary>
        /// 自动船票入场与路牌入口共用同一确认页。自动流程只负责创建短命
        /// presenter，不直接选择契约或扣除物品。
        /// </summary>
        internal static bool TryOpenConfirmation(ModBehaviour host)
        {
            LastConfirmationAttemptedStart = false;
            if (host == null || _activeConfirmation != null) return false;
            GameObject obj = null;
            try
            {
                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                    return false;
                if (ModeGRuntimeGates.IsModeGEntryBlocked) return false;
                if (!ModeGMapSupportRegistry.IsVerifiedSceneName(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return false;
                if (!ModeGPresentationAssetCache.TryPreflight()) return false;

                ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
                if (preview == null || preview.contractCandidateIds == null
                    || preview.contractCandidateIds.Length < 2
                    || !host.IsModeGEntryPreviewValidForCurrentScene(preview))
                {
                    return false;
                }

                obj = new GameObject("ModeG_AutoConfirmPresenter");
                UnityEngine.Object.DontDestroyOnLoad(obj);
                ModeGInteractable presenter = obj.AddComponent<ModeGInteractable>();
                presenter._autoPresenter = true;
                _activeConfirmation = presenter;
                return presenter.OpenConfirmPage(host, preview);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 自动入口确认页创建失败: " + e.Message);
                _activeConfirmation = null;
                try { if (obj != null) UnityEngine.Object.Destroy(obj); }
                catch (Exception cleanupException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 自动确认页对象清理失败: "
                        + cleanupException.Message);
                }
                return false;
            }
        }

        #region InteractableBase Overrides

        protected override void Awake()
        {
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = "BossRush_ModeG_Preview";
                InteractName = "BossRush_ModeG_Preview";
                interactCollider = GetComponent<Collider>();
            }
            catch { }
            try { base.Awake(); } catch { }
            try { MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = "BossRush_ModeG_Preview";
                InteractName = "BossRush_ModeG_Preview";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            try
            {
                // 正式入口或显式开发入口均可见；两个开关都关闭时隐藏。
                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                {
                    return false;
                }
                if (ModeGRuntimeGates.IsModeGEntryBlocked) return false;
                if (!ModeGMapSupportRegistry.IsVerifiedSceneName(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                {
                    return false;
                }
                // 展示资源预检（fail-closed；缓存结果，bundle 每 runtime 一次加载）
                return ModeGPresentationAssetCache.TryPreflight();
            }
            catch { return false; }
        }

        /// <summary>
        /// 交互触发：只创建/复用不可变 preview 并打开确认页（规格 §15）。
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                if (_modalRoot != null) return; // 确认页已打开（幂等）
                if (!IsInteractable()) return;

                ModBehaviour host = ModBehaviour.Instance;
                if (host == null) return;

                ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
                if (preview == null || preview.contractCandidateIds == null
                    || preview.contractCandidateIds.Length < 2
                    || !host.IsModeGEntryPreviewValidForCurrentScene(preview))
                {
                    host.ShowMessage(L10n.T(
                        "宿命回响入口还没准备好，稍后再试。",
                        "Fate Echo entry isn't ready yet. Try again in a moment."));
                    return;
                }

                if (!OpenConfirmPage(host, preview))
                {
                    host.ShowMessage(L10n.T(
                        "宿命回响确认页没法安全暂停战斗，稍后再试。",
                        "Fate Echo couldn't safely pause the fight to confirm. Try again."));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ModeGInteractable.OnTimeOut 异常: " + e.Message);
                try { CloseModal(); }
                catch (Exception closeException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 异常确认页关闭失败: " + closeException.Message);
                }
            }
        }

        #endregion

        #region Confirm Page（确认页）

        private bool OpenConfirmPage(ModBehaviour host, ModeGEntryPreview preview)
        {
            _activeConfirmation = this;
            _entryHost = host;
            _confirmed = false;
            _modalPreview = preview;
            // 默认选中第一张契约（UI 共识对照审查 B-13）：打开就是能直接开打的状态，「立即迎战」不再先置灰。
            _selectedCandidateIndex = 0;

            GameObject root = BossRushUI.CreateCanvasRoot("ModeG_ConfirmPage", BossRushUILayers.ModeGEntry, true).gameObject;
            UnityEngine.Object.DontDestroyOnLoad(root);
            _modalRoot = root;

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeG_Confirm", root.transform, new Vector2(ModalWidth, ModalInitialHeight),
                BossRushUIColors.WarningText);
            MakeSurfaceOpaque(surface);

            Transform st = surface.transform;
            // 纵向游标：从面板顶边往下量的距离。下面每一段都挂在面板顶边上、排完把游标往下推。
            float cursor = HeroInset;

            // 主视觉：横幅按原图比例铺满面板宽度，上下裁掉，只露中间那一带（圆角 Mask 切边）。
            try
            {
                Sprite banner = ModeGPresentationAssetCache.GetBannerSprite();
                if (banner != null)
                {
                    RectTransform hero = CreateHeroClip(st, cursor);
                    Vector2 artSize = GetHeroArtSize(banner);
                    GameObject bannerObj = ZombieModeUIHelper.CreateRect("Banner", hero, new Vector2(0.5f, 0.5f), artSize);
                    bannerObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, GetHeroArtOffset(artSize.y));
                    Image bannerImage = bannerObj.AddComponent<Image>();
                    bannerImage.sprite = banner;
                    bannerImage.preserveAspect = true;
                    bannerImage.raycastTarget = false;
                    // 描边建在插图之后，画在它上面；同在 Mask 里，圆角与模板一致。
                    BossRushUI.ApplyPanelStroke(hero.GetComponent<Image>(), HeroRadius,
                        BossRushUISkinPart.Card, BossRushUIColors.Stroke);
                    cursor += HeroHeight + 14f;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页横幅加载失败: " + e.Message);
            }

            // 徽记 + 标题同一行、整组居中（展示缓存提供徽记；缺失时标题单独居中）
            RectTransform emblemRect = CreateEmblem(st, EmblemSize);
            cursor += PlaceTitleRow(st, emblemRect, cursor, L10n.T("宿命回响", "Fate Echo"), 32f,
                BossRushUIColors.WarningText, ContentWidth, EmblemSize) + 8f;

            // 玩法说明：三句大白话——怎么打、敌人会怎样、能拿到什么。「决意」金色标出，和局内 HUD 同一个词。
            // 长段落左对齐、按 ①②③ 分条（2026-09-23 审美审查 UB-19：居中长句行首参差，读不出是三条）。
            string gold = ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText);
            cursor += PlaceText(CreateParagraphText("Rules", st,
                L10n.T(
                    "① 带上自己的装备连打九波，敌人会专门针对你上一波的打法。\n"
                    + "② 换个距离、弹药或武器破解针对，就能攒下<color=#" + gold + ">决意</color>。\n"
                    + "③ 九波全部打赢后按决意发 6–10 件高品质物品（Q5–Q8），并退还信物。",
                    "① Bring your own gear through 9 waves. Enemies adapt to how you fought the last wave.\n"
                    + "② Switch range, ammo or weapon to beat the counter and earn <color=#" + gold + ">Resolve</color>.\n"
                    + "③ Clear all 9 waves for 6–10 Q5–Q8 items (more Resolve, more items); your relic comes back."),
                16f, BossRushUIColors.TextSecondary), ContentWidth, 0f, cursor) + 12f;

            // 宿敌回响（有活跃宿敌时显示）：整局的威胁提示，放在选契约之前
            string nemesisLine = string.Empty;
            try
            {
                if (ModeGNemesisPersistence.HasActiveNemesis())
                {
                    ModeGNemesisPersistence.NemesisRecordDto nemesis = ModeGNemesisPersistence.LoadOrInit();
                    if (nemesis != null && !string.IsNullOrEmpty(nemesis.bossPresetKey))
                    {
                        nemesisLine = L10n.T("你的宿敌还在等你：", "Your nemesis is waiting: ")
                            + ModeGEncounterVariation.GetManagedBossDisplayName(nemesis.bossPresetKey);
                    }
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(nemesisLine))
            {
                cursor += PlaceText(CreateBodyText("Nemesis", st, nemesisLine, 16f, BossRushUIColors.DangerText),
                    ContentWidth, 0f, cursor) + 10f;
            }

            // 契约二选一：标题后面用次级字补一句「只是额外目标」，免得玩家以为选了会变难
            string secondary = ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary);
            cursor += PlaceText(CreateBodyText("ContractHeader", st,
                L10n.T(
                    "选择本局的宿命契约  <size=14><color=#" + secondary + ">额外目标，不改变敌人强度和奖励件数</color></size>",
                    "Choose this run's Fate Contract  <size=14><color=#" + secondary + ">bonus goal; enemy strength and rewards stay the same</color></size>"),
                20f, BossRushUIColors.TextPrimary), ContentWidth, 0f, cursor) + 8f;
            cursor += BuildContractCards(st, preview, cursor) + 10f;
            RefreshContractCards();

            // 下一枚印章目标（契约图鉴进度）
            try
            {
                string sealLine = ModeGRecapPanel.ComposeEntrySealLine();
                if (!string.IsNullOrEmpty(sealLine))
                {
                    cursor += PlaceText(CreateBodyText("SealGoal", st, sealLine, 14f, BossRushUIColors.TextSecondary),
                        ContentWidth, 0f, cursor);
                }
            }
            catch { /* 呈现失败不影响确认页 */ }

            // 分隔线：上面是选择，下面是入场须知
            cursor += 10f;
            GameObject divider = ZombieModeUIHelper.CreateSeparator("Divider", st,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(cursor + 4f)), 1f, BossRushUIColors.Divider);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.sizeDelta = new Vector2(-PadX * 2f, dividerRect.sizeDelta.y);
            cursor += 18f;

            // 强制披露（规格 §3.1）：死亡损失遵循当前地图规则 + 多攒决意的备装建议。
            // 两行都必须在扣除入场物品前对玩家可见，不得省略。
            cursor += PlaceText(CreateParagraphText("Disclosure", st,
                L10n.T("BossRush_ModeG_Entry_DeathRule") + "\n"
                    + L10n.T("BossRush_ModeG_Entry_LoadoutHint"),
                14f, BossRushUIColors.TextSecondary), ContentWidth, 0f, cursor) + 6f;

            // 入场消耗紧贴按钮：点下去之前最后看到的就是代价。取消什么都不扣（预扣船票由 CloseModal 退回）。
            cursor += PlaceText(CreateBodyText("Cost", st,
                L10n.T("入场消耗：船票 ×1、宿命回响信物 ×1。点「暂不挑战」不扣任何东西。",
                    "Entry cost: 1 ticket + 1 Fate Echo relic. \"Not Now\" costs nothing."),
                16f, BossRushUIColors.TextPrimary), ContentWidth, 0f, cursor) + 16f;

            // 暂不挑战（免费退出，次级样式，左）/ 立即迎战（本屏唯一主操作，最右；UI 共识对照审查 B-23）。
            // 契约默认已选第一张，「立即迎战」打开就能点，不挂灰按钮（B-13）。
            // 主操作底色走 AccentFill（全 Mod 按钮口径，2026-09-23）：旧版是 Success 平涂绿，与 Mode H / Mode E 三套主操作色（UB-33）。
            float buttonY = -(cursor + ButtonHeight * 0.5f);
            float buttonX = (ButtonWidth + ButtonGap) * 0.5f;
            // 「放弃挑战」在局内是不可逆的弃局（ModeGAbandonPresenter）；这里只是免费退出、还会退回船票，
            // 所以不用危险色、也不叫放弃。次级样式：SurfaceRaised 底 + Stroke 描边（SurfaceRaised 对面板底只有 1.03:1）。
            Button cancelButton = ZombieModeUIHelper.CreateButton(
                "Cancel", st, L10n.T("暂不挑战", "Not Now"),
                new Vector2(0.5f, 1f), new Vector2(-buttonX, buttonY), new Vector2(ButtonWidth, ButtonHeight),
                BossRushUIColors.SurfaceRaised, 20f, new Vector2(ButtonWidth - 24f, ButtonHeight - 12f),
                CloseModal, true);
            BossRushUIKit.StyleSecondaryButton(cancelButton);
            ZombieModeUIHelper.CreateButton(
                "Start", st, L10n.T("立即迎战", "Fight Now"),
                new Vector2(0.5f, 1f), new Vector2(buttonX, buttonY), new Vector2(ButtonWidth, ButtonHeight),
                BossRushUIColors.AccentFill, 20f, new Vector2(ButtonWidth - 24f, ButtonHeight - 12f),
                () => ConfirmAndStart(host), true);
            cursor += ButtonHeight + BottomPad;

            // 面板收到内容高度。子物体都挂在顶边上，改高度不会挪动它们。
            RectTransform surfaceRect = surface.GetComponent<RectTransform>();
            surfaceRect.sizeDelta = new Vector2(ModalWidth, Mathf.Ceil(cursor));
            BossRushUI.PlayOpenAnimation(surface);
            // ESC / 手柄取消 = 「暂不挑战」：同一下 ESC 不再穿透去开官方暂停菜单（UB-19）。
            _cancelKey = ModeGModalCancelKey.Attach(root, CloseModal);

            try
            {
                _inputLease = ZombieModeUIHelper.ClaimModalInput(root, "ModeGConfirmPage");
                if (_inputLease == null || !ZombieModeUIHelper.IsModalInputPaused)
                {
                    throw new InvalidOperationException("Mode G confirmation could not acquire modal pause");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页输入占用失败: " + e.Message);
                CloseModal();
                return false;
            }
            return true;
        }

        #region 确认页版式辅助

        /// <summary>横幅裁切容器：Card 档圆角图当模板（showMaskGraphic=false，只写模板缓冲、自己不画）。</summary>
        private static RectTransform CreateHeroClip(Transform parent, float top)
        {
            GameObject clip = ZombieModeUIHelper.CreateRect("Hero", parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top),
                new Vector2(ModalWidth - HeroInset * 2f, HeroHeight), new Vector2(0.5f, 1f));
            Image stencil = clip.AddComponent<Image>();
            stencil.color = Color.white;
            stencil.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(stencil, HeroRadius, BossRushUISkinPart.Card);
            Mask mask = clip.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            return clip.GetComponent<RectTransform>();
        }

        /// <summary>按原图比例铺满横幅宽度；原图比横幅还扁时改按高度铺满，保证裁切框里没有空边。</summary>
        private static Vector2 GetHeroArtSize(Sprite banner)
        {
            float heroWidth = ModalWidth - HeroInset * 2f;
            float aspect = banner.rect.height > 0.5f ? banner.rect.width / banner.rect.height : 16f / 9f;
            if (aspect <= 0.01f) aspect = 16f / 9f;
            Vector2 size = new Vector2(heroWidth, heroWidth / aspect);
            if (size.y < HeroHeight) size = new Vector2(HeroHeight * aspect, HeroHeight);
            return size;
        }

        /// <summary>把原图 HeroFocusY 那一带移到裁切框中央；夹住偏移，插图边缘不会露进框里。</summary>
        private static float GetHeroArtOffset(float artHeight)
        {
            float slack = Mathf.Max(0f, (artHeight - HeroHeight) * 0.5f);
            return Mathf.Clamp((HeroFocusY - 0.5f) * artHeight, -slack, slack);
        }

        /// <summary>
        /// Mode G 徽记（展示缓存提供）。锚在父物体顶边中央，位置由 <see cref="PlaceTitleRow"/> 定；
        /// 取不到返回 null，标题单独居中（不退回汉字或灰方块）。入场页与弃局页共用。
        /// </summary>
        internal static RectTransform CreateEmblem(Transform parent, float size)
        {
            try
            {
                Sprite emblem = ModeGPresentationAssetCache.GetEmblemSprite();
                if (emblem == null) return null;
                GameObject emblemObj = ZombieModeUIHelper.CreateRect(
                    "Emblem", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(size, size), new Vector2(0.5f, 1f));
                Image emblemImage = emblemObj.AddComponent<Image>();
                emblemImage.sprite = emblem;
                emblemImage.preserveAspect = true;
                emblemImage.raycastTarget = false;
                return emblemObj.GetComponent<RectTransform>();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页徽记加载失败: " + e.Message);
                return null;
            }
        }

        /// <summary>徽记在左、标题在右，按标题实际宽度整组居中。返回这一行的高度。入场页与弃局页共用。</summary>
        internal static float PlaceTitleRow(Transform parent, RectTransform emblem, float top, string text,
            float fontSize, Color color, float contentWidth, float emblemSize)
        {
            TextMeshProUGUI title = CreateBodyText("Title", parent, text, fontSize, color);
            float maxWidth = emblem != null ? contentWidth - emblemSize - EmblemGap : contentWidth;
            title.enableAutoSizing = false;
            title.margin = Vector4.zero;
            float width = Mathf.Min(maxWidth, Mathf.Ceil(title.GetPreferredValues(title.text).x) + 8f);
            float height = PlaceText(title, width, 0f, top);
            float row = emblem != null ? Mathf.Max(height, emblemSize) : height;
            float group = emblem != null ? emblemSize + EmblemGap + width : width;
            float left = -group * 0.5f;
            if (emblem != null)
            {
                emblem.anchoredPosition = new Vector2(left + emblemSize * 0.5f, -(top + (row - emblemSize) * 0.5f));
                left += emblemSize + EmblemGap;
            }
            title.rectTransform.anchoredPosition = new Vector2(left + width * 0.5f, -(top + (row - height) * 0.5f));
            return row;
        }

        private static TextMeshProUGUI CreateBodyText(string name, Transform parent, string text, float fontSize,
            Color color)
        {
            return ZombieModeUIHelper.CreateText(name, parent, text, fontSize, Vector2.zero,
                new Vector2(ContentWidth, LineHeight(fontSize)), TextAlignmentOptions.Center, color);
        }

        /// <summary>多行说明段落：左对齐（居中的长句行首参差，读不出分条），其余同 <see cref="CreateBodyText"/>。</summary>
        internal static TextMeshProUGUI CreateParagraphText(string name, Transform parent, string text, float fontSize,
            Color color)
        {
            TextMeshProUGUI paragraph = CreateBodyText(name, parent, text, fontSize, color);
            paragraph.alignment = TextAlignmentOptions.TopLeft;
            return paragraph;
        }

        /// <summary>
        /// 模态面板底色压到近不透明（owner 实测第 16 条复核 V16-1）：共享 Surface 的 α 0.92 在亮场景上能透出背后的地形，
        /// 字压在「花」背景上。只改 alpha，色相仍取 Surface token。入场页、弃局页、recap 共用。
        /// </summary>
        internal static void MakeSurfaceOpaque(GameObject surface)
        {
            Image image = surface != null ? surface.GetComponent<Image>() : null;
            if (image == null) return;
            Color color = BossRushUIColors.Surface;
            color.a = 0.98f;
            image.color = color;
        }

        /// <summary>
        /// 挂到父物体顶边中央、按内容量高（自动换行，不缩字、不省略）。返回高度，调用方据此推游标。
        /// </summary>
        internal static float PlaceText(TextMeshProUGUI text, float width, float x, float top)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            float height = BossRushUI.MeasureTextHeight(text, width, LineHeight(text.fontSize));
            rect.anchoredPosition = new Vector2(x, -top);
            return height;
        }

        private static float LineHeight(float fontSize)
        {
            return Mathf.Ceil(fontSize * LineHeightFactor) + 4f;
        }

        /// <summary>
        /// 两张契约卡左右并排、等高（取两张里说明较长的那张），错峰滑入。返回卡片高度。
        /// </summary>
        private float BuildContractCards(Transform parent, ModeGEntryPreview preview, float top)
        {
            float cardWidth = (ContentWidth - CardGap) * 0.5f;
            _cardButtons = new Button[2];
            _cardStrokes = new Image[2];
            _cardBadges = new GameObject[2];
            RectTransform[] cards = new RectTransform[2];
            float height = 0f;
            for (int i = 0; i < cards.Length; i++)
            {
                float cardHeight;
                cards[i] = BuildContractCard(parent, i,
                    ModeGFateContract.GetById(preview.contractCandidateIds[i]), cardWidth, out cardHeight);
                height = Mathf.Max(height, cardHeight);
            }
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].sizeDelta = new Vector2(cardWidth, height);
                cards[i].anchoredPosition = new Vector2((i == 0 ? -0.5f : 0.5f) * (cardWidth + CardGap), -top);
                BossRushUIEntranceAnimation.Play(cards[i].gameObject, CardStagger * (i + 1), CardEntrance, CardRise);
            }
            return height;
        }

        /// <summary>
        /// 一张契约卡：Card 档底图 + 描边（照 SkyIslandStoryPresentation.BuildChoice 的口径）。
        /// 名称左上、「已选」角标右上、说明在下。选中态由 <see cref="RefreshContractCards"/> 画：
        /// 描边换 WarningText、底色染一点 WarningText、角标出现（UI 制作共识第 6 节：选中统一金色描边）。navigation 设 None：点过之后 EventSystem 不再把它挂成
        /// 选中态，焦点移开时也不会出现一张「看起来还亮着」的卡。
        /// </summary>
        private RectTransform BuildContractCard(Transform parent, int index, ModeGFateContract.ContractDef def,
            float width, out float height)
        {
            GameObject card = ZombieModeUIHelper.CreateRect("Contract_" + index, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(width, LineHeight(20f)), new Vector2(0.5f, 1f));
            Image image = card.AddComponent<Image>();
            BossRushUI.ApplyPanelSkin(image, CardRadius, BossRushUISkinPart.Card);
            Image stroke = BossRushUI.ApplyPanelStroke(image, CardRadius, BossRushUISkinPart.Card,
                BossRushUIColors.Stroke);
            Button button = card.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ApplyCardColors(button, false);
            // 刚挂上的 Button 已把渲染色置白，赋完 colors 会从白渐变到卡底色；立即落到常态色，免得闪一下白底。
            image.CrossFadeColor(GetCardColor(false), 0f, true, true);
            int choice = index;
            button.onClick.AddListener(() => SelectCandidate(choice));

            float innerWidth = width - CardPadX * 2f;
            GameObject badge = BuildSelectedBadge(card.transform);
            float badgeWidth = badge.GetComponent<RectTransform>().sizeDelta.x;

            TextMeshProUGUI nameText = ZombieModeUIHelper.CreateText("Name", card.transform, def.GetDisplayName(),
                20f, Vector2.zero, new Vector2(innerWidth, LineHeight(20f)), TextAlignmentOptions.TopLeft,
                BossRushUIColors.TextPrimary);
            float nameHeight = PlaceCardText(nameText, innerWidth - badgeWidth - 8f, CardPadY);
            TextMeshProUGUI descText = ZombieModeUIHelper.CreateText("Desc", card.transform, def.GetDescription(),
                16f, Vector2.zero, new Vector2(innerWidth, LineHeight(16f)), TextAlignmentOptions.TopLeft,
                BossRushUIColors.TextSecondary);
            float descHeight = PlaceCardText(descText, innerWidth, CardPadY + nameHeight + 4f);
            height = CardPadY * 2f + nameHeight + 4f + descHeight;

            _cardButtons[index] = button;
            _cardStrokes[index] = stroke;
            _cardBadges[index] = badge;
            badge.SetActive(false);
            return card.GetComponent<RectTransform>();
        }

        private static float PlaceCardText(TextMeshProUGUI text, float width, float top)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            float height = BossRushUI.MeasureTextHeight(text, width, LineHeight(text.fontSize));
            rect.anchoredPosition = new Vector2(CardPadX, -top);
            return height;
        }

        /// <summary>「√ 已选」角标：WarningText 底 + 深字（GetButtonTextColor），单行不换行、不省略，宽度按字量。</summary>
        private static GameObject BuildSelectedBadge(Transform card)
        {
            string text = L10n.T("√ 已选", "√ Selected");
            // 与名称首行垂直居中：名称一行高 20×1.45，角标高 BadgeHeight。
            float top = CardPadY + (20f * LineHeightFactor - BadgeHeight) * 0.5f;
            GameObject badge = ZombieModeUIHelper.CreateRect("SelectedBadge", card,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-CardPadX, -top),
                new Vector2(80f, BadgeHeight), new Vector2(1f, 1f));
            Image fill = badge.AddComponent<Image>();
            fill.color = BossRushUIColors.WarningText;
            fill.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(fill, 8, BossRushUISkinPart.Button);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Label", badge.transform, text, 14f,
                Vector2.zero, new Vector2(80f, BadgeHeight), TextAlignmentOptions.Center,
                BossRushUI.GetButtonTextColor(BossRushUIColors.WarningText));
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            float width = Mathf.Ceil(label.GetPreferredValues(text).x) + 16f;
            badge.GetComponent<RectTransform>().sizeDelta = new Vector2(width, BadgeHeight);
            return badge;
        }

        private static Color GetCardColor(bool selected)
        {
            Color raised = BossRushUIColors.SurfaceRaised;
            if (!selected) return raised;
            Color tinted = Color.Lerp(raised, BossRushUIColors.WarningText, CardSelectedTint);
            tinted.a = raised.a;
            return tinted;
        }

        private static void ApplyCardColors(Button button, bool selected)
        {
            if (button == null) return;
            Color normal = GetCardColor(selected);
            Color hover = Color.Lerp(normal, Color.white, CardHoverLift);
            hover.a = normal.a;
            ZombieModeUIHelper.ApplyButtonColors(button, normal, hover, BossRushUI.GetDisabledColor(normal));
        }

        private void RefreshContractCards()
        {
            if (_cardButtons == null) return;
            for (int i = 0; i < _cardButtons.Length; i++)
            {
                bool selected = i == _selectedCandidateIndex;
                ApplyCardColors(_cardButtons[i], selected);
                if (_cardStrokes != null && i < _cardStrokes.Length && _cardStrokes[i] != null)
                    _cardStrokes[i].color = selected ? BossRushUIColors.WarningText : BossRushUIColors.Stroke;
                if (_cardBadges != null && i < _cardBadges.Length && _cardBadges[i] != null)
                    _cardBadges[i].SetActive(selected);
            }
        }

        #endregion

        private void SelectCandidate(int index)
        {
            if (_modalPreview == null || index < 0
                || _modalPreview.contractCandidateIds == null
                || index >= _modalPreview.contractCandidateIds.Length)
            {
                return;
            }
            try
            {
                _selectedCandidateIndex = index;
                RefreshContractCards();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 契约选择异常: " + e.Message);
            }
        }

        /// <summary>
        /// 确认：记录所选契约后唯一调用 TryStartModeG()。
        /// </summary>
        private void ConfirmAndStart(ModBehaviour host)
        {
            try
            {
                if (_modalPreview == null || _selectedCandidateIndex < 0
                    || _selectedCandidateIndex >= _modalPreview.contractCandidateIds.Length)
                {
                    host.ShowMessage(L10n.T("先挑一个宿命契约。", "Pick a Fate Contract first."));
                    return;
                }
                if (_modalPreview != null && _modalPreview.contractCandidateIds != null
                    && _selectedCandidateIndex >= 0
                    && _selectedCandidateIndex < _modalPreview.contractCandidateIds.Length)
                {
                    host.SetModeGSelectedContractId(_modalPreview.contractCandidateIds[_selectedCandidateIndex]);
                }
                _confirmed = true;
                LastConfirmationAttemptedStart = true;
                CloseModal();
                bool started = host.TryStartModeG();
                if (!started)
                {
                    host.TryRefundModeGPendingPrepaidTicket();
                }
                else
                {
                    BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ConfirmAndStart 异常: " + e.Message);
                CloseModal();
            }
        }

        private void CloseModal()
        {
            bool wasConfirmed = _confirmed;
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { }
            try
            {
                if (_cancelKey != null) _cancelKey.Detach();
            }
            catch { /* 根已随场景销毁：订阅由 ModeGModalCancelKey.OnDestroy 退掉 */ }
            _cancelKey = null;
            try
            {
                // 淡出后销毁（UB-32）：输入租约已在上面先还掉，引用下面立刻置空，重开会新建根，动画不变成输入延迟。
                if (_modalRoot != null) BossRushUIKit.PlayCloseAndDestroy(_modalRoot);
            }
            catch { }
            _modalRoot = null;
            _modalPreview = null;
            ModBehaviour entryHost = _entryHost;
            _entryHost = null;
            _cardButtons = null;
            _cardStrokes = null;
            _cardBadges = null;
            if (!wasConfirmed)
            {
                // Map selection may already have charged the ticket; direct teleport has no
                // prepaid ownership and therefore remains a no-op here.
                try
                {
                    if (entryHost != null) entryHost.TryRefundModeGPendingPrepaidTicket();
                }
                catch (Exception refundException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 关闭确认页退回预扣船票失败: "
                        + refundException.Message);
                }
            }
            if (ReferenceEquals(_activeConfirmation, this)) _activeConfirmation = null;
            if (_autoPresenter)
            {
                _autoPresenter = false;
                try { UnityEngine.Object.Destroy(gameObject); }
                catch (Exception destroyException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 自动确认页销毁失败: "
                        + destroyException.Message);
                }
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            // 清理顺序不变：先关自己的确认页（释放输入租约 + 退回预扣船票），
            // 再交回基类。官方 InteractableBase.OnDestroy 是 protected virtual
            // （Interacting 时 StopInteract），此前用 new 隐藏它，一旦本组件挂到
            // 带碰撞体的实体上，销毁时官方交互就停不下来。
            try { CloseModal(); } catch { /* no-throw */ }
            try { base.OnDestroy(); } catch { /* no-throw */ }
        }
    }

    /// <summary>
    /// Mode G 局内「放弃挑战」确认页（短命 presenter，形态复刻入场确认页的 auto-presenter）。
    ///
    /// 契约：
    /// - 只由 ModeGEntry 的快捷键轮询创建，run 进行中场内没有可交互实体（IsModeGEntryBlocked
    ///   会挡掉 ModeGInteractable），因此不做成 InteractableBase；
    /// - 与入场确认页共用 ClaimModalInput 的时停语义，同一时刻只允许一个实例；
    /// - 确认分支唯一调用 module.End(ModeGExitReason.ManualExit)，连胜清零由
    ///   ModeGCleanupController 的既有 ManualExit 分支消费，本类不碰任何存档；
    /// - 放弃不退还船票与信物（既定规则），页面必须强制披露。
    /// </summary>
    internal sealed class ModeGAbandonPresenter : MonoBehaviour
    {
        private const float ModalWidth = 720f;
        /// <summary>建面板时的初始高度；排版完成后按内容收口（口径同入场确认页）。</summary>
        private const float ModalInitialHeight = 320f;
        private const float PadX = 36f;
        private const float ContentWidth = ModalWidth - PadX * 2f;
        private const float TopPad = 28f;
        private const float EmblemSize = 48f;
        private const float ButtonWidth = 240f;
        private const float ButtonHeight = 52f;
        /// <summary>危险键与取消键拉开的间距（UI 制作共识第 4 节「破坏性按钮与安全按钮拉开距离」，同共享确认框的 40）。</summary>
        private const float ButtonGap = 40f;
        private const float BottomPad = 24f;

        private static ModeGAbandonPresenter _active;

        private GameObject _modalRoot;
        private ZombieModeUIHelper.ModalInputLease _inputLease;
        private ModeGModalCancelKey _cancelKey;
        private ModeGRuntimeModule _module;

        /// <summary>确认页是否已打开（轮询侧防重入）。</summary>
        internal static bool IsOpen
        {
            get { return _active != null && _active._modalRoot != null; }
        }

        /// <summary>模式结束、切图和宿主销毁共用的幂等关闭入口。</summary>
        internal static void CloseIfOpen()
        {
            try
            {
                if (_active != null) _active.Close();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页清理失败: " + e.Message);
            }
        }

        /// <summary>打开弃局确认页；已有实例或缺少运行中的 run 时返回 false。</summary>
        internal static bool TryOpen(ModeGRuntimeModule module)
        {
            if (module == null || _active != null) return false;
            GameObject host = null;
            try
            {
                ModeGRunState state = module.State;
                if (state == null || !state.IsActive) return false;

                host = new GameObject("ModeG_AbandonConfirmPresenter");
                UnityEngine.Object.DontDestroyOnLoad(host);
                ModeGAbandonPresenter presenter = host.AddComponent<ModeGAbandonPresenter>();
                presenter._module = module;
                _active = presenter;
                return presenter.OpenPage();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页创建失败: " + e.Message);
                _active = null;
                try { if (host != null) UnityEngine.Object.Destroy(host); }
                catch { /* 宿主已被销毁：清理路径不再二次报错 */ }
                return false;
            }
        }

        private bool OpenPage()
        {
            GameObject root = BossRushUI.CreateCanvasRoot("ModeG_AbandonPage", BossRushUILayers.ModeGEntry, true).gameObject;
            UnityEngine.Object.DontDestroyOnLoad(root);
            _modalRoot = root;

            // 与入场确认页同一套样式（2026-09-23 审美审查 UB-18 / owner 实测第 16 条复核 V16-3）：
            // 近不透明底、徽记 + 标题一行、从顶边往下按内容排、分隔线、危险「确认放弃」+ 次级「继续战斗」；
            // 打开淡入、关闭淡出、ESC 等于「继续战斗」。旧版是一对平涂绿 / 红按钮，面板一帧弹出。
            // 按钮左右照确认弹窗页型（UI 共识对照审查 B-23）：危险键在左、取消在右、中间拉开 40。
            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeG_Abandon", root.transform, new Vector2(ModalWidth, ModalInitialHeight),
                BossRushUIColors.DangerText);
            ModeGInteractable.MakeSurfaceOpaque(surface);
            Transform st = surface.transform;

            float cursor = TopPad;
            cursor += ModeGInteractable.PlaceTitleRow(st, ModeGInteractable.CreateEmblem(st, EmblemSize), cursor,
                L10n.T("放弃宿命回响挑战？", "Abandon the Fate Echo run?"), 28f, BossRushUIColors.TextPrimary,
                ContentWidth, EmblemSize) + 14f;

            // §3.1 强制披露：放弃的全部代价一次讲清，不得只写「确认放弃」
            cursor += ModeGInteractable.PlaceText(ModeGInteractable.CreateParagraphText("Disclosure", st,
                L10n.T(
                    "放弃后本局进度作废：已消耗的船票与信物不返还，契约连胜清零。",
                    "Abandoning voids this run: the ticket and relic are not refunded, "
                    + "and your contract win streak resets."),
                16f, BossRushUIColors.TextSecondary), ContentWidth, 0f, cursor) + 12f;

            GameObject divider = ZombieModeUIHelper.CreateSeparator("Divider", st,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(cursor + 4f)), 1f, BossRushUIColors.Divider);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.sizeDelta = new Vector2(-PadX * 2f, dividerRect.sizeDelta.y);
            cursor += 22f;

            float buttonY = -(cursor + ButtonHeight * 0.5f);
            float buttonX = (ButtonWidth + ButtonGap) * 0.5f;
            ZombieModeUIHelper.CreateButton(
                "Abandon", st, L10n.T("确认放弃", "Abandon Run"),
                new Vector2(0.5f, 1f), new Vector2(-buttonX, buttonY), new Vector2(ButtonWidth, ButtonHeight),
                BossRushUIColors.Danger, 20f, new Vector2(ButtonWidth - 24f, ButtonHeight - 12f),
                ConfirmAbandon, true);

            Button keepButton = ZombieModeUIHelper.CreateButton(
                "Keep", st, L10n.T("继续战斗", "Keep Fighting"),
                new Vector2(0.5f, 1f), new Vector2(buttonX, buttonY), new Vector2(ButtonWidth, ButtonHeight),
                BossRushUIColors.SurfaceRaised, 20f, new Vector2(ButtonWidth - 24f, ButtonHeight - 12f),
                Close, true);
            BossRushUIKit.StyleSecondaryButton(keepButton);
            cursor += ButtonHeight + BottomPad;

            surface.GetComponent<RectTransform>().sizeDelta = new Vector2(ModalWidth, Mathf.Ceil(cursor));
            BossRushUI.PlayOpenAnimation(surface);
            _cancelKey = ModeGModalCancelKey.Attach(root, Close);

            try
            {
                _inputLease = ZombieModeUIHelper.ClaimModalInput(root, "ModeGAbandonPage");
                if (_inputLease == null || !ZombieModeUIHelper.IsModalInputPaused)
                {
                    throw new InvalidOperationException("Mode G abandon page could not acquire modal pause");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页输入占用失败: " + e.Message);
                Close();
                return false;
            }
            return true;
        }

        private void ConfirmAbandon()
        {
            ModeGRuntimeModule module = _module;
            Close();
            try
            {
                // End 幂等：终局横幅、连胜清零与关停由 End -> Cleanup -> UpdateModeG 的既有链承接
                if (module != null) module.End(ModeGExitReason.ManualExit);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] 放弃挑战终局失败: " + e.Message);
            }
        }

        private void Close()
        {
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { /* 租约已被宿主回收：继续走完销毁，不得中断 */ }
            try { if (_cancelKey != null) _cancelKey.Detach(); }
            catch { /* 根已销毁：订阅随 OnDestroy 退掉 */ }
            _cancelKey = null;
            // 淡出后销毁（UB-32）：租约已先还掉、引用立刻置空，淡出中的根不再吃点击也不再吃 ESC。
            try { if (_modalRoot != null) BossRushUIKit.PlayCloseAndDestroy(_modalRoot); }
            catch { /* 面板已随场景销毁：置空即可 */ }
            _modalRoot = null;
            _module = null;
            if (ReferenceEquals(_active, this)) _active = null;
            try { UnityEngine.Object.Destroy(gameObject); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页销毁失败: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            // 与 Close 同款兜底：presenter 被外部销毁时也必须还掉输入租约，否则时停不解除
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { /* 租约已被宿主回收 */ }
            try { if (_modalRoot != null) UnityEngine.Object.Destroy(_modalRoot); }
            catch { /* 面板已随场景销毁 */ }
            _modalRoot = null;
            _module = null;
            if (ReferenceEquals(_active, this)) _active = null;
        }
    }

    /// <summary>
    /// Mode G 模态页的 ESC / 手柄取消（2026-09-23 审美审查 UB-18 / UB-19）。
    ///
    /// 订官方 <c>UIInputManager.OnCancelEarly</c>，关页并 <c>Use()</c> 掉这次事件：官方
    /// <c>UIInputManager.OnInputActionCancel</c> 在没人用掉事件、又没有官方 View 时会 <c>PauseMenu.Toggle()</c>——
    /// 不用掉的话同一下 ESC 在关掉确认页之后接着打开暂停菜单。模态租约的 <c>InputManager.DisableInput</c>
    /// 只挡角色输入，挡不住 UI 取消键，所以旧版在确认页上按 ESC 是直接开暂停菜单。
    ///
    /// 挂在模态根上；订阅幂等（私有布尔），关页开始淡出时调用方先 <see cref="Detach"/>，根销毁时 OnDestroy 兜底退订（AGENTS §4.6）。
    /// 只收事件、没有 Update。
    /// </summary>
    internal sealed class ModeGModalCancelKey : MonoBehaviour
    {
        private Action _onCancel;
        private bool _subscribed;

        internal static ModeGModalCancelKey Attach(GameObject root, Action onCancel)
        {
            if (root == null || onCancel == null) return null;
            ModeGModalCancelKey key = root.GetComponent<ModeGModalCancelKey>();
            if (key == null) key = root.AddComponent<ModeGModalCancelKey>();
            key._onCancel = onCancel;
            key.Subscribe();
            return key;
        }

        /// <summary>关页时调用：不再响应取消键（淡出中的根还会活 0.12 秒）。</summary>
        internal void Detach()
        {
            _onCancel = null;
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            try
            {
                global::UIInputManager.OnCancelEarly += HandleCancel;
                _subscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 模态页订阅取消键失败（只能点按钮关闭）: " + e.Message);
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            try { global::UIInputManager.OnCancelEarly -= HandleCancel; }
            catch (Exception e) { ModBehaviour.DevLog("[ModeG] [WARNING] 模态页退订取消键失败: " + e.Message); }
        }

        private void HandleCancel(global::UIInputEventData data)
        {
            Action onCancel = _onCancel;
            if (onCancel == null || !isActiveAndEnabled) return;
            // 正在淡出的页（已关、根还活 0.12 秒）不再吃 ESC：让这一下照常落到下一层（例如新开的页或暂停菜单）。
            BossRushUICloseAnimation closing = GetComponent<BossRushUICloseAnimation>();
            if (closing != null && closing.enabled) return;
            if (data != null) data.Use();
            Detach();
            try { onCancel(); }
            catch (Exception e) { ModBehaviour.DevLog("[ModeG] [WARNING] 模态页取消键处理失败: " + e.Message); }
        }

        private void OnDestroy()
        {
            _onCancel = null;
            Unsubscribe();
        }
    }
}
