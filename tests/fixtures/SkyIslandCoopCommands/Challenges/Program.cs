using System;using System.Linq;using BossRush;using UnityEngine;
internal static class Program {
 static int checks;static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static BossRushCoop.Sent Last(string topic){return BossRushCoop.Outbox.Last(p=>p.Topic==topic);}
 static void Role(string player){BossRushCoop.LocalPlayerId=player;BossRushCoop.LocalAssetId=player+"-asset";BossRushCoop.IsHost=player=="host";CharacterMainControl.Main=BossRushCoop.FindPlayer(player).Character;}
 static void StartTravel(SkyIslandCoopRuntime travel){var value=new SkyIslandCoopRuntime.Travel {Id="trip",Generation=1};BossRushCoop.Handlers[BossRushCoop.LocalPlayerId+":sky.travel.start"](new BossRushCoopMessage{IsFromHost=true,Json=BossRushCoopJson.ToJson(value)});}
 static int Main(){
  foreach(string id in new[]{"host","guest","other"}){BossRushCoop.Players.Add(new BossRushCoopPlayer{Id=id,AssetId=id+"-asset",Character=new CharacterMainControl()});var story=new SkyIslandStoryService();story.Current.flags=(int)(SkyIslandStoryFlag.WindBeacon|SkyIslandStoryFlag.StarLamp);SkyIslandOfficialQuestStory.Stores[id+"-asset"]=story;}
  Role("guest");var guestStory=SkyIslandOfficialQuestStory.Stores["guest-asset"];var guestOwner=new ModBehaviour();var guestTravel=new SkyIslandCoopRuntime(guestOwner);StartTravel(guestTravel);var client=new SkyIslandSession(guestStory);guestOwner.Session=client;client.Wire();
  Role("other");var otherTravel=new SkyIslandCoopRuntime(new ModBehaviour());StartTravel(otherTravel);
  Role("host");var hostStory=SkyIslandOfficialQuestStory.Stores["host-asset"];hostStory.Current.flags|=(int)(SkyIslandStoryFlag.ZhelingDefeated|SkyIslandStoryFlag.BellKeeperDefeated|SkyIslandStoryFlag.StormSlain);
  var hostOwner=new ModBehaviour();var hostTravel=new SkyIslandCoopRuntime(hostOwner);BossRushCoop.IsHost=false;StartTravel(hostTravel);BossRushCoop.IsHost=true;var server=new SkyIslandSession(hostStory);hostOwner.Session=server;server.Wire();
  server.encounters.Tick();Check(!server.encounters.Cleared("Zheling"),"host old save must not preclear another player's manual encounter");
  foreach(string id in new[]{"Zheling","BellKeeper","Storm"}){
   Role("guest");Check(client.Request(id),"client can queue "+id);string original=client.RequestId;Check(!client.Request(id),"same pending interaction cannot replace request identity");client.Send();var request=Last("sky.challenge.request");
   Role("host");BossRushCoop.Emit(request,false);BossRushCoop.Emit(request,false);Check(server.encounters.Spawns==Array.IndexOf(new[]{"Zheling","BellKeeper","Storm"},id)+1,"duplicate actual challenge request never respawns group");
   server.ResidentsForTest();Check(id!="Zheling"||!server.ResidentVisible,"actual resident visibility statement hides Zheling only during combat");
   var reply=Last("BossRush.Personal.sky.echo");Role("guest");BossRushCoop.Emit(reply,true);Check(client.StatusText=="The challenge begins"&&!client.StatusError,"only host acceptance announces a started challenge");
   Role("host");server.encounters.Kill(id);server.ResidentsForTest();Check(server.ResidentVisible,"actual cleared-group visibility restores next player challenge entry");var completion=Last("BossRush.Personal.sky.challenge.complete");Check(completion.Target=="guest","completion freezes challenger recipient");
   Role("other");completion.Target="other";BossRushCoop.Emit(completion,true);Check(!SkyIslandOfficialQuestStory.Stores["other-asset"].Current.ZhelingResolved,"other party member cannot receive initiator's ending");completion.Target="guest";
   Role("guest");int before=guestStory.Applies;BossRushCoop.Emit(completion,true);BossRushCoop.Emit(completion,true);Check(guestStory.Applies==before+1,"real story rules accept original outcome exactly once");
   Check(hostStory.Applies==0,"host old ending is not newly credited from guest kill");
  }
  Role("host");int count=server.encounters.Spawns;
  var next=new SkyIslandSession.CoopChallenge{Id="Zheling",TravelId="trip",SceneToken=BossRushCoop.SceneToken,AssetId="other-asset",RequestId="next-owner"};
  BossRushCoop.Handlers["host:sky.challenge.request"](new BossRushCoopMessage{SenderId="other",Json=BossRushCoopJson.ToJson(next)});
  Check(server.encounters.Spawns==count+1,"another participant can start the same completed group in the same trip at full count");
  string owner,asset,rid;Check(server.encounters.TryGetChallengeOwner("Zheling",out owner,out asset,out rid)&&owner=="other"&&asset=="other-asset"&&rid=="next-owner","reopened group freezes new exact owner and request");
  Role("guest");Check(client.Request("Zheling"),"new client request identity allocated after prior completion");client.Send();var denied=Last("sky.challenge.request");Role("host");BossRushCoop.Emit(denied,false);var refusal=Last("BossRush.Personal.sky.echo");Role("guest");BossRushCoop.Emit(refusal,true);
  Check(client.StatusError&&client.StatusText.Contains("Challenge not started")&&!client.StatusText.Contains("Echo"),"named challenge rejection is acknowledged with named wording");
  Role("host");var missing=BossRushCoop.FindPlayer("other");BossRushCoop.Players.Remove(missing);server.encounters.Kill("Zheling");int sent=BossRushCoop.Outbox.Count(p=>p.Target=="other");hostTravel.Tick();Check(BossRushCoop.Outbox.Count(p=>p.Target=="other")==sent,"disconnected owner ending stays pending rather than sent to another player");
  BossRushCoop.Players.Add(missing);BossRushCoop.SceneGeneration=3;hostOwner.Session=null;hostTravel.Tick();var lateCompletion=Last("BossRush.Personal.sky.challenge.complete");
  Role("other");BossRushCoop.LocalAssetId="other-slot";BossRushCoop.Emit(lateCompletion,true);Check(!SkyIslandOfficialQuestStory.Stores["other-asset"].Current.ZhelingResolved,"wrong slot cannot receive original owner ending");
  BossRushCoop.LocalAssetId="other-asset";var otherStory=SkyIslandOfficialQuestStory.Stores["other-asset"];otherStory.CanWrite=false;BossRushCoop.Emit(lateCompletion,true);Check(!otherStory.Current.ZhelingResolved,"write barrier keeps lateCompletion unpaid");
  otherStory.CanWrite=true;BossRushCoop.Emit(lateCompletion,true);Check(otherStory.Current.ZhelingResolved,"earned ending survives scene generation change and write retry on original asset");
  Role("host");BossRushCoop.IsConnected=false;var soloStory=new SkyIslandStoryService();var solo=new SkyIslandSession(soloStory);solo.encounters.BeginChallenge("Zheling");solo.encounters.Kill("Zheling");Check(soloStory.Current.ZhelingResolved,"single player preserves original local ending");
  Console.WriteLine("PASS SkyIslandChallengeOwnership: "+checks+" real handlers, real story rules and real personal delivery checks");return 0;
 }
}
