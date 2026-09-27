// ============================================================================
// CampaignRewardTable.cs - 杰夫任务的奖励物品、引导奖金与提交物品（纯数据）
// ============================================================================
// 2026-09-27 owner：「奖励不单单是钱，适当插入一些要提交物品的任务」，授权自行定。
//
// 【引导奖励的串法】每条引导的奖励 = 下一条要用的入场物品，玩家接着做不必先去商人那儿买：
//   宿命回响 → 船票 ×2（鸭王杯要 1 张）；鸭王杯 → 遗种蛋（遗种巢不再靠 4% 掉率卡住）；
//   白手起家 → 营旗；划地为营 → 血猎收发器 + 船票；血猎追击 → 尸潮邀请函；
//   丧尸 → 词缀熔石 ×2；词缀 → 冷淬液 ×2（重铸锁属性用）。
//   菜地 / 陈列 / 天空岛装备三条可能被前置跳过，所以只把奖励放在一定不会被跳过的那几条上，
//   让「上回给你的……」的文案始终成立。现金在 3000–10000 之间，14 条合计 74000，
//   远低于六章契约奖金（合计 480000），不改变主线的收入结构。
//
// 【提交物品】
//   - 第 2 章「种地的选手」：交 2 个后山收成（三种任选）——章节名就是种地，收成要浇水、20 分钟成熟，
//     让菜地到手后真的种一轮；本章奖励回三种种子各 2 颗，交出去的收成转回下一轮种子。
//   - 引导「带一件云上的纪念」：交 5 片残铜片（天空岛矿脉常见材料），给岛上装备那条补一个带东西回来的理由。
//   交的物品都只认背包，交付时一次收走；序章的航向仪原本就是提交物，不动。
//
// 【章节奖励物品】六章奖金不变，另加一份与下一段玩法相关的物品。
// 回退：把对应条目改成 null / 0 即回到只发钱；数据不进存档，改表不影响旧档。
// 无 Unity / Duckov 依赖：tests/fixtures/JeffQuestFlow 直接执行。
// ============================================================================

namespace BossRush
{
    internal static class CampaignRewardTable
    {
        internal sealed class Grant
        {
            internal int Cash;
            internal OfficialQuestItemStack[] Items;
            internal OfficialQuestSubmission[] Submissions;
        }

        private static readonly Grant None = new Grant();

        private static OfficialQuestItemStack Item(int typeId, int count) { return new OfficialQuestItemStack(typeId, count); }

        private static Grant Pays(int cash, params OfficialQuestItemStack[] items) { return new Grant { Cash = cash, Items = items }; }

        internal static readonly int[] BackMountainHarvests =
            { BossRushItemIds.DragonFruit, BossRushItemIds.EmberChili, BossRushItemIds.PhantomMushroom };

        /// <summary>引导的奖金、奖励物与提交物。未列出的返回空（不发、不收）。</summary>
        internal static Grant ForGuide(string guideId)
        {
            switch (guideId)
            {
                case CampaignGuideTable.ModeG: return Pays(3000, Item(BossRushItemIds.BossRushTicket, 2));
                case CampaignGuideTable.ModeH: return Pays(3000, Item(BossRushItemIds.RelicEgg, 1));
                case CampaignGuideTable.PetNest: return Pays(3000, Item(BossRushItemIds.BossRushTicket, 1));
                case CampaignGuideTable.RandomEvents: return Pays(5000, Item(BossRushItemIds.BossRushTicket, 1));
                case CampaignGuideTable.SkyIslandGear:
                    Grant gear = Pays(10000);
                    gear.Submissions = new[]
                    {
                        new OfficialQuestSubmission
                        {
                            TypeIds = new[] { BossRushItemIds.SkyIslandBrassScrap }, Count = 5,
                            Description = () => L10n.T("交给杰夫：残铜片 ×5", "Give Jeff: Brass Scrap x5"),
                        },
                    };
                    return gear;
                case CampaignGuideTable.ModeD: return Pays(5000, Item(FactionFlagConfig.RANDOM_FLAG_TYPE_ID, 1));
                case CampaignGuideTable.ModeE:
                    return Pays(5000, Item(BloodhuntTransponderConfig.TYPE_ID, 1), Item(BossRushItemIds.BossRushTicket, 1));
                case CampaignGuideTable.ModeF: return Pays(3000, Item(BossRushItemIds.ZombieTideInvitation, 1));
                case CampaignGuideTable.Zombie: return Pays(5000, Item(BossRushItemIds.AffixForgeStone, 2));
                case CampaignGuideTable.Garden:
                    return Pays(3000, Item(BossRushItemIds.DragonSeed, 1), Item(BossRushItemIds.EmberSeed, 1), Item(BossRushItemIds.PhantomSpore, 1));
                case CampaignGuideTable.Trophy:
                    return Pays(6000, Item(BossRushItemIds.DragonFruit, 1), Item(BossRushItemIds.EmberChili, 1), Item(BossRushItemIds.PhantomMushroom, 1));
                case CampaignGuideTable.AffixForge: return Pays(5000, Item(ColdQuenchFluidConfig.TYPE_ID, 2));
                case CampaignGuideTable.Reforge: return Pays(8000);
                case CampaignGuideTable.DailyReport: return Pays(10000, Item(BossRushItemIds.RelicEgg, 1));
                default: return None;
            }
        }

        /// <summary>章节奖励物品（奖金仍来自章节数据 RewardCash）。越界返回 null。</summary>
        internal static OfficialQuestItemStack[] ChapterItems(int order)
        {
            switch (order)
            {
                case 1: return new[] { Item(BossRushItemIds.BossRushTicket, 2) };
                case 2: return new[] { Item(BossRushItemIds.DragonSeed, 2), Item(BossRushItemIds.EmberSeed, 2), Item(BossRushItemIds.PhantomSpore, 2) };
                case 3: return new[] { Item(BossRushItemIds.AffixForgeStone, 2) };
                case 4: return new[] { Item(ColdQuenchFluidConfig.TYPE_ID, 2), Item(BloodhuntTransponderConfig.TYPE_ID, 1) };
                case 5: return new[] { Item(BossRushItemIds.ZombieTideInvitation, 1), Item(BossRushItemIds.PortableSafeZoneDevice, 1) };
                case 6: return new[] { Item(BossRushItemIds.RelicEgg, 2), Item(BossRushItemIds.AffixForgeStone, 3) };
                default: return null;
            }
        }

        /// <summary>章节交付时要交的物品。越界或不需要返回 null。</summary>
        internal static OfficialQuestSubmission[] ChapterSubmissions(int order)
        {
            if (order != 2) return null;
            return new[]
            {
                new OfficialQuestSubmission
                {
                    TypeIds = BackMountainHarvests, Count = 2,
                    Description = () => L10n.T("交给杰夫：后山收成 ×2（龙息果、焚心椒、幽影蘑菇任选）",
                        "Give Jeff: 2 backyard harvests (Dragonbreath Fruit, Emberheart Chili or Umbral Mushroom)"),
                },
            };
        }
    }
}
