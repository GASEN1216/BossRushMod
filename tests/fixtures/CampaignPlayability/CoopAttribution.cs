using System;
using System.Linq;
using BossRush;

internal static partial class CoopFactsRegression
{
    private static void AttributionAndAdmission()
    {
        var module = Setup("ch2", host: true);
        var owner = ModBehaviour.Instance;
        CharacterMainControl guest = CharacterMainControl.Main;
        guest.IsMainCharacter = false;
        var hostBody = new CharacterMainControl { Team = Teams.player, Health = new Health(), IsMainCharacter = true };
        var other = new CharacterMainControl { Team = Teams.player, Health = new Health() };
        var spectator = new CharacterMainControl { Team = Teams.player, Health = new Health() };
        CharacterMainControl.Main = hostBody;
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="host", AssetId="host-asset", IsConnected=true, IsAlive=true, Character=hostBody });
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="guest", AssetId="asset-A", IsConnected=true, IsAlive=true, Character=guest });
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="other", AssetId="asset-C", IsConnected=true, IsAlive=true, Character=other });
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="spectator", AssetId="asset-S", IsConnected=true, IsAlive=true, Character=spectator });
        owner.AdmitCampaignForTest("host", "host-asset"); owner.AdmitCampaignForTest("other", "asset-C");
        BossRushCoop.IsHost=true; BossRushCoop.LocalPlayerId="host"; BossRushCoop.LocalAssetId="host-asset";
        string originalAsset;
        check(owner.TryGetCampaignParticipantAssetForRuntime(CampaignContentCatalog.ModeModeD,"original-scope","guest",out originalAsset)
            && originalAsset=="asset-A" && BossRushCoop.GetPlayerId(guest)=="guest" && BossRushCoop.IsPlayer(guest),
            "fixture configures actual original mode cohort and guest body");
        int victimId=8000;
        Action<CharacterMainControl,string> kill = (actor, expected) =>
        {
            int before=CampaignFactNetwork.Sent.Count;
            var victim=new CharacterMainControl { InstanceId=++victimId, Team=Teams.wolf, isBossCharacter=true };
            CampaignObjectiveCollector.OnGlobalDead(new Health { Character=victim }, new DamageInfo { fromCharacter=actor, fromWeaponItemID=1 });
            var sent=CampaignFactNetwork.Sent.Skip(before).Where(frame=>frame.Topic=="BossRush.Personal.Campaign").ToArray();
            check(expected==null ? sent.Length==0 : sent.Length==1 && sent[0].Player==expected,
                "actual global death emits exactly one owner fact or none: " + (expected??"unqualified")
                + "; actual=" + string.Join(",", sent.Select(frame=>frame.Player)));
            if(expected!=null)
            {
                var receipt=BossRushCoopJson.FromJson<CoopPersonalReceipt>(sent[0].Json);
                var fact=BossRushCoopJson.FromJson<CoopCampaignFact>(receipt.Payload);
                check(fact.Kind=="kill" && fact.Melee && fact.Boss && receipt.TargetAssetId==BossRushCoop.FindPlayer(expected).AssetId,
                    "actual collector preserves weapon/boss facts and original recipient asset");
                CampaignObjectiveCollector.OnGlobalDead(new Health { Character=victim }, new DamageInfo { fromCharacter=actor, fromWeaponItemID=1 });
                check(CampaignFactNetwork.Sent.Count==before+1, "duplicate death cannot generate a second personal reward fact");
            }
        };
        kill(guest,"guest"); kill(other,"other"); kill(hostBody,"host"); kill(spectator,null);
        kill(new CharacterMainControl { Team=Teams.player },null);
        var frost=new CharacterMainControl { Team=Teams.player };
        FrostmourneAction.Owners[frost]=guest; kill(frost,"guest");
        var staff=new CharacterMainControl { Team=Teams.player };
        SummonStaffAction.Owners[staff]=other; kill(staff,"other");
        var pet=new CharacterMainControl { Team=Teams.player, PetAgent=new PetNestCompanionAgent { Master=guest } };
        kill(pet,"guest"); pet.PetAgent.Master=spectator; kill(pet,null);
        var hired=new CharacterMainControl { Team=Teams.player }; owner.HiredOwners[hired]=other; kill(hired,"other");
        BossRushCoop.FindPlayer("guest").IsAlive=false; kill(frost,null);
        BossRushCoop.FindPlayer("guest").IsAlive=true;
        BossRushCoop.FindPlayer("guest").AssetId="new-unadmitted-slot"; kill(guest,null); kill(frost,null);
        BossRushCoop.FindPlayer("guest").AssetId="asset-A";
        BossRushCoop.FindPlayer("other").IsConnected=false;
        BossRushCoop.FindPlayer("guest").IsAlive=false;
        int waveBefore=CampaignFactNetwork.Sent.Count;
        check(module.DeliverForTest("admitted-wave",new CoopCampaignFact { Kind="wave",Mode=CampaignContentCatalog.ModeModeD,Wave=3 }),
            "real team wave facts are issued from original admitted cohort");
        var waveFrames=CampaignFactNetwork.Sent.Skip(waveBefore).Where(frame=>frame.Topic=="BossRush.Personal.Campaign").ToArray();
        check(waveFrames.Length==2 && waveFrames.All(frame=>frame.Player!="spectator"),
            "online admitted owners including dead teammate receive team wave while spectator gets none");
        BossRushCoop.FindPlayer("guest").IsAlive=true;
        BossRushCoop.FindPlayer("other").AssetId="new-slot-C";
        BossRushCoop.FindPlayer("other").IsConnected=true;
        module.FactsTickForTest();
        var oldOther = CampaignFactNetwork.Sent.Last(frame=>frame.Player=="other" && frame.Json.Contains("admitted-wave"));
        check(BossRushCoopJson.FromJson<CoopPersonalReceipt>(oldOther.Json).TargetAssetId=="asset-C",
            "disconnected admitted owner's earned team fact retains original slot through reconnection");
        BossRushCoop.FindPlayer("host").IsLocal=true;
        owner.TickCampaignModeBridge(0f);
        var hostKills=CampaignObjectiveTracker.Progress.First(item=>item.Def.Kind==CampaignObjectiveKind.MeleeKills);
        int beforeLocal=hostKills.Current, beforeLocalWire=CampaignFactNetwork.Sent.Count;
        var localVictim=new CharacterMainControl { InstanceId=++victimId, Team=Teams.wolf, isBossCharacter=true };
        CampaignObjectiveCollector.OnGlobalDead(new Health { Character=localVictim },new DamageInfo { fromCharacter=hostBody,fromWeaponItemID=1 });
        check(hostKills.Current==beforeLocal+1 && CampaignFactNetwork.Sent.Count==beforeLocalWire,
            "actual host local delivery updates its own original tracker without broadcasting the kill");
        CampaignObjectiveCollector.OnGlobalDead(new Health { Character=localVictim },new DamageInfo { fromCharacter=hostBody,fromWeaponItemID=1 });
        check(hostKills.Current==beforeLocal+1,"actual local host repeated death is idempotent");
        owner.SetCampaignEntryForTest("new-scope");
        string asset;
        check(!owner.TryGetCampaignParticipantAssetForRuntime(CampaignContentCatalog.ModeModeD,"original-scope","guest",out asset),
            "production mode admission getter rejects previous entry epoch");
        module.FactsCleanupForTest();

        module=Setup("ch3"); owner=ModBehaviour.Instance;
        BossRushCoop.LocalPlayerId="spectator"; BossRushCoop.LocalAssetId="not-admitted";
        owner.TickCampaignModeBridge(600f);
        check(!CampaignObjectiveTracker.IsArmed && CampaignObjectiveTracker.Progress.Count==0,
            "spectator cannot earn local survival time from shared active mode flags");
        owner.AdmitCampaignForTest("spectator","not-admitted");
        owner.TickCampaignModeBridge(.5f);
        check(CampaignObjectiveTracker.IsArmed && CampaignObjectiveTracker.Progress[0].Current==0,
            "actual later admission starts personal tracking without backfilling spectator time");
        module.FactsCleanupForTest();
        FrostmourneAction.Owners.Clear(); SummonStaffAction.Owners.Clear();
    }
}
