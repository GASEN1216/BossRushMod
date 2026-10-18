// ============================================================================
// PetNestUIMuseumPage.cs - 博物馆页的数据组装（2026-10-17 从 PetNestUIPages.cs 挪出并重写）
// ============================================================================
// 与 PetNestUIPages.cs 是同一个 partial 类，拆开为行数预算，也因为这一页这次几乎重写：
//   - E-1 图鉴铺满全谱系：没遇到的血脉也占一格（剪影 + 「？？？」），顶部「已解锁 x / 全部」和格子数对得上
//     （宝可梦图鉴的口径）。旧写法只列统计记录里有的血脉，首次击杀前连格子都没有；
//   - E-2 格子点进去看血脉详情（元素、契合的远征目的地、巢里有几只、遗魂进度、怎么解锁），详情是面板上的浮层卡；
//   - E-3 驯养成就挂进博物馆（Wiki 一直这么写，页面上此前没有）；
//   - E-4 碑文写阵亡日期（deathTicks 一直存着，界面没显示）。
// 本文件同样**不创建 canvas、不碰 sortingOrder**，只组装只读数据。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static partial class PetNestUIPages
    {
        #region 博物馆

        /// <summary>驯养成就 id（注册在 BossRushAchievementManager「驯养成就」一节，顺序即展示顺序）。</summary>
        private static readonly string[] TamingAchievementIds =
        {
            "petnest_first_hatch", "petnest_lineage_10", "petnest_lineage_30", "petnest_shiny", "petnest_memorial",
        };

        /// <summary>博物馆页：血脉图鉴网格（全谱系）+ 驯养成就 + 阵亡纪念碑。<paramref name="openLineage"/> 为 null 时格子不可点。</summary>
        internal static PetNestPageContent BuildMuseumPage(Action<string> openLineage = null)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Museum");
            // 血脉图鉴按 4 列网格排：一格一张立绘 + 名字 + 两行数字；未解锁的压成剪影（UA-17）
            page.CardsAsGrid = true;

            PetNestMuseumData museum = PetNestPersistenceAccess.Museum;
            Dictionary<string, PetNestLineageStats> byKey = new Dictionary<string, PetNestLineageStats>(StringComparer.Ordinal);
            for (int i = 0; i < museum.lineages.Count; i++)
            {
                PetNestLineageStats stats = museum.lineages[i];
                if (stats != null && !string.IsNullOrEmpty(stats.lineageKey)) byKey[stats.lineageKey] = stats;
            }

            int unlocked = 0;
            int seen = 0;
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            for (int i = 0; i < lineages.Count; i++)
            {
                PetNestLineageInfo lineage = lineages[i];
                if (lineage == null) continue;
                PetNestLineageStats stats;
                byKey.TryGetValue(lineage.LineageKey, out stats);
                if (stats != null)
                {
                    seen++;
                    if (stats.unlocked) unlocked++;
                }
                page.Cards.Add(BuildLineageCard(lineage, stats, openLineage));
            }

            page.Body = L10n.T("已解锁血脉", "Bloodlines unlocked") + " " + unlocked + " / " + lineages.Count
                + L10n.T(" · 遇到过 ", " · Encountered ") + seen
                + (openLineage != null ? L10n.T(" · 点格子看详情", " · Click a card for details") : string.Empty);

            AppendTamingAchievements(page);
            AppendMemorialCards(page, museum);
            return page;
        }

        private static PetNestCardData BuildLineageCard(PetNestLineageInfo lineage, PetNestLineageStats stats, Action<string> openLineage)
        {
            // 网格格子：Title = 名字，Subtitle = 第一行数字（击杀 · 孵化），Body = 第二行（元素 · 最高等级 · 远征 · 异色）。
            // card.Shiny 只给格子描一圈金边，**不挂流光**：标题是白字血脉名，流光挂上去看不出（2026-09-23 复核第 12 项）。
            PetNestCardData card = new PetNestCardData();
            card.Key = lineage.LineageKey;
            card.Icon = ResolveLineagePortrait(lineage.LineageKey);
            if (stats == null)
            {
                // 没遇到过：剪影 + 问号，不剧透名字（图鉴口径）；没有可看的详情，不可点
                card.Title = L10n.T("？？？", "???");
                card.IconLocked = true;
                card.Subtitle = L10n.T("未遇到", "Not encountered");
                card.Body = L10n.T("打倒它就会登记", "Defeat it to log it");
                return card;
            }

            card.Title = lineage.DisplayName;
            card.Shiny = stats.shinyHatched > 0;
            card.IconLocked = !stats.unlocked;
            string key = lineage.LineageKey;
            if (openLineage != null) card.OnCardClick = delegate { openLineage(key); };
            if (!stats.unlocked)
            {
                card.Subtitle = L10n.T("未解锁", "Locked") + " · " + L10n.T("击杀", "Kills") + " " + stats.kills;
                card.Body = L10n.T("孵化一只就解锁", "Hatch one to unlock");
                return card;
            }
            card.Subtitle = L10n.T("击杀", "Kills") + " " + stats.kills
                + " · " + L10n.T("孵化", "Hatched") + " " + stats.hatched;
            card.Body = DescribeElement(lineage.Element)
                + " · " + L10n.T("最高 Lv", "Best Lv") + stats.maxLevel
                + " · " + L10n.T("远征", "Trips") + " " + stats.expeditions
                + (stats.shinyHatched > 0
                    ? " · <color=" + PetNestChroma.ShinyTextHex + ">★" + stats.shinyHatched + "</color>"
                    : string.Empty);
            return card;
        }

        /// <summary>
        /// 血脉详情（博物馆浮层卡）：元素、契合的远征目的地、巢里几只、遗魂进度、战绩；未解锁的写怎么解锁。
        /// 复用巢页右栏的数据形状（PetNestDetailData），画法在 PetNestUIMuseumDetail.cs。
        /// </summary>
        internal static PetNestDetailData BuildLineageDetail(string lineageKey)
        {
            PetNestDetailData detail = new PetNestDetailData();
            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(lineageKey, out lineage) || lineage == null)
            {
                detail.Title = L10n.T("找不到这条血脉", "Bloodline not found");
                return detail;
            }
            PetNestLineageStats stats = null;
            List<PetNestLineageStats> all = PetNestPersistenceAccess.Museum.lineages;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].lineageKey, lineageKey, StringComparison.Ordinal)) stats = all[i];
            }
            bool unlocked = stats != null && stats.unlocked;

            detail.Icon = ResolveLineagePortrait(lineageKey);
            detail.Title = lineage.DisplayName;
            detail.Shiny = stats != null && stats.shinyHatched > 0;
            detail.Subtitle = DescribeElement(lineage.Element) + L10n.T(" 系", "")
                + " · " + (unlocked ? L10n.T("已解锁", "Unlocked") : L10n.T("未解锁", "Locked"));

            if (!unlocked)
            {
                detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("怎么解锁", "How to unlock"),
                    L10n.T("孵化一只这条血脉的崽。打倒它有机会掉遗种蛋，每次击杀都会攒下它的遗魂，攒够 "
                            + PetNestTuning.SoulsPerCondensedEgg + " 就能在孵化页凝一枚。",
                        "Hatch one cub of this bloodline. Defeating it may drop a relic egg, and every kill banks its souls; "
                            + PetNestTuning.SoulsPerCondensedEgg + " condense an egg on the Hatch tab.")));
            }

            int souls = PetNestService.GetSouls(lineageKey);
            detail.Sections.Add(new KeyValuePair<string, string>(T("SoulLedger"),
                souls + " / " + PetNestTuning.SoulsPerCondensedEgg
                + (souls >= PetNestTuning.SoulsPerCondensedEgg
                    ? L10n.T("，够凝一枚蛋了", ", enough for an egg")
                    : L10n.T("，还差 " + (PetNestTuning.SoulsPerCondensedEgg - souls), ", " + (PetNestTuning.SoulsPerCondensedEgg - souls) + " to go"))));

            string fits = string.Empty;
            PetNestDestinationInfo[] destinations = PetNestExpeditionService.Destinations;
            for (int d = 0; d < destinations.Length; d++)
            {
                if (!PetNestLineageCatalog.DestinationAcceptsElement(destinations[d].Id, lineage.Element)) continue;
                if (fits.Length > 0) fits += L10n.T("、", ", ");
                fits += PetNestLocalization.DescribeDestination(destinations[d].Id);
            }
            if (fits.Length > 0)
            {
                detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("远征契合", "Expedition match"),
                    fits + L10n.T("（成功率 +", " (+") + FormatPercent(PetNestTuning.ElementAffinityBonus)
                    + L10n.T("）", " success)")));
            }

            int inNest = 0;
            string names = string.Empty;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null || !string.Equals(pet.lineageKey, lineageKey, StringComparison.Ordinal)) continue;
                inNest++;
                if (inNest <= 4) names += (names.Length > 0 ? L10n.T("、", ", ") : string.Empty) + PetNestService.GetDecoratedPetName(pet);
            }
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("巢里", "In the nest"),
                inNest == 0
                    ? L10n.T("一只都没有", "None")
                    : inNest + L10n.T(" 只：", ": ") + names + (inNest > 4 ? L10n.T(" 等", " and more") : string.Empty)));

            if (stats != null)
            {
                detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("记录", "Record"),
                    L10n.T("击杀 ", "Kills ") + stats.kills
                    + L10n.T(" · 孵化 ", " · Hatched ") + stats.hatched
                    + L10n.T(" · 异色 ", " · Shiny ") + stats.shinyHatched
                    + L10n.T(" · 最高 Lv", " · Best Lv") + stats.maxLevel
                    + L10n.T(" · 远征 ", " · Trips ") + stats.expeditions));
            }
            return detail;
        }

        /// <summary>驯养成就（E-3）：一行一个，已达成写「已达成」，没达成写条件；隐藏成就达成前只写「？？？」。</summary>
        private static void AppendTamingAchievements(PetNestPageContent page)
        {
            PetNestSection section = new PetNestSection();
            section.Title = L10n.T("驯养成就", "Taming achievements");
            int done = 0;
            for (int i = 0; i < TamingAchievementIds.Length; i++)
            {
                BossRushAchievementDef def;
                bool unlocked;
                try
                {
                    def = BossRushAchievementManager.GetAchievement(TamingAchievementIds[i]);
                    unlocked = BossRushAchievementManager.IsUnlocked(TamingAchievementIds[i]);
                }
                catch (Exception)
                {
                    continue;
                }
                if (def == null) continue;
                if (unlocked) done++;
                bool chinese = L10n.IsChinese;
                PetNestCardData row = new PetNestCardData();
                row.Key = def.id;
                row.Title = unlocked || !def.isHidden ? (chinese ? def.nameCN : def.nameEN) : L10n.T("？？？", "???");
                row.Subtitle = unlocked ? L10n.T("已达成", "Unlocked") : L10n.T("未达成", "Locked");
                row.Body = unlocked || !def.isHidden ? (chinese ? def.descCN : def.descEN) : L10n.T("达成后揭晓", "Revealed when unlocked");
                // 已达成的格子描金边（与异色同一个稀有口径），未达成的保持素色；只描边，标题不挂流光
                row.Shiny = unlocked;
                row.ShinyFrameOnly = true;
                section.Items.Add(row);
            }
            if (section.Items.Count == 0) return;
            section.Caption = done + " / " + section.Items.Count;
            page.Sections.Add(section);
        }

        /// <summary>纪念碑：一行一位，名字 + 血脉、生涯与日期 + 去向、档位、死亡率。旧写法是一长串「·」拼成的单行字。</summary>
        private static void AppendMemorialCards(PetNestPageContent page, PetNestMuseumData museum)
        {
            if (museum.memorials.Count == 0 && museum.mergedMemorialCount == 0) return;

            PetNestSection memorial = new PetNestSection();
            memorial.Title = T("Page_Memorial");
            for (int i = museum.memorials.Count - 1; i >= 0; i--)
            {
                PetNestMemorialEntry m = museum.memorials[i];
                if (m == null) continue;
                // 碑文一定要刻风险档位：那是玩家自己按下的选择。
                PetNestCardData row = new PetNestCardData();
                row.Body = PetNestLocalization.DescribeDestination(m.destinationId)
                    + " · " + DescribeRisk(m.riskTier)
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(m.deathRate);
                row.Subtitle = LineageName(m.lineageKey) + " · " + L10n.T("生涯", "Career") + " " + m.careerCount
                    + DescribeMemorialDate(m.deathTicks);
                row.IsDanger = true;
                // 名字同样要带炫彩 / 异色：崽已经被移除，碑上这一行是这份颜色**唯一**的去处。
                try
                {
                    row.Title = PetNestChroma.Decorate(m.shiny, m.chromaA, m.chromaB, m.displayName, L10n.IsChinese);
                }
                catch (Exception)
                {
                    row.Title = m.displayName;
                }
                memorial.Items.Add(row);
            }

            if (museum.mergedMemorialCount > 0)
            {
                memorial.Caption = L10n.T("碑林（更早的名字）", "Older names in the grove")
                    + " " + museum.mergedMemorialCount;
            }
            page.Sections.Add(memorial);
        }

        /// <summary>「 · 2026-10-17」；没有时间（旧档）时不写。</summary>
        private static string DescribeMemorialDate(long deathTicks)
        {
            if (deathTicks <= 0L) return string.Empty;
            try
            {
                return " · " + new DateTime(deathTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd");
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        #endregion
    }
}
