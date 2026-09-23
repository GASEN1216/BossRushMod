// ============================================================================
// PetNestExpeditionRevealView.cs - 远征结算翻牌演出（实施计划 步骤 11）
// ============================================================================
// 借官方 DeathLottery 的翻牌节奏语言，自建实现（只借 UI 语言，不碰它的抽卡逻辑）。
//
// 硬约束（tests/PetNestRevealIdempotencyGuard.py 守卫）：
//   - **只回放已 settled 的结果**：本文件不得出现任何 roll 符号与写档符号；
//   - 唯一允许的服务层写调用是 MarkRevealed——那是"翻完牌把记录移出待翻列表"，
//     不是结算；
//   - 演出中断不影响结果：结果与 settled 标记在结算时就已落档，
//     下次回基地会重新弹出未翻的牌。
//
// 2026-09-23 审美审查 UA-11 / UA-13：真的有一张牌——背面是目的地与档位色条，
// 翻面是 localScale.x 1→0 换面再 0→1（SmoothStep、暂停感知，不用随机数）；
// 正面是崽的立绘 + 装饰名 + 结果色条（平安 SuccessText / 负伤 WarningText / 阵亡 DangerText，阵亡整卡压暗）；
// 战利品是物品图标 + 名字；翻完最后一张**不自动收**，「跳过」变「关闭」，等玩家自己点（与孵化揭晓同口径）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>远征结算翻牌演出。逐张翻，纯回放。</summary>
    internal sealed class PetNestExpeditionRevealView : MonoBehaviour
    {
        #region 节奏（时长草案）

        private const float CardEnterSeconds = 0.3f;
        private const float CardFlipSeconds = 0.55f;
        private const float CardHoldSeconds = 1.4f;

        /// <summary>翻牌卡上最多点名的战利品件数，多出来的并成「+N」。</summary>
        private const int MaxLootNamesShown = 3;

        #endregion

        #region 版式

        private static readonly Vector2 SurfaceSize = new Vector2(820f, 520f);
        private static readonly Vector2 CardSize = new Vector2(380f, 200f);
        private const float CardCenterY = 80f;
        private const float PortraitSize = 128f;
        private const float LootIconSize = 48f;
        private const float LootTileWidth = 140f;
        /// <summary>阵亡的那张牌整卡压暗。</summary>
        private const float DeadCardAlpha = 0.7f;

        #endregion

        private static PetNestExpeditionRevealView _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private TextMeshProUGUI _titleText;
        private GameObject _card;
        private CanvasGroup _cardGroup;
        private Image _cardStroke;
        private GameObject _backFace;
        private GameObject _frontFace;
        private TextMeshProUGUI _backTitle;
        private TextMeshProUGUI _backTier;
        private Image _backBar;
        private TextMeshProUGUI _detailText;
        private GameObject _lootRow;
        private Button _skipButton;
        private TextMeshProUGUI _skipLabel;
        private List<PetNestExpeditionRecord> _pending;
        private bool _finished;

        /// <summary>演出是否在播（ESC 优先级判定用）。</summary>
        internal static bool IsOpen
        {
            get { return _instance != null; }
        }

        /// <summary>
        /// 把所有已结算未翻牌的远征逐张翻出来。没有待翻记录时直接返回。
        /// </summary>
        internal static void PlayPending()
        {
            try
            {
                List<PetNestExpeditionRecord> pending = PetNestExpeditionService.GetPendingReveals();
                if (pending == null || pending.Count == 0) return;

                Stop();
                GameObject host = new GameObject("BossRush_PetNestExpeditionReveal");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestExpeditionRevealView>();
                _instance._pending = pending;
                _instance.Build();
                _instance.StartCoroutine(_instance.PlayRoutine());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征翻牌启动失败: " + e.Message);
                Stop();
            }
        }

        /// <summary>中断并销毁演出。幂等；未翻的牌下次回基地会重新弹。</summary>
        internal static void Stop()
        {
            try
            {
                if (_instance == null) return;
                _instance.ReleaseLease();
                if (_instance._cancelKey != null) _instance._cancelKey.Detach();
                if (_instance.gameObject != null)
                {
                    UnityEngine.Object.Destroy(_instance.gameObject);
                }
            }
            catch (Exception)
            {
                // 销毁失败只丢引用
            }
            finally
            {
                _instance = null;
            }
        }

        /// <summary>玩家点「关闭 / 跳过」或按 ESC 收场：画布淡出 0.12 秒（UA-21）。切图等被动路径走 Stop()。</summary>
        private void CloseByPlayer()
        {
            if (_instance != this) return;
            ReleaseLease();
            if (_cancelKey != null) _cancelKey.Detach();
            _instance = null;
            StopAllCoroutines();
            PetNestUI.FadeOutAndDestroy(gameObject, _canvas);
        }

        private void OnDestroy()
        {
            ReleaseLease();
            if (_instance == this) _instance = null;
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

        private void Build()
        {
            _canvas = BossRushUI.CreateCanvasRoot(
                "BossRush_PetNestExpeditionRevealCanvas", BossRushUILayers.PetNestModal, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), SurfaceSize);
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(image, 14, BossRushUISkinPart.Panel);

            _titleText = ZombieModeUIHelper.CreateText(
                "Title", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Page_Expedition"),
                30f, new Vector2(0f, 212f), new Vector2(760f, 46f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            _titleText.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(_titleText);

            BuildCard(surface.transform);

            _detailText = ZombieModeUIHelper.CreateText(
                "Detail", surface.transform, string.Empty, 18f,
                new Vector2(0f, -54f), new Vector2(760f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_detailText);

            _lootRow = ZombieModeUIHelper.CreateRect(
                "LootRow", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(760f, LootIconSize + 28f));
            _lootRow.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -130f);
            HorizontalLayoutGroup lootLayout = _lootRow.AddComponent<HorizontalLayoutGroup>();
            lootLayout.childAlignment = TextAnchor.MiddleCenter;
            lootLayout.spacing = 16f;
            lootLayout.childControlWidth = false;
            lootLayout.childControlHeight = false;
            lootLayout.childForceExpandWidth = false;
            lootLayout.childForceExpandHeight = false;

            // 跳过：一次派 6 只崽就是 13.5 秒全屏遮罩，没有跳过键是不可接受的。
            // 翻完最后一张之后它变成「关闭」（主操作 AccentFill），等玩家看完自己收（UA-13）。
            _skipButton = ZombieModeUIHelper.CreateButton(
                "Skip", surface.transform, L10n.T("跳过", "Skip"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -SurfaceSize.y * 0.5f + 42f), new Vector2(170f, 44f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(160f, 40f),
                delegate { OnSkipOrClose(); }, true);
            if (_skipButton != null)
            {
                BossRushUIKit.StyleSecondaryButton(_skipButton);
                _skipLabel = _skipButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            // 接管输入：遮罩盖住画面却不拦输入的话，玩家会在看不见的情况下
            // 摸到交互点、打开面板、走进战斗
            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestExpeditionReveal");
            // ESC / 手柄取消 = 与按钮同一个入口（播放中跳过、翻完后关闭）
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, OnSkipOrClose, null);

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>牌：背面（目的地 + 档位色条）与正面（立绘 + 名字 + 结果色条）两层，翻面时互换显隐。</summary>
        private void BuildCard(Transform parent)
        {
            _card = BossRushUI.CreateCard(
                "FlipCard", parent, new Vector2(0f, CardCenterY), CardSize,
                BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, false);
            _cardGroup = _card.AddComponent<CanvasGroup>();
            Transform stroke = _card.transform.Find("Stroke");
            _cardStroke = stroke != null ? stroke.GetComponent<Image>() : null;

            _backFace = ZombieModeUIHelper.CreateRect(
                "Back", _card.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _backTitle = ZombieModeUIHelper.CreateText(
                "BackTitle", _backFace.transform, string.Empty, 26f,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 22f), new Vector2(-32f, 40f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            _backTitle.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(_backTitle);
            _backTier = ZombieModeUIHelper.CreateText(
                "BackTier", _backFace.transform, string.Empty, 18f,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -20f), new Vector2(-32f, 30f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_backTier);
            _backBar = CreateBar(_backFace.transform, "BackBar");

            _frontFace = ZombieModeUIHelper.CreateRect(
                "Front", _card.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _frontFace.SetActive(false);
        }

        /// <summary>牌底边的一条色条（档位 / 结果色）。</summary>
        private static Image CreateBar(Transform parent, string name)
        {
            GameObject bar = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 14f), new Vector2(-40f, 4f), new Vector2(0.5f, 0.5f));
            Image image = bar.AddComponent<Image>();
            image.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(image, 2, BossRushUISkinPart.Hairline);
            return image;
        }

        /// <summary>
        /// 播放中 = 跳过：把还没翻的记录一次性标记为已翻，然后收工。结果早已落档，跳过只是不看动画。
        /// 翻完之后 = 关闭。
        /// </summary>
        private void OnSkipOrClose()
        {
            if (!_finished)
            {
                SkipAll();
                return;
            }
            CloseByPlayer();
        }

        private void SkipAll()
        {
            try
            {
                if (_pending != null)
                {
                    for (int i = 0; i < _pending.Count; i++)
                    {
                        if (_pending[i] == null) continue;
                        string reason;
                        PetNestExpeditionService.MarkRevealed(_pending[i], out reason);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 跳过翻牌失败: " + e.Message);
            }
            CloseByPlayer();
        }

        private IEnumerator PlayRoutine()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                PetNestExpeditionRecord record = _pending[i];
                if (record == null) continue;

                // 背面：目的地 + 档位色条
                ShowBack(record);
                SetText(_detailText, string.Empty);
                ClearLoot();
                yield return WaitForPresentation(CardEnterSeconds);

                // 翻面：宽度收到 0 → 换成正面 → 再展开
                yield return Flip(record);

                SetText(_detailText, BuildCardDetail(record));
                BuildLoot(record);
                yield return WaitForPresentation(CardHoldSeconds);

                // 翻完这张才把它移出待翻列表；中途退出的话下次回基地会重新弹
                string reason;
                PetNestExpeditionService.MarkRevealed(record, out reason);
            }

            // 最后一张**不自动收**（UA-13）：阵亡结果一闪就没了是最糟的体验。「跳过」变「关闭」，等玩家自己点。
            _finished = true;
            if (_skipLabel != null) _skipLabel.text = L10n.T("关闭", "Close");
            if (_skipButton != null) ZombieModeUIHelper.SetButtonBaseColor(_skipButton, BossRushUIColors.AccentFill);
        }

        /// <summary>与孵化演出同样使用暂停感知的表现时间，暂停不能消耗待翻卡片。</summary>
        private static IEnumerator WaitForPresentation(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                if (!BossRushUI.IsGamePaused()) elapsed += Time.unscaledDeltaTime;
            }
        }

        /// <summary>翻牌：前半程宽度 1→0，换成正面，后半程 0→1。SmoothStep、unscaled、暂停时不推进。</summary>
        private IEnumerator Flip(PetNestExpeditionRecord record)
        {
            float half = CardFlipSeconds * 0.5f;
            float elapsed = 0f;
            while (elapsed < half)
            {
                yield return null;
                if (!BossRushUI.IsGamePaused()) elapsed += Time.unscaledDeltaTime;
                SetCardWidthScale(1f - BossRushUI.SmoothStep(elapsed / half));
            }
            ShowFront(record);
            elapsed = 0f;
            while (elapsed < half)
            {
                yield return null;
                if (!BossRushUI.IsGamePaused()) elapsed += Time.unscaledDeltaTime;
                SetCardWidthScale(BossRushUI.SmoothStep(elapsed / half));
            }
            SetCardWidthScale(1f);
        }

        private void SetCardWidthScale(float x)
        {
            if (_card == null) return;
            _card.transform.localScale = new Vector3(Mathf.Clamp01(x), 1f, 1f);
        }

        private void ShowBack(PetNestExpeditionRecord record)
        {
            if (_card == null) return;
            _backFace.SetActive(true);
            _frontFace.SetActive(false);
            if (_cardGroup != null) _cardGroup.alpha = 1f;
            if (_cardStroke != null) _cardStroke.color = BossRushUIColors.Stroke;
            Color tier = TierColor(record.riskTier);
            SetText(_backTitle, PetNestLocalization.DescribeDestination(record.destinationId));
            SetText(_backTier, PetNestLocalization.DescribeRisk(record.riskTier)
                + " · " + LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "DeathRateLabel")
                + " " + PetNestLocalization.FormatPercent(record.deathRate));
            if (_backTier != null) _backTier.color = tier;
            if (_backBar != null) _backBar.color = tier;
            SetCardWidthScale(1f);
        }

        /// <summary>正面：崽的立绘（取不到就不画）+ 装饰名 + 结果字与结果色条；阵亡整卡压暗。</summary>
        private void ShowFront(PetNestExpeditionRecord record)
        {
            _backFace.SetActive(false);
            for (int i = _frontFace.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_frontFace.transform.GetChild(i).gameObject);
            }
            _frontFace.SetActive(true);

            Color outcome = OutcomeColor(record);
            float textLeft = 24f;
            Sprite portrait = PetNestUIPages.ResolveLineagePortrait(record.petLineageKey);
            if (portrait != null)
            {
                PetNestUI.CreateIconFrame(_frontFace.transform, "Portrait", portrait,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 6f),
                    PortraitSize, record.petShiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke, false);
                textLeft = 20f + PortraitSize + 16f;
            }

            float textWidth = CardSize.x - textLeft - 20f;
            TextMeshProUGUI name = ZombieModeUIHelper.CreateText(
                "FrontName", _frontFace.transform, BuildCardTitle(record), 24f,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(textLeft + textWidth * 0.5f, 28f), new Vector2(textWidth, 40f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            name.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(name);

            TextMeshProUGUI result = ZombieModeUIHelper.CreateText(
                "FrontOutcome", _frontFace.transform, DescribeOutcome(record), 20f,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(textLeft + textWidth * 0.5f, -14f), new Vector2(textWidth, 32f),
                TextAlignmentOptions.Left, outcome);
            BossRushUI.ApplyGameFont(result);

            Image bar = CreateBar(_frontFace.transform, "OutcomeBar");
            bar.color = outcome;
            if (_cardStroke != null) _cardStroke.color = record.outcomeDead ? BossRushUIColors.DangerText : BossRushUIColors.Stroke;
            if (_cardGroup != null) _cardGroup.alpha = record.outcomeDead ? DeadCardAlpha : 1f;
        }

        private static Color TierColor(int riskTier)
        {
            if (riskTier == (int)PetNestRiskTier.Desperate) return BossRushUIColors.DangerText;
            if (riskTier == (int)PetNestRiskTier.Rough) return BossRushUIColors.WarningText;
            return BossRushUIColors.Accent;
        }

        private static Color OutcomeColor(PetNestExpeditionRecord record)
        {
            if (record.outcomeDead) return BossRushUIColors.DangerText;
            if (record.outcomeInjured) return BossRushUIColors.WarningText;
            return BossRushUIColors.SuccessText;
        }

        private static string BuildCardTitle(PetNestExpeditionRecord record)
        {
            // 装饰名：黑边卡（没回来）恰恰是 PetRecord 已被移除的那一档，
            // 只有记录里固化的颜色能让异色 / 炫彩在最后一屏仍然显示出来。
            return PetNestExpeditionService.DescribeDecoratedPetName(record);
        }

        /// <summary>
        /// 结果字。阵亡用 DangerText（UA-13：旧写法 #7A3434 暗红压在深色面板上只有约 2.2:1，36 号大字也看不清）。
        /// 颜色由正面的结果字控件承担，这里只给文案。
        /// </summary>
        private static string DescribeOutcome(PetNestExpeditionRecord record)
        {
            if (record.outcomeDead) return L10n.T("没有回来", "Never came back");
            if (record.outcomeInjured) return L10n.T("负伤归来", "Returned wounded");
            return L10n.T("平安归来", "Returned safely");
        }

        private static string BuildCardDetail(PetNestExpeditionRecord record)
        {
            // 目的地 / 档位 / 死亡率的文案口径与面板共用 PetNestLocalization 的单点入口，
            // 此前这里另写了一份 switch 与一份取整，加档位时必然漏改一处。
            string text = PetNestLocalization.DescribeDestination(record.destinationId)
                + " · " + PetNestLocalization.DescribeRisk(record.riskTier)
                + " · " + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "DeathRateLabel")
                + " " + PetNestLocalization.FormatPercent(record.deathRate);

            if (record.outcomeCash > 0L)
            {
                text += "\n" + L10n.T("现金", "Cash") + " +" + record.outcomeCash;
            }
            if (record.outcomeDead)
            {
                text += "\n" + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "Page_Memorial");
            }
            return text;
        }

        private void ClearLoot()
        {
            if (_lootRow == null) return;
            for (int i = _lootRow.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_lootRow.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// 战利品：最多 MaxLootNamesShown 格「物品图标 + 名字 ×N」，其余并成「+N」（UA-10 / UA-13）。
        /// 图标取不到的格只写名字；名字也查不到（资源未就绪）时整行回落成「战利品 ×件数」，绝不空着。
        /// </summary>
        private void BuildLoot(PetNestExpeditionRecord record)
        {
            ClearLoot();
            if (_lootRow == null || record.outcomeLootTypeIds == null || record.outcomeLootTypeIds.Count == 0) return;

            int shown = 0;
            int hidden = 0;
            for (int i = 0; i < record.outcomeLootTypeIds.Count; i++)
            {
                if (shown >= MaxLootNamesShown)
                {
                    hidden++;
                    continue;
                }
                int typeId = record.outcomeLootTypeIds[i];
                int count = i < record.outcomeLootCounts.Count ? record.outcomeLootCounts[i] : 1;
                string name = ResolveItemName(typeId);
                if (string.IsNullOrEmpty(name)) continue;
                CreateLootTile(typeId, count > 1 ? name + " ×" + count : name);
                shown++;
            }

            if (shown == 0)
            {
                CreateLootCaption(L10n.T("战利品", "Loot") + " ×" + record.outcomeLootTypeIds.Count);
                return;
            }
            if (hidden > 0) CreateLootCaption("+" + hidden);
        }

        private void CreateLootTile(int typeId, string caption)
        {
            GameObject tile = ZombieModeUIHelper.CreateRect(
                "LootTile", _lootRow.transform, new Vector2(0.5f, 0.5f), new Vector2(LootTileWidth, LootIconSize + 28f));
            Sprite icon = PetNestUIPages.ResolveItemIcon(typeId);
            if (icon != null)
            {
                PetNestUI.CreateIconFrame(tile.transform, "Icon", icon,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                    LootIconSize, BossRushUIColors.Stroke, false);
            }
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Caption", tile.transform, caption, 14f,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 13f), new Vector2(0f, 26f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(label);
        }

        private void CreateLootCaption(string text)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "LootMore", _lootRow.transform, text, 16f,
                Vector2.zero, new Vector2(120f, LootIconSize + 28f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(label);
        }

        /// <summary>单件战利品的显示名。查不到返回 null 由调用方回落。</summary>
        private static string ResolveItemName(int typeId)
        {
            if (typeId == RelicEggConfig.TYPE_ID) return RelicEggConfig.GetDisplayName();
            try
            {
                ItemStatsSystem.Item prefab = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId);
                string name = prefab != null ? prefab.DisplayName : null;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception)
            {
                // 物品表未就绪：交由调用方回落成件数
                return null;
            }
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target != null) target.text = value;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Stop();
        }
    }
}
