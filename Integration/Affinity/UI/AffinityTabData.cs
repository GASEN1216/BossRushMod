// ============================================================================
// AffinityTabData.cs - 好感页签的数据组装（不碰 UI）
// ============================================================================
// 好感页签（官方 Tab 页签栏最左一格，见 AffinityTabInjector.cs）列出所有登记了好感配置的 NPC，
// 每人一行：名字、十颗心、一行状态；右栏是选中那位的等级进度、今天聊过 / 送过没有、各级好感奖励。
// 本文件只读 AffinityManager 与各 NPC 配置，拼成纯数据；画法在 AffinityTabView.cs。
// 文案在取用时按当前语言解析（玩家可以在游戏里切语言），所以每次打开页签都重新组装。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>一级好感奖励（来自各 NPC 配置的 UnlocksByLevel）。</summary>
    internal struct AffinityTabUnlock
    {
        public int Level;
        public string Text;
        public bool Reached;
    }

    /// <summary>好感页签里的一位 NPC。</summary>
    internal sealed class AffinityTabEntry
    {
        public string NpcId;
        public string Name;
        public int Points;
        public int Level;
        public int MaxLevel;
        public int Progress;
        public int Required;
        /// <summary>聊过、送过礼或有点数，就算打过交道；没打过交道的排在最后。</summary>
        public bool Met;
        public bool Married;
        public string MarriageDate;
        public bool ChattedToday;
        public bool GiftedToday;
        /// <summary>当前折扣（0–1）；没有商店 / 治疗服务或没到折扣档为 0。</summary>
        public float Discount;
        /// <summary>折扣是治疗折扣（羽织不开商店，DiscountsByLevel 用在治疗上）。</summary>
        public bool DiscountIsHealing;
        public readonly List<AffinityTabUnlock> Unlocks = new List<AffinityTabUnlock>();
        /// <summary>
        /// 心意簿（2026-11-24 玩家许愿第 6 项）：送礼摸清的喜欢 / 不喜欢（显示名），与还没摸清的条数。
        /// 这位 NPC 没有任何礼物偏好配置时 HasGiftJournal 为 false，详情里不画这一节。
        /// </summary>
        public bool HasGiftJournal;
        public readonly List<string> KnownLikes = new List<string>();
        public readonly List<string> KnownDislikes = new List<string>();
        public int UnknownLikes;
        public int UnknownDislikes;

        public bool IsMaxLevel { get { return Level >= MaxLevel; } }
    }

    internal static class AffinityTabData
    {
        /// <summary>按「配偶 → 点数高 → 没打过交道」排好的全部 NPC。</summary>
        internal static List<AffinityTabEntry> Build()
        {
            List<AffinityTabEntry> entries = new List<AffinityTabEntry>();
            List<string> ids;
            try
            {
                ids = AffinityManager.GetRegisteredNpcIds();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityTab] [WARNING] 读取 NPC 列表失败: " + e.Message);
                return entries;
            }

            int today = SafeCurrentDay();
            for (int i = 0; i < ids.Count; i++)
            {
                AffinityTabEntry entry = BuildEntry(ids[i], today);
                if (entry != null) entries.Add(entry);
            }

            entries.Sort(CompareEntries);
            return entries;
        }

        private static AffinityTabEntry BuildEntry(string npcId, int today)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            try
            {
                // 测试用捏脸 NPC（蓝图 testNpc: true）不进好感页
                DuckNpcBlueprint blueprint;
                if (PermanentDuckNpcRegistry.TryGetBlueprint(npcId, out blueprint) && blueprint.testNpc) return null;

                INPCAffinityConfig config = AffinityManager.GetNPCConfig(npcId);
                if (config == null) return null;

                AffinityTabEntry entry = new AffinityTabEntry();
                entry.NpcId = npcId;
                entry.Name = !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : npcId;
                entry.Points = AffinityManager.GetPoints(npcId);
                entry.Level = AffinityManager.GetLevel(npcId);
                entry.MaxLevel = config.MaxLevel > 0 ? config.MaxLevel : AffinityConfig.DEFAULT_MAX_LEVEL;
                AffinityManager.GetLevelProgressDetails(npcId, out entry.Progress, out entry.Required);

                int lastChat = AffinityManager.GetLastChatDay(npcId);
                int lastGift = AffinityManager.GetLastGiftDay(npcId);
                entry.ChattedToday = today >= 0 && lastChat == today;
                entry.GiftedToday = today >= 0 && lastGift == today;
                entry.Married = AffinityManager.IsMarriedToPlayer(npcId);
                entry.MarriageDate = entry.Married ? AffinityManager.GetMarriageDateText(npcId) : null;
                entry.Met = entry.Points > 0 || lastChat >= 0 || lastGift >= 0 || entry.Married;
                // 有商店的按商店折扣（与开店实际价格同一个入口）；羽织的 DiscountsByLevel 是治疗折扣，单独标
                INPCShopConfig shopConfig = config as INPCShopConfig;
                if (shopConfig != null && shopConfig.ShopEnabled)
                {
                    entry.Discount = NPCShopSystem.GetDiscount(npcId);
                }
                else if (config is NurseAffinityConfig)
                {
                    entry.Discount = AffinityManager.GetDiscount(npcId);
                    entry.DiscountIsHealing = true;
                }

                Dictionary<int, List<string>> byLevel = new Dictionary<int, List<string>>();
                Dictionary<int, string[]> unlocks = config.UnlocksByLevel;
                if (unlocks != null)
                {
                    foreach (KeyValuePair<int, string[]> pair in unlocks)
                    {
                        if (pair.Value == null) continue;
                        for (int i = 0; i < pair.Value.Length; i++) AddUnlockText(byLevel, pair.Key, pair.Value[i]);
                    }
                }
                // 捏脸 NPC 的奖励表只登记商店与剧情，折扣档另在 DiscountsByLevel（升级横幅也是分开读的）；
                // 叮当、羽织的奖励表里已经手写了折扣，不重复并。
                if (blueprint != null) AppendDiscounts(byLevel, config.DiscountsByLevel);

                List<int> levels = new List<int>(byLevel.Keys);
                levels.Sort();
                for (int i = 0; i < levels.Count; i++)
                {
                    AffinityTabUnlock unlock;
                    unlock.Level = levels[i];
                    unlock.Text = string.Join(L10n.T("、", ", "), byLevel[levels[i]].ToArray());
                    unlock.Reached = entry.Level >= levels[i];
                    entry.Unlocks.Add(unlock);
                }
                FillGiftJournal(entry, npcId, config as INPCGiftConfig);
                return entry;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityTab] [WARNING] 组装 " + npcId + " 失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 心意簿：按这位 NPC 当前的礼物配置列出全部心意规则（与 NPCGiftSystem.ResolveGiftPreference 同一判定顺序：
        /// 厌恶物品优先，其次喜欢物品、收集品 LikedBy、喜欢的标签），再对照心意簿里已摸清的令牌。
        /// 配置里已经没有的旧令牌不显示，也不算进「已摸清」。
        /// </summary>
        private static void FillGiftJournal(AffinityTabEntry entry, string npcId, INPCGiftConfig gift)
        {
            if (gift == null) return;
            List<string> dislikes = new List<string>();
            List<string> likes = new List<string>();
            Dictionary<int, int> negative = gift.NegativeItems;
            Dictionary<int, int> positive = gift.PositiveItems;
            if (negative != null)
            {
                foreach (int typeId in negative.Keys) dislikes.Add(AffinityManager.GiftItemToken(typeId));
            }
            if (positive != null)
            {
                foreach (int typeId in positive.Keys)
                {
                    if (negative == null || !negative.ContainsKey(typeId)) likes.Add(AffinityManager.GiftItemToken(typeId));
                }
            }
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def == null || Array.IndexOf(def.LikedBy, npcId) < 0) continue;
                if ((negative != null && negative.ContainsKey(def.TypeId)) || (positive != null && positive.ContainsKey(def.TypeId))) continue;
                likes.Add(AffinityManager.GiftItemToken(def.TypeId));
            }
            if (gift.PositiveTags != null)
            {
                foreach (string tag in gift.PositiveTags)
                {
                    if (!string.IsNullOrEmpty(tag)) likes.Add(AffinityManager.GiftTagToken(tag));
                }
            }
            entry.HasGiftJournal = likes.Count + dislikes.Count > 0;
            entry.UnknownLikes = FillJournalSide(entry.KnownLikes, likes, AffinityManager.GetKnownGiftPreferences(npcId, true));
            entry.UnknownDislikes = FillJournalSide(entry.KnownDislikes, dislikes, AffinityManager.GetKnownGiftPreferences(npcId, false));
        }

        /// <summary>把仍在配置里的已摸清令牌换成显示名放进 names，返回还没摸清的条数。</summary>
        private static int FillJournalSide(List<string> names, List<string> configured, List<string> known)
        {
            int found = 0;
            for (int i = 0; i < known.Count; i++)
            {
                if (!configured.Contains(known[i])) continue;
                names.Add(DescribeGiftToken(known[i]));
                found++;
            }
            return configured.Count - found;
        }

        /// <summary>item:&lt;TypeID&gt; → 物品名；tag:&lt;标签名&gt; → 「配方类」。在取用时解析，跟着当前语言走。</summary>
        private static string DescribeGiftToken(string token)
        {
            if (token.StartsWith(AffinityManager.GiftTagTokenPrefix, StringComparison.Ordinal))
            {
                string tag = token.Substring(AffinityManager.GiftTagTokenPrefix.Length);
                string key = "Tag_" + tag;
                string text = LocalizationHelper.GetLocalizedText(key);
                if (string.IsNullOrEmpty(text) || text == key || (text.StartsWith("*") && text.EndsWith("*"))) text = tag;
                return L10n.T(text + "类", "any " + text);
            }
            int typeId;
            if (!int.TryParse(token.Substring(AffinityManager.GiftItemTokenPrefix.Length), out typeId)) return token;
            try
            {
                string name = ItemAssetsCollection.GetMetaData(typeId).DisplayName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
                // 元数据读不到（物品被移除）时退回编号
            }
            return "#" + typeId;
        }

        private static void AddUnlockText(Dictionary<int, List<string>> byLevel, int level, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            List<string> list;
            if (!byLevel.TryGetValue(level, out list))
            {
                list = new List<string>();
                byLevel[level] = list;
            }
            list.Add(text);
        }

        private static void AppendDiscounts(Dictionary<int, List<string>> byLevel, Dictionary<int, float> discounts)
        {
            if (discounts == null) return;
            foreach (KeyValuePair<int, float> pair in discounts)
            {
                if (pair.Value <= 0.001f) continue;
                double percent = Math.Round(pair.Value * 100f);
                AddUnlockText(byLevel, pair.Key, L10n.T(percent + "%折扣", percent + "% Discount"));
            }
        }

        private static int CompareEntries(AffinityTabEntry a, AffinityTabEntry b)
        {
            if (a.Married != b.Married) return a.Married ? -1 : 1;
            if (a.Met != b.Met) return a.Met ? -1 : 1;
            if (a.Points != b.Points) return b.Points.CompareTo(a.Points);
            return string.CompareOrdinal(a.Name, b.Name);
        }

        private static int SafeCurrentDay()
        {
            try { return NPCGiftSystem.GetCurrentGameDay(); }
            catch { return -1; }
        }

        /// <summary>列表行的一行状态：「Lv.6 · 今天聊过 · 送过礼」「配偶 · Lv.10」「还没打过交道」。</summary>
        internal static string BuildRowStatus(AffinityTabEntry entry)
        {
            if (entry == null) return string.Empty;
            if (!entry.Met) return L10n.T("还没打过交道", "Not met yet");

            string text = entry.Married
                ? L10n.T("配偶 · Lv.", "Spouse · Lv.") + entry.Level
                : "Lv." + entry.Level;
            if (entry.ChattedToday) text += L10n.T(" · 今天聊过", " · Chatted today");
            if (entry.GiftedToday) text += L10n.T(" · 送过礼", " · Gifted today");
            return text;
        }

        /// <summary>详情页等级行：「好感 Lv.6 · 120/300」；满级写「好感 Lv.10 · 满级」。</summary>
        internal static string BuildLevelLine(AffinityTabEntry entry)
        {
            if (entry == null) return string.Empty;
            string line = L10n.T("好感 Lv.", "Affinity Lv.") + entry.Level + " · ";
            line += entry.IsMaxLevel
                ? L10n.T("满级", "MAX")
                : entry.Progress + "/" + entry.Required;
            if (entry.Discount > 0.001f)
            {
                line += (entry.DiscountIsHealing
                    ? L10n.T(" · 治疗折扣 ", " · Healing discount ")
                    : L10n.T(" · 商店折扣 ", " · Shop discount ")) + Math.Round(entry.Discount * 100f) + "%";
            }
            return line;
        }

        /// <summary>升到下一级的进度（0–1）；满级为 1。</summary>
        internal static float LevelProgress01(AffinityTabEntry entry)
        {
            if (entry == null) return 0f;
            if (entry.IsMaxLevel || entry.Required <= 0) return 1f;
            float value = (float)entry.Progress / entry.Required;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
