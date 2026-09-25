using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>Jeff 一次性入门任务；六章和天空岛主线保持各自原有任务身份。</summary>
    internal static class CampaignGuideTable
    {
        internal const int QuestIdBase = 590200;
        internal const int FirstQuestId = 590201;
        internal const int LastQuestId = 590214;
        internal const string ModeG = "mode_g";
        internal const string ModeH = "mode_h";
        internal const string PetNest = "pet_nest";
        internal const string RandomEvents = "random_events";
        internal const string SkyIslandGear = "sky_island_gear";
        internal const string ModeD = "mode_d";
        internal const string ModeE = "mode_e";
        internal const string ModeF = "mode_f";
        internal const string Zombie = "zombie";
        internal const string Garden = "garden";
        internal const string Trophy = "trophy";
        internal const string AffixForge = "affix_forge";
        internal const string Reforge = "reforge";
        internal const string DailyReport = "daily_report";

        internal sealed class Definition
        {
            internal string Id;
            internal int QuestId;
            internal string NameKey;
            internal string DescriptionKey;
            internal string NameCN;
            internal string NameEN;
            internal string HintCN;
            internal string HintEN;
        }

        private static readonly Definition[] _definitions = new Definition[]
        {
            Make(ModeG, 1, "ModeG", "带上你的老伙计", "Bring Your Own Gear",
                "带一张船票和宿命回响信物，穿上你的装备，再从基地码头出发。进图选好契约，开始一次九波挑战就算入门；回来和我说说第一道回声。",
                "Take a ticket and an Echo of Fate token to the base dock with your usual gear. Confirm a contract after arriving and start the nine-wave challenge. Come back and tell me about its first echo."),
            Make(ModeH, 2, "ModeH", "这回坐在看台上", "Your Turn in the Stands",
                "到基地码头选黑市鸭王杯，带一张船票就够。自己挑两位斗士，先比较双方装备和属性再押注，开始一场比赛。钱和装备输掉就没了，第一次押少一点。",
                "Choose the Black Market Duck King Cup at the base dock and bring one ticket. Pick two fighters, compare both sides and their equipment, then place a small bet and start a match. Lost stakes are gone."),
            Make(PetNest, 3, "PetNest", "给小家伙留个窝", "Room for a Cub",
                "击败 Boss 有机会捡到遗种蛋。回基地建遗种巢，把蛋送去孵化；巢里有一只崽，或者带它出战，就回来让我见识见识。",
                "Bosses can drop relic eggs. Build a Pet Nest at base and hatch one. Once you have a cub in the nest, or take one into a raid, come tell me about it."),
            Make(RandomEvents, 4, "RandomEvents", "战场不照剧本走", "Expect a Surprise",
                "去打一局普通 BossRush 或其它支持随机事件的模式。战场出现一次随机事件后回来找我；先读提示，别把意外当故障。丧尸模式不出这些事件。",
                "Play ordinary BossRush or another mode with random events. Experience one event, read its hint, then return. Zombie mode uses its own events and does not count."),
            Make(SkyIslandGear, 5, "SkyIslandGear", "带一件云上的纪念", "Something from the Clouds",
                "先做云上的坐标，把天空岛航线开出来。挑战岛上的 Boss，拿到一件专属装备，放进背包或穿上。带着它回基地找我，别只在岛上转一圈。",
                "Finish Coordinates Above the Clouds to unlock Sky Island. Defeat an island boss and take a piece of its exclusive gear. Bring it back in your backpack or wear it when you return to me."),
            Make(ModeD, 6, "ModeD", "空手也能开张", "Start with Empty Hands",
                "把装备、背包和宠物包清空，只带一张 BossRush 船票从码头出发。别夹带别的模式信物。白手起家正式开始后，试试把捡到的东西变成下一波的本钱。",
                "Empty your equipment, backpack and pet bag. Take only a BossRush ticket from the dock, without other mode tokens. Start Bare Hands and turn scavenged gear into your next wave of progress."),
            Make(ModeE, 7, "ModeE", "先认清自己人", "Choose Your Side",
                "从基地商人买一面营旗，卸下装备再出发。营旗决定你的阵营；划地为营正式开始就算体验。先看队伍再开枪，同阵营的别误伤。",
                "Buy a faction flag from the base vendor, unequip your gear, then depart. The flag chooses your faction. Start Territory and learn who is on your side before shooting."),
            Make(ModeF, 8, "ModeF", "借来的时间", "Borrowed Time",
                "脱下装备，带船票和血猎收发器出发，不要带营旗。血猎追击正式开始就算体验。这里一直在失血，击败 Boss 才能续命，别站着发呆。",
                "Unequip your gear and depart with a ticket and a Blood Hunt receiver, without a faction flag. Start Blood Hunt. Your health keeps draining here; defeating bosses buys time."),
            Make(Zombie, 9, "Zombie", "听见尸潮了吗", "Hear the Horde",
                "在基地商人处买尸潮邀请函，使用后选图。入场物品会送回仓库；选好初始流派并开始战斗，再来找我。开局不投现金也能玩。",
                "Buy and use a Horde Invitation from the base vendor. Your gear goes to storage on entry. Choose a starting build and begin fighting, then return to me. Investing cash is optional."),
            Make(Garden, 10, "Garden", "把种子种下去", "A Place for Seeds",
                "推进鸭王征程，拿到菜地解锁后，在后山工地建成菜地。起步种子会送进背包，种下后等它成熟。后山收成优先进背包，放不下再入仓，满仓的去马蜂自提点拿。",
                "Progress the campaign to unlock the garden, then finish construction at the backyard site. Starter seeds go to your backpack. Plant them and wait for ripening. Backyard harvest goes to your backpack first, then storage; collect overflow at Package Pickup."),
            Make(Trophy, 11, "Trophy", "留一件给自己看", "Keep a Trophy",
                "拿到陈列设施解锁后，把一件 Mod 战利品摆上官方展示架或假人。摆好后回到我这里，让这些战利品有个落脚的地方。",
                "Unlock the display facilities through the campaign. Put a Mod trophy on an official display rack or mannequin, then return to me. Give your victories a place at home."),
            Make(AffixForge, 12, "AffixForge", "给装备一点脾气", "Give Gear Some Character",
                "找哥布林打开词缀锻造，给一件支持的装备锻出一个词缀。把装备放进背包或穿上再来找我，空词缀槽可不算。锻造会花钱，先看价钱。",
                "Visit the goblin and forge an affix on eligible gear. Keep the gear in your backpack or wear it, then return; an empty affix slot does not count. Check the price before forging."),
            Make(Reforge, 13, "Reforge", "旧装备也能再试试", "Give Old Gear a Chance",
                "在哥布林的重铸页选一件装备，先看属性和费用，再做一次重铸。带着留有重铸记录的装备回来找我；数值有升有降，不必为了这件事追满值。",
                "Select gear in the goblin reforge screen, review its attributes and cost, then reforge it. Bring gear with a reforge record back to me. Results can improve or worsen; no need to chase perfect rolls."),
            Make(DailyReport, 14, "DailyReport", "看看今天的报纸", "Read the Daily Paper",
                "在基地建邮箱，打开日报，签一次到。顺手看看当天悬赏和你的战斗记录，领完再回来找我。",
                "Build the mailbox at base, open the daily report and sign in once. Check the current bounty and your combat record, then come back."),
        };

        private static Definition Make(string id, int order, string suffix, string nameCN, string nameEN, string cn, string en)
        {
            return new Definition { Id = id, QuestId = QuestIdBase + order,
                NameKey = "BossRush_Campaign_Guide_" + suffix + "_Name",
                DescriptionKey = "BossRush_Campaign_Guide_" + suffix + "_Description",
                NameCN = nameCN, NameEN = nameEN, HintCN = cn, HintEN = en };
        }

        internal static IList<Definition> Definitions { get { return _definitions; } }
        internal static Definition Find(string id)
        {
            for (int i = 0; i < _definitions.Length; i++)
                if (string.Equals(_definitions[i].Id, id, StringComparison.Ordinal)) return _definitions[i];
            return null;
        }
        internal static bool IsCompleted(string id) { return CampaignPersistence.IsGuideCompleted(id); }
        internal static bool IsAccepted(string id) { return CampaignPersistence.IsGuideAccepted(id); }
        internal static bool IsExperienced(string id) { return CampaignPersistence.IsGuideExperienced(id); }
        internal static bool InBase()
        {
            return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel && !SceneLoader.IsSceneLoading;
        }
        internal static string Describe(Definition definition, bool done)
        {
            if (definition == null) return string.Empty;
            return L10n.T(definition.HintCN, definition.HintEN)
                + (done ? L10n.T("\n体验完成，回基地找杰夫交付。", "\nTrial complete. Return to Jeff at base.") : string.Empty);
        }
    }
}
