using System;
using BossRush;
using Saves;

internal static class SkyIslandMarriageTextRegression
{
    internal static void Run(Action<bool,string> check)
    {
        SavesSystem.Switch(100917);
        var story = new SkyIslandStoryService(); story.Open();
        int route = (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered | SkyIslandStoryFlag.RouteUnlocked);
        SkyIslandStoryFlag[] stages = { SkyIslandStoryFlag.None, SkyIslandStoryFlag.BeaconQuestAccepted,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.WindBeacon,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.Ending,
        };
        foreach(bool cn in new[]{true,false})
        foreach(bool married in new[]{false,true})
        foreach(bool island in new[]{false,true})
        {
            L10n.IsChinese=cn;
            for(int planting=0;planting<3;planting++)
            {
                story.Current.flags=route | (planting>0?(int)SkyIslandStoryFlag.PlantingRecord:0) |
                    (planting>1?(int)SkyIslandStoryFlag.PlantingDelivered:0);
                string before=SkyIslandStoryCodec.Encode(story.Current);
                string line=story.DescribeNpc("sky_qinghe",married,island);
                check(!string.IsNullOrEmpty(line) && (cn || !Cjk(line)),"Qinghe context has current-language text");
                check(planting!=1 || !line.Contains(cn?"路过帮我找找":"Look for the muddy"),"found record is never described as missing");
                check(planting!=1 || line.Contains(cn?"委托板":"Market board"),"unreturned record has a reachable fallback");
                check(island || !line.Contains(cn?"我就下锅":"I'll start cooking"),"base spouse never promises to cook at island dock");
                check(island || line.Contains(cn?"岛上":"island"),"base guidance points back to island");
                check(before==SkyIslandStoryCodec.Encode(story.Current),"Qinghe dialogue does not mutate progress");
            }
            for(int stage=0;stage<stages.Length;stage++)
            {
                story.Current.flags=route | (int)stages[stage];
                string before=SkyIslandStoryCodec.Encode(story.Current);
                string line=story.DescribeNpc("sky_weibai",married,island);
                check(!string.IsNullOrEmpty(line) && (cn || !Cjk(line)),"Weibai context has current-language text");
                check(stage!=5 || line.Contains(cn?"还没交":"Turn in the beacon quest first"),"both repaired: hand-in before next quest");
                check(stage!=4 || line.Contains(cn?"还没接":"haven't taken"),"late acceptance after repairs is explained");
                check(stage!=6 || line.Contains(cn?"钟庭之争":"Bell Court Standoff"),"after hand-in: Fuzhou's next quest is named");
                check(island || line.Contains(cn?"回岛接交":"on the island"),"home does not claim to accept island quests");
                check(before==SkyIslandStoryCodec.Encode(story.Current),"Weibai dialogue does not mutate progress");
            }
            story.Current.flags=route;
            string fuzhou=story.DescribeNpc("sky_fuzhou",false,true,true);
            check(fuzhou.Contains(cn?"委托板":"Market board") && !fuzhou.Contains(cn?"沿桥去风铃集":"Follow the bridge"),"Fuzhou directs player to board when Weibai is away");
        }
        L10n.IsChinese=true;
        story.Current.flags=route;
        story.Current.discoveredNotes = new[]{"Letter_02","Letter_03"};
        check(story.DescribeNpc("sky_qinghe",true,false).Contains("没署名的信"),"home spouse keeps collected-letter conversation");
        check(story.DescribeNpc("sky_weibai",true,false).Contains("苇生的信"),"home spouse keeps Weisheng's letter conversation");
        story.Close();
    }
    private static bool Cjk(string text)
    {
        foreach(char c in text) if((c>=0x4e00&&c<=0x9fff)||(c>=0x3000&&c<=0x303f)||(c>=0xff00&&c<=0xffef)) return true;
        return false;
    }
}
