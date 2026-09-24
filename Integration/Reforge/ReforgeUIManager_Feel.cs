// ============================================================================
// ReforgeUIManager_Feel.cs - 重铸 / 词缀锻造界面的反馈层（ReforgeUIManager 的 partial 续）
// ============================================================================
// 模块说明：
//   2026-09-23 审美审查 D 区 UD-23…UD-30。重铸寄生在官方分解界面上，官方那一侧是克制的深灰底、
//   白色数值；我们塞进去的部分原先是七行算式配霓虹色富文本、点一下属性行就扣冷淬液、结果一帧换完。
//   这里放「让它像官方界面的一部分」所需的表现逻辑：
//     - UD-23 费用区：只留两行（重铸幅度、总计花费），颜色全走 token（IntegrationUIFeedback.*Hex）；
//       钱不够只染「总计」那一行并写「还差 X」，不再把算式原样写给玩家看。
//       2026-09-24 UI 共识对照审查 A-01…A-03：按钮写价钱（ApplyReforgeButtonLabel）、系数等次信息删掉、
//       涨跌倾向的概率与额外费用挪到滑块旁的白话标签（RefreshTendencyLabel）。
//     - UD-24 属性行固定：两步确认（第一次点进入待确认：行底 Accent 淡色 + 「再点一次固定」，2 秒内再点才扣），
//       常态左侧一道 Accent 细条表示能点，冷淬液计数下方一行说明；成功播 UI/confirm、行底金色闪一下。
//     - UD-25 重铸揭晓：变化的行按顺序闪一下（涨 SuccessText、跌 DangerText、触顶 RarityLegendary + UI/pop），
//       数值从 1.15 倍 EaseOut 回落。
//     - UD-29 词缀揭晓：新词缀行依次淡入，描边短暂换成稀有度色后回到 Stroke，抽到稀有播 UI/level_up。
//     - UD-30 图标兜底：退官方物品图标（ItemAssetsCollection 元数据），取不到就不画那一格，不再顶一个菱形字符。
//
//   扣费、退费、锁定的业务路径一行不改；这里只管「看起来 / 听起来」怎么样。
//   单独成文件：_ComparisonAndState.cs 只剩个位数行预算（LargeFileBudgetGuard 1200）。
//
// 约束：
//   - 所有动效走 unscaled 时间 + BossRushUI.IsGamePaused() 门，缓动只用 EaseOut / SmoothStep，播完自己停（常态零 Update）。
//   - 贴在官方属性条目上的表现层（ReforgeRowFx）是条目的子物体，条目属于官方对象池：
//     一律 ignoreLayout、不吃射线；关闭界面时统一销毁（CleanupReforgeFeel）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    public static partial class ReforgeUIManager
    {
        // ---------------- 费用区字号（UD-23，1080p 参考口径：关键数字 30、正文 20、注脚 16） ----------------
        /// <summary>probabilityText 的基准字号；各行用 &lt;size&gt; 标签分级。</summary>
        private const int COST_BASE_FONT_SIZE = 18;
        private const int COST_HEADLINE_LABEL_SIZE = 18;
        private const int COST_HEADLINE_VALUE_SIZE = 30;
        private const int COST_TOTAL_SIZE = 20;
        private const int COST_DETAIL_SIZE = 16;

        // ---------------- 属性行固定（UD-24） ----------------
        private const float PROPERTY_LOCK_CONFIRM_SECONDS = 2f;
        private const string COLD_QUENCH_HINT_NAME = "LockHint";
        private const int COLD_QUENCH_HINT_FONT_SIZE = 15;
        /// <summary>单行框高 ≥ 字号×1.45+4（否则 TMP Ellipsis 整行清空；这里关了自动缩字）。</summary>
        private const float COLD_QUENCH_HINT_HEIGHT = 28f;
        private const float COLD_QUENCH_HINT_WIDTH = 640f;
        private const float PROPERTY_LOCK_FLASH_ALPHA = 0.35f;
        private const float PROPERTY_LOCK_FLASH_SECONDS = 0.5f;

        // ---------------- 揭晓节奏（UD-25 / UD-29） ----------------
        private const float REVEAL_ROW_STAGGER = 0.06f;
        private const float REVEAL_FLASH_ALPHA = 0.28f;
        private const float REVEAL_FLASH_SECONDS = 0.6f;
        internal const float REVEAL_POP_SCALE = 1.15f;
        internal const float REVEAL_POP_SECONDS = 0.18f;
        private const float AFFIX_REVEAL_STAGGER = 0.08f;
        private const float AFFIX_REVEAL_FADE_SECONDS = 0.22f;
        private const float AFFIX_STROKE_HOLD_SECONDS = 0.35f;
        private const float AFFIX_STROKE_FADE_SECONDS = 0.6f;

        // ---------------- 状态（全部在 CleanupReforgeFeel 里复位） ----------------
        private static TextMeshProUGUI coldQuenchHintText;
        private static PropertyEntryInteractable pendingLockEntry;
        private static Coroutine pendingLockTimeout;
        /// <summary>贴在官方属性条目上的表现层根物体，关闭界面时统一销毁（条目本身属于官方对象池，不能留我们的子物体）。</summary>
        private static readonly List<GameObject> reforgeRowFxObjects = new List<GameObject>();
        /// <summary>本次重铸变化了的行，等详情面板重建完（RefreshUIAfterReforgeDelayed 末尾）再揭晓。</summary>
        private static readonly List<ReforgeRevealRow> pendingRevealRows = new List<ReforgeRevealRow>();

        private struct ReforgeRevealRow
        {
            public string Key;
            public PropertyType Type;
            public int Ordinal;
            public bool Up;
            public bool AtMax;
        }

        // ============================================================================
        // UD-23 费用区
        // ============================================================================

        /// <summary>
        /// 重铸模式的费用区（2026-09-24 UI 共识对照审查 A-02）：只留两行——「重铸幅度 72%」（按档位取色）
        /// 与「总计花费 12,345」。系数、投入加成这些公式量不再写给玩家；涨跌倾向的概率与额外费用挪到倾向滑块旁边
        /// （A-03，RefreshTendencyLabel），总价同时写在按钮上（A-01，ApplyReforgeButtonLabel）。
        /// 钱不够时只把总计那一行染成 DangerText 并写「还差 X」。
        /// </summary>
        private static void RenderReforgeCostText()
        {
            // 倾向滑块的白话标签与按钮价钱跟费用区同一时机刷新：每条改费用的路径都会走到这里。
            RefreshTendencyLabel();
            ApplyReforgeButtonLabel();

            if (probabilityText == null)
            {
                return;
            }

            if (selectedItem == null)
            {
                probabilityText.text = L10n.T("请选择物品", "Select an item");
                probabilityText.color = BossRushUIColors.TextSecondary;
                return;
            }

            if (!ReforgeSystem.CanExecuteReforge(selectedItem))
            {
                probabilityText.text = L10n.T("该物品没有未固定的可重铸属性", "This item has no unlocked reforgeable properties.");
                probabilityText.color = BossRushUIColors.DangerText;
                return;
            }

            int rarity = GetItemQuality(selectedItem);
            float itemValue = ReforgeSystem.GetItemValue(selectedItem);
            float magnitude = ReforgeSystem.FinalProbability(rarity, itemValue, currentMoney);
            int tendencyCost = GetTendencyCost();
            int totalCost = currentMoney + tendencyCost;
            // 与 UpdateReforgeButtonInteractable 的 canAfford 同一判据：既要付得起总额，也要付得起基础费用。
            long required = Math.Max((long)totalCost, (long)ReforgeSystem.GetDiscountedCost(selectedItem));
            long shortfall = required - GetPlayerMoney();

            string tierHex = magnitude >= 0.8f
                ? IntegrationUIFeedback.SuccessHex
                : (magnitude >= 0.5f ? IntegrationUIFeedback.WarningHex : IntegrationUIFeedback.DangerHex);
            string totalLabel = IsReforgePaidWithPurification()
                ? L10n.T("总计花费（净化点）", "Total cost (Purification)")
                : L10n.T("总计花费", "Total cost");
            string totalLine = totalLabel + "  " + FormatReforgeAmount(totalCost);
            if (shortfall > 0)
            {
                totalLine = "<color=" + IntegrationUIFeedback.DangerHex + ">" + totalLine + "    "
                    + string.Format(L10n.T("还差 {0}", "{0} short"), FormatReforgeAmount(shortfall)) + "</color>";
            }

            probabilityText.text =
                "<size=" + COST_HEADLINE_LABEL_SIZE + "><color=" + IntegrationUIFeedback.SecondaryHex + ">"
                + L10n.T("重铸幅度", "Magnitude") + "</color></size>  "
                + "<size=" + COST_HEADLINE_VALUE_SIZE + "><b><color=" + tierHex + ">" + FormatReforgePercent(magnitude) + "</color></b></size>\n"
                + "<size=" + COST_TOTAL_SIZE + ">" + totalLine + "</size>";
            probabilityText.color = BossRushUIColors.TextPrimary;
        }

        /// <summary>
        /// 重铸按钮写价钱（A-01，UI 共识第 4 节「付费按钮写价钱」）：「重铸 · 12,345」，丧尸模式临时哥布林写「净化点」。
        /// 钱不够时按钮照挂（interactable=false 由 UpdateReforgeButtonInteractable 管），玩家一眼看得到要多少。
        /// 只改按钮里显示「分解 / 重铸」的那几段字（主文字与手柄提示里的同名文字），别的子文字不动。
        /// </summary>
        private static void ApplyReforgeButtonLabel()
        {
            if (reforgeButton == null || currentForgeMode != ForgeUIMode.Reforge)
            {
                return;
            }

            string label = L10n.T("重铸", "Reforge");
            if (selectedItem != null && ReforgeSystem.CanExecuteReforge(selectedItem))
            {
                string price = FormatReforgeAmount(currentMoney + GetTendencyCost());
                label = IsReforgePaidWithPurification()
                    ? string.Format(L10n.T("重铸 · {0} 净化点", "Reforge · {0} Purification"), price)
                    : string.Format(L10n.T("重铸 · {0}", "Reforge · {0}"), price);
            }

            TextMeshProUGUI[] texts = reforgeButton.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshProUGUI text = texts[i];
                if (text != null && IsReforgeButtonLabel(text.text) && text.text != label)
                {
                    text.text = label;
                }
            }
        }

        /// <summary>按钮里的这段字是不是「分解 / 重铸」动作名（含上一次写上去的价钱）。两种语言都认：玩家能在局内切语言。</summary>
        private static bool IsReforgeButtonLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return text == "分解" || text == "Decompose"
                || text.StartsWith("重铸", StringComparison.Ordinal)
                || text.StartsWith("Reforge", StringComparison.Ordinal);
        }

        /// <summary>
        /// 涨跌倾向滑块的白话标签（A-03）：旧版「正负极性倾向 / 偏向正面 (+20)」是内部术语，概率与额外费用又写在远处的费用区。
        /// 现在滑块旁边直接写「数值偏涨：涨 70% · 另加 1,234」；居中时「数值涨跌各半 · 不加钱」。
        /// 「涨」是数值变大，不等于变好（后坐力涨了是坏事），所以只写涨跌、不写好坏。
        /// </summary>
        private static void RefreshTendencyLabel()
        {
            if (tendencyText == null || currentForgeMode != ForgeUIMode.Reforge)
            {
                return;
            }

            float up = currentTendencyChance;
            int cost = GetTendencyCost();
            if (Mathf.Abs(up - 0.5f) < 0.001f)
            {
                tendencyText.text = L10n.T("数值涨跌各半 · 不加钱", "Values rise or fall 50/50 · no extra cost");
                return;
            }

            bool leansUp = up > 0.5f;
            string chance = FormatReforgePercent(leansUp ? up : 1f - up);
            string extra = FormatReforgeAmount(cost);
            string body = leansUp
                ? string.Format(L10n.T("数值偏涨：涨 {0} · 另加 {1}", "Leans up: {0} rise · +{1} cost"), chance, extra)
                : string.Format(L10n.T("数值偏跌：跌 {0} · 另加 {1}", "Leans down: {0} fall · +{1} cost"), chance, extra);
            tendencyText.text = "<color=" + (leansUp ? IntegrationUIFeedback.SuccessHex : IntegrationUIFeedback.WarningHex) + ">"
                + body + "</color>";
        }

        /// <summary>丧尸模式临时哥布林收净化点，其余收钱（与 OnReforgeButtonClick 的付款分支同一判据）。</summary>
        private static bool IsReforgePaidWithPurification()
        {
            return currentController != null &&
                ModBehaviour.Instance != null &&
                ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController);
        }

        /// <summary>金额千分位。固定用 InvariantCulture：玩家系统区域不同，不该出现「12.345」或「12 345」。</summary>
        private static string FormatReforgeAmount(long amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>百分比。不用 P0：InvariantCulture 的 P0 会写成「72 %」，当前区域又可能是别的写法。</summary>
        private static string FormatReforgePercent(float ratio)
        {
            return Mathf.RoundToInt(ratio * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // ============================================================================
        // UD-24 属性行两步固定
        // ============================================================================

        /// <summary>冷淬液计数下方的一行说明（容器的子物体，随容器一起隐藏 / 复用）。</summary>
        private static void EnsureColdQuenchHint(Transform container)
        {
            coldQuenchHintText = null;
            if (container == null)
            {
                return;
            }

            Transform existing = container.Find(COLD_QUENCH_HINT_NAME);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                coldQuenchHintText = existing.GetComponent<TextMeshProUGUI>();
                if (coldQuenchHintText != null)
                {
                    return;
                }
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            GameObject hint = ZombieModeUIHelper.CreateRect(
                COLD_QUENCH_HINT_NAME,
                container,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, -2f),
                new Vector2(COLD_QUENCH_HINT_WIDTH, COLD_QUENCH_HINT_HEIGHT),
                new Vector2(0.5f, 1f));
            // 容器是水平布局：说明行挂在容器底边下方，不参与布局。
            hint.AddComponent<LayoutElement>().ignoreLayout = true;
            coldQuenchHintText = ZombieModeUIHelper.CreateTMPText(
                hint,
                string.Empty,
                COLD_QUENCH_HINT_FONT_SIZE,
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);
            coldQuenchHintText.enableAutoSizing = false;
            coldQuenchHintText.enableWordWrapping = false;
            coldQuenchHintText.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>说明行三种状态：待确认（Accent）/ 有冷淬液 / 没有冷淬液。</summary>
        private static void UpdateColdQuenchHint(int fluidCount)
        {
            if (coldQuenchHintText == null)
            {
                return;
            }

            if (pendingLockEntry != null)
            {
                coldQuenchHintText.text = L10n.T("再点一次同一行即可固定（消耗 1 瓶冷淬液）",
                    "Click the same row again to lock it (uses 1 Cold Quench Fluid)");
                coldQuenchHintText.color = BossRushUIColors.Accent;
                return;
            }

            coldQuenchHintText.text = fluidCount > 0
                ? L10n.T("点两下属性行即可固定，之后重铸不再改变它（每次 1 瓶）",
                    "Click a stat row twice to lock it against reforging (1 fluid each)")
                : L10n.T("冷淬液可以固定属性，让重铸不再改变它",
                    "Cold Quench Fluid locks a stat so reforging leaves it alone");
            coldQuenchHintText.color = BossRushUIColors.TextSecondary;
        }

        private static void RefreshColdQuenchHint()
        {
            if (coldQuenchHintText == null)
            {
                return;
            }
            UpdateColdQuenchHint(ItemFactory.GetItemCountInInventory(ColdQuenchFluidConfig.TYPE_ID));
        }

        internal static bool IsPropertyLockPending(PropertyEntryInteractable entry)
        {
            return entry != null && ReferenceEquals(pendingLockEntry, entry);
        }

        /// <summary>第一次点：进入待确认，不扣任何东西。同一时间只有一行待确认，点别的行会把前一行撤回。</summary>
        internal static void BeginPropertyLockPending(PropertyEntryInteractable entry)
        {
            CancelPendingPropertyLock();
            if (entry == null)
            {
                return;
            }

            pendingLockEntry = entry;
            entry.EnterPending();
            RefreshColdQuenchHint();
            IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundPop);
            if (ModBehaviour.Instance != null)
            {
                pendingLockTimeout = ModBehaviour.Instance.StartCoroutine(PropertyLockPendingTimeout(entry));
            }
        }

        /// <summary>撤回待确认（超时、确认、切换物品、关闭界面都走这里）。幂等。</summary>
        internal static void CancelPendingPropertyLock()
        {
            if (pendingLockTimeout != null && ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StopCoroutine(pendingLockTimeout);
            }
            pendingLockTimeout = null;

            PropertyEntryInteractable entry = pendingLockEntry;
            pendingLockEntry = null;
            if (entry == null)
            {
                return;
            }

            try
            {
                entry.ExitPending();
            }
            catch { }
            RefreshColdQuenchHint();
        }

        private static System.Collections.IEnumerator PropertyLockPendingTimeout(PropertyEntryInteractable entry)
        {
            yield return new WaitForSecondsRealtime(PROPERTY_LOCK_CONFIRM_SECONDS);
            // 先置空自己，CancelPendingPropertyLock 不会再去 StopCoroutine 正在收尾的这一条。
            pendingLockTimeout = null;
            if (ReferenceEquals(pendingLockEntry, entry))
            {
                CancelPendingPropertyLock();
            }
        }

        /// <summary>固定成功：UI/confirm + 行底金色闪一下 + 数值轻微放大回落。</summary>
        internal static void PlayPropertyLockedFeedback(PropertyEntryInteractable entry)
        {
            if (entry == null || entry.Fx == null)
            {
                IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundConfirm);
                return;
            }

            entry.Fx.Flash(
                BossRushUIColors.RarityLegendary,
                PROPERTY_LOCK_FLASH_ALPHA,
                PROPERTY_LOCK_FLASH_SECONDS,
                0f,
                entry.ValueText != null ? entry.ValueText.transform : null,
                IntegrationUIFeedback.SoundConfirm);
        }

        internal static void TrackRowFx(GameObject fxRoot)
        {
            if (fxRoot != null && !reforgeRowFxObjects.Contains(fxRoot))
            {
                reforgeRowFxObjects.Add(fxRoot);
            }
        }

        private static void DestroyReforgeRowFx()
        {
            for (int i = 0; i < reforgeRowFxObjects.Count; i++)
            {
                GameObject fxRoot = reforgeRowFxObjects[i];
                if (fxRoot != null)
                {
                    UnityEngine.Object.Destroy(fxRoot);
                }
            }
            reforgeRowFxObjects.Clear();
        }

        // ============================================================================
        // UD-25 重铸结果逐行揭晓
        // ============================================================================

        /// <summary>ShowPropertyChanges 里每一条变化的属性登记一次；触顶判据与 Max 标签同一个（IsValueAtUpperBound）。</summary>
        private static void QueueReforgeReveal(string key, PropertyType propType, int entryOrdinal, float newValue, float diff)
        {
            float prefabValue;
            bool atMax = diff > 0f
                && TryGetCachedPrefabValue(key, propType, entryOrdinal, out prefabValue)
                && ReforgeSystem.IsValueAtUpperBound(key, prefabValue, newValue);
            pendingRevealRows.Add(new ReforgeRevealRow
            {
                Key = key,
                Type = propType,
                Ordinal = entryOrdinal,
                Up = diff > 0f,
                AtMax = atMax
            });
        }

        private static void ResetReforgeRevealQueue()
        {
            pendingRevealRows.Clear();
        }

        /// <summary>
        /// 详情面板重建完之后（RefreshUIAfterReforgeDelayed 末尾）按显示顺序逐行揭晓：
        /// 每行间隔 0.06 秒，行底闪一下（涨 SuccessText / 跌 DangerText / 触顶 RarityLegendary 并播 UI/pop），
        /// 数值从 1.15 倍 EaseOut 回落。挂在重建之后，才不会被整页 Setup 刷掉。
        /// </summary>
        private static void PlayQueuedReforgeReveal()
        {
            if (pendingRevealRows.Count == 0)
            {
                return;
            }

            try
            {
                ItemDetailsDisplay details = detailsDisplayObj as ItemDetailsDisplay;
                Transform propsParent = details != null && PropertiesParentField != null
                    ? PropertiesParentField.GetValue(details) as Transform
                    : null;
                if (propsParent == null)
                {
                    return;
                }

                int order = 0;
                foreach (Transform child in propsParent)
                {
                    if (child == null || !child.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    string key;
                    PropertyType propType;
                    int entryOrdinal;
                    TextMeshProUGUI valueText;
                    if (!TryGetDisplayedEntryInfo(child, out key, out propType, out entryOrdinal, out valueText))
                    {
                        continue;
                    }

                    int index = FindRevealRow(key, propType, entryOrdinal);
                    if (index < 0)
                    {
                        continue;
                    }

                    ReforgeRevealRow row = pendingRevealRows[index];
                    ReforgeRowFx fx = ReforgeRowFx.Ensure(child);
                    if (fx == null)
                    {
                        continue;
                    }

                    Color flashColor = row.AtMax
                        ? BossRushUIColors.RarityLegendary
                        : (row.Up ? BossRushUIColors.SuccessText : BossRushUIColors.DangerText);
                    fx.Flash(flashColor, REVEAL_FLASH_ALPHA, REVEAL_FLASH_SECONDS, order * REVEAL_ROW_STAGGER,
                        valueText != null ? valueText.transform : null,
                        row.AtMax ? IntegrationUIFeedback.SoundPop : null);
                    order++;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 重铸揭晓动效失败: " + e.Message);
            }
            finally
            {
                pendingRevealRows.Clear();
            }
        }

        private static int FindRevealRow(string key, PropertyType propType, int entryOrdinal)
        {
            for (int i = 0; i < pendingRevealRows.Count; i++)
            {
                ReforgeRevealRow row = pendingRevealRows[i];
                if (row.Type == propType && row.Ordinal == entryOrdinal && row.Key == key)
                {
                    return i;
                }
            }
            return -1;
        }

        // ============================================================================
        // UD-29 词缀揭晓 / UD-27 锁定按钮外观
        // ============================================================================

        /// <summary>
        /// 随机词缀成功后：本次重抽的行（未锁、非空）依次淡入，每行间隔 0.08 秒；
        /// 描边先换成稀有度色、停 0.35 秒后 0.6 秒 SmoothStep 回到 Stroke；第一条稀有词缀出场时播 UI/level_up。
        /// 行是 LayoutGroup 的子项：只动 CanvasGroup 透明度与描边颜色，不动 anchoredPosition（会和布局抢位置）。
        /// </summary>
        private static void PlayAffixRollReveal()
        {
            if (selectedItem == null)
            {
                return;
            }

            try
            {
                int order = 0;
                bool rareSounded = false;
                for (int i = 0; i < affixRows.Count; i++)
                {
                    AffixRowWidgets row = affixRows[i];
                    if (row == null || row.Root == null || !row.Root.activeInHierarchy)
                    {
                        continue;
                    }

                    AffixSlotView view = default(AffixSlotView);
                    if (!AffixItemData.TryReadSlot(selectedItem, row.SlotIndex, out view) || view.IsEmpty || view.Locked)
                    {
                        continue;
                    }

                    AffixDefinition definition = AffixDefinitions.Find(view.AffixId);
                    float delay = order * AFFIX_REVEAL_STAGGER;
                    string sound = null;
                    if (!rareSounded && definition != null && definition.Rarity == AffixRarity.Rare)
                    {
                        sound = IntegrationUIFeedback.SoundLevelUp;
                        rareSounded = true;
                    }

                    CanvasGroup group = row.Root.GetComponent<CanvasGroup>();
                    if (group == null)
                    {
                        group = row.Root.AddComponent<CanvasGroup>();
                    }
                    ReforgeUIFade.PlayFadeIn(group, delay, AFFIX_REVEAL_FADE_SECONDS, sound);

                    Transform strokeTransform = row.Root.transform.Find("Stroke");
                    Image stroke = strokeTransform != null ? strokeTransform.GetComponent<Image>() : null;
                    if (stroke != null)
                    {
                        Color rarityColor = definition != null ? GetRarityColor(definition.Rarity) : BossRushUIColors.Accent;
                        ReforgeUIFade.PlayColor(stroke, rarityColor, BossRushUIColors.Stroke,
                            delay + AFFIX_STROKE_HOLD_SECONDS, AFFIX_STROKE_FADE_SECONDS);
                    }
                    order++;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] 词缀揭晓动效失败: " + e.Message);
            }
        }

        /// <summary>
        /// 词缀槽按钮：「锁定」是次级按钮（SurfaceRaised + Stroke，悬停由 GetHoverColor 派生）；
        /// 「解锁」是危险次级按钮——深底不变、DangerText 描边 + 红字（UI 共识第 4 节：进确认前不铺实心色块）。
        /// 旧版把「已锁定」画成 Warning 实心底 + 金描边的按钮（A-06），读起来像主操作，一点就免费解锁。
        /// </summary>
        private static void ApplyAffixLockButtonLook(Button button, bool unlockAction)
        {
            if (button == null)
            {
                return;
            }

            // 幂等：保证描边存在、底色回到次级口径（同时把标签色按底色复位）。
            BossRushUIKit.StyleSecondaryButton(button);

            Graphic target = button.targetGraphic;
            Transform strokeTransform = target != null ? target.transform.Find("Stroke") : null;
            Image stroke = strokeTransform != null ? strokeTransform.GetComponent<Image>() : null;
            if (stroke != null)
            {
                stroke.color = unlockAction ? BossRushUIColors.DangerText : BossRushUIColors.Stroke;
            }

            if (unlockAction)
            {
                Transform labelTransform = button.transform.Find("Text");
                TextMeshProUGUI label = labelTransform != null ? labelTransform.GetComponent<TextMeshProUGUI>() : null;
                if (label != null)
                {
                    label.color = BossRushUIColors.DangerText;
                }
            }
        }

        // ============================================================================
        // UD-30 图标兜底
        // ============================================================================

        /// <summary>本 Mod 注册物品的官方元数据图标（动态注册表里带着配置器注入的图标）。取不到返回 null。</summary>
        private static Sprite TryGetItemMetaIcon(int typeId)
        {
            try
            {
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                return meta.id == typeId ? meta.icon : null;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================================
        // 清理
        // ============================================================================

        /// <summary>由 Cleanup() 在 CleanupColdQuenchFluidUI() 之后调用：撤回待确认、清揭晓队列、拆掉贴在官方条目上的表现层。</summary>
        private static void CleanupReforgeFeel()
        {
            CancelPendingPropertyLock();
            pendingRevealRows.Clear();
            DestroyReforgeRowFx();
            coldQuenchHintText = null;
        }
    }

    /// <summary>
    /// 贴在官方属性条目上的一层表现（UD-24 / UD-25）：左侧「可点」细条、悬停 / 待确认的行底淡色、揭晓与固定成功的闪光。
    /// 条目属于官方对象池：本物体 ignoreLayout、不吃射线、排在第一个子物体（画在文字下面），关闭界面时由 ReforgeUIManager 统一销毁。
    /// 自己没有 Update；闪光交给 <see cref="ReforgeUIFade"/>，播完自停。
    /// </summary>
    internal sealed class ReforgeRowFx : MonoBehaviour
    {
        internal const string ObjectName = "ReforgeRowFx";
        /// <summary>行底淡色与闪光的圆角：官方条目是细长行，4 足够圆、不会在两端显出胶囊形。</summary>
        private const int TintRadius = 4;
        private const float BarWidth = 2f;
        private const float BarInset = 4f;

        private Image tint;
        private Image flash;
        private Image bar;

        internal static ReforgeRowFx Ensure(Transform entry)
        {
            if (entry == null)
            {
                return null;
            }

            Transform existing = entry.Find(ObjectName);
            ReforgeRowFx fx = existing != null ? existing.GetComponent<ReforgeRowFx>() : null;
            if (fx == null)
            {
                if (existing != null)
                {
                    UnityEngine.Object.Destroy(existing.gameObject);
                }
                fx = Build(entry);
            }

            // 画在条目自己的文字下面（uGUI 子物体按顺序绘制）。
            fx.transform.SetAsFirstSibling();
            ReforgeUIManager.TrackRowFx(fx.gameObject);
            return fx;
        }

        private static ReforgeRowFx Build(Transform entry)
        {
            GameObject root = ZombieModeUIHelper.CreateRect(ObjectName, entry, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            root.AddComponent<LayoutElement>().ignoreLayout = true;
            ReforgeRowFx fx = root.AddComponent<ReforgeRowFx>();
            fx.tint = CreateLayer(root.transform, "Tint", TintRadius, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fx.flash = CreateLayer(root.transform, "Flash", TintRadius, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fx.bar = CreateLayer(root.transform, "Bar", 1, Vector2.zero, new Vector2(0f, 1f),
                new Vector2(0f, BarInset), new Vector2(BarWidth, -BarInset));
            fx.SetIdle();
            return fx;
        }

        private static Image CreateLayer(Transform parent, string name, int radius,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, anchorMin, anchorMax,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = obj.AddComponent<Image>();
            image.raycastTarget = false;   // 点击仍由条目自己的 Image 接
            // 细条档一律程序化圆角：行底淡色只要形状，不要图集面板的纹理细节。
            BossRushUI.ApplyPanelSkin(image, radius, BossRushUISkinPart.Hairline);
            image.color = Color.clear;
            return image;
        }

        /// <summary>常驻状态：细条颜色 / 透明度，行底淡色颜色 / 透明度。透明度为 0 的层直接关掉。</summary>
        internal void SetState(Color barColor, float barAlpha, Color tintColor, float tintAlpha)
        {
            SetLayer(bar, barColor, barAlpha);
            SetLayer(tint, tintColor, tintAlpha);
        }

        internal void SetIdle()
        {
            SetLayer(bar, Color.clear, 0f);
            SetLayer(tint, Color.clear, 0f);
        }

        /// <summary>行底闪一下：<paramref name="alpha"/> → 0，SmoothStep；可带数值放大回落与起播音效。</summary>
        internal void Flash(Color color, float alpha, float seconds, float delay, Transform popTarget, string sound)
        {
            if (flash == null)
            {
                return;
            }

            Color from = color;
            from.a = alpha;
            Color to = color;
            to.a = 0f;
            ReforgeUIFade.PlayColor(flash, from, to, delay, seconds, popTarget,
                popTarget != null ? ReforgeUIManager.REVEAL_POP_SCALE : 1f,
                popTarget != null ? ReforgeUIManager.REVEAL_POP_SECONDS : 0f, sound);
        }

        private static void SetLayer(Image layer, Color color, float alpha)
        {
            if (layer == null)
            {
                return;
            }
            color.a = alpha;
            layer.color = color;
            layer.enabled = alpha > 0.001f;
        }
    }

    /// <summary>
    /// 重铸界面的一次性过渡（UD-24 / UD-25 / UD-29）：Graphic 颜色 from → to（SmoothStep）、CanvasGroup 淡入、
    /// 可选的数值放大回落（EaseOut）与起播音效。unscaled 时间，过 IsGamePaused 门，播完 enabled=false（常态零开销）。
    /// 被停用（行被隐藏、条目回收进官方对象池、界面关闭）时直接落到终态，不留半透明或放大的残影。
    /// </summary>
    internal sealed class ReforgeUIFade : MonoBehaviour
    {
        private Graphic graphic;
        private Color fromColor;
        private Color toColor;
        private CanvasGroup group;
        private Transform popTarget;
        private float popFrom = 1f;
        private float popSeconds;
        private string sound;
        private float delay;
        private float duration;
        private float elapsed;
        private bool playing;
        private bool soundPlayed;

        internal static ReforgeUIFade PlayColor(Graphic target, Color from, Color to, float delay, float duration,
            Transform pop = null, float popFromScale = 1f, float popDuration = 0f, string startSound = null)
        {
            if (target == null)
            {
                return null;
            }

            ReforgeUIFade fade = Acquire(target.gameObject);
            fade.graphic = target;
            fade.fromColor = from;
            fade.toColor = to;
            fade.popTarget = pop;
            fade.popFrom = popFromScale;
            fade.popSeconds = popDuration;
            fade.Begin(delay, duration, startSound);
            target.color = from;
            return fade;
        }

        internal static ReforgeUIFade PlayFadeIn(CanvasGroup target, float delay, float duration, string startSound = null)
        {
            if (target == null)
            {
                return null;
            }

            ReforgeUIFade fade = Acquire(target.gameObject);
            fade.group = target;
            fade.Begin(delay, duration, startSound);
            target.alpha = 0f;
            return fade;
        }

        private static ReforgeUIFade Acquire(GameObject host)
        {
            ReforgeUIFade fade = host.GetComponent<ReforgeUIFade>();
            if (fade == null)
            {
                fade = host.AddComponent<ReforgeUIFade>();
            }
            else if (fade.playing)
            {
                // 重播前先把上一轮落到终态（放大的数值缩回 1），再换参数。
                fade.Finish();
            }
            fade.graphic = null;
            fade.group = null;
            fade.popTarget = null;
            return fade;
        }

        private void Begin(float delaySeconds, float durationSeconds, string startSound)
        {
            delay = Mathf.Max(0f, delaySeconds);
            duration = Mathf.Max(0.01f, durationSeconds);
            sound = startSound;
            soundPlayed = false;
            elapsed = 0f;
            playing = true;
            enabled = true;
        }

        private void Update()
        {
            if (!playing)
            {
                enabled = false;
                return;
            }
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            if (elapsed < delay)
            {
                return;
            }

            if (!soundPlayed)
            {
                soundPlayed = true;
                if (!string.IsNullOrEmpty(sound))
                {
                    IntegrationUIFeedback.PlaySound(sound);
                }
            }

            float local = elapsed - delay;
            float t = Mathf.Clamp01(local / duration);
            float eased = BossRushUI.SmoothStep(t);
            if (graphic != null)
            {
                graphic.color = Color.LerpUnclamped(fromColor, toColor, eased);
            }
            if (group != null)
            {
                group.alpha = eased;
            }

            bool popDone = true;
            if (popTarget != null && popSeconds > 0f)
            {
                float popT = Mathf.Clamp01(local / popSeconds);
                float scale = Mathf.LerpUnclamped(popFrom, 1f, BossRushUI.EaseOut(popT));
                popTarget.localScale = new Vector3(scale, scale, 1f);
                popDone = popT >= 1f;
            }

            if (t >= 1f && popDone)
            {
                Finish();
            }
        }

        private void OnDisable()
        {
            if (playing)
            {
                Finish();
            }
        }

        private void Finish()
        {
            playing = false;
            if (graphic != null)
            {
                graphic.color = toColor;
            }
            if (group != null)
            {
                group.alpha = 1f;
            }
            if (popTarget != null)
            {
                popTarget.localScale = Vector3.one;
                popTarget = null;
            }
            enabled = false;
        }
    }
}
