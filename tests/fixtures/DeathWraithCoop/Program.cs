using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem.Data;
using Saves;
using UnityEngine;

internal static class Program
{
    private static void Check(bool value,string label) { if(!value)throw new Exception(label); }
    private static WraithInfo Record(uint raid,string name="owner")
    { return new WraithInfo { valid=true,raidID=raid,subSceneID="scene-a",playerName=name,itemTreeData=new ItemTreeData(),playerMaxHealth=123,
        hasPlayerFaceData=true,playerFaceData=new CustomFaceSettingData { Eye=7 } }; }
    public static int Main()
    {
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="travel-initial";Duckov.Scenes.MultiSceneCore.Instance=new object();
        var host=new DeathWraithRuntimeModule();host.Start();
        var tomb=new Vector3(12,3,45);
        for(int i=0;i<16;i++)
        {
            string player="player-"+i,asset="asset-"+i;
            BossRushCoop.Players[player]=new BossRushCoopPlayer { Id=player,AssetId=asset };
            host.Receive(player,asset,Record(7),tomb);host.Receive(player,asset,Record(7),tomb);
        }
        host.OnUpdate(0.1f,0.1f);
        Check(host.Actors==16 && host.SpawnCount==16,"16 owners with identical raid each keep one independent wraith; duplicate same owner record does not clone");
        for(uint raid=8;raid<12;raid++)host.Receive("player-0","asset-0",Record(raid),tomb);
        host.OnUpdate(0.1f,0.1f);
        Check(host.Actors==20 && host.SpawnCount==20,"one owner retains all raid-bound wraiths allowed by original personal records");
        CharacterMainControl actor=host.Actor("player-0","asset-0",7);
        Check(actor.Ai<0 && actor.transform.position.x==12 && actor.transform.position.z==45,"world uses actual tomb coordinate and accepts negative native AI identity");
        host.Receive("player-0","asset-new",Record(13),tomb);
        host.Receive("player-0","asset-0",Record(14),tomb,0);
        Check(host.Actors==20,"wrong asset and stale generation requests cannot create a new world wraith");
        host.Receive("player-0","asset-0",Record(7,"replacement"),tomb);host.OnUpdate(0.1f,0.1f);
        Check(host.SpawnCount==21,"same raid with a distinct death snapshot is a new record, not a duplicate");
        SavesSystem.Seed(0,new List<WraithInfo> { Record(7,"host-private") });
        SavesSystem.Seed(1,new List<WraithInfo> { Record(7),Record(8) });
        SavesSystem.Seed(2,new List<WraithInfo> { Record(7,"other") });
        int reads=SavesSystem.Reads,writes=SavesSystem.Writes;
        Check(host.Killed(actor) && host.Killed(actor) && host.Packets==0,"native kill marks only the current actor and never queues persistent record deletion");
        host.Receive("player-0","asset-0",Record(7),tomb);host.OnUpdate(.1f,.1f);
        Check(host.SpawnCount==21,"dead same-generation owner asset raid fingerprint cannot respawn from a repeated tomb request");
        Check(SavesSystem.At(1).Count==2 && SavesSystem.At(0).Count==1 && SavesSystem.At(2).Count==1 && SavesSystem.Reads==reads && SavesSystem.Writes==writes,"kill preserves every owner's stored records with zero personal store IO");
        var guest=new DeathWraithRuntimeModule();guest.Start();guest.SelectSlot(2,"asset-other");
        Check(!guest.LegacyKillReceipt("asset-0",7) && SavesSystem.Reads==reads,"legacy killed receipt does not touch another slot");
        guest.SelectSlot(1,"asset-0");SavesSystem.FailRead=SavesSystem.FailWrite=true;
        Check(guest.LegacyKillReceipt("asset-0",7) && SavesSystem.At(1).Count==2,"legacy in-flight killed receipt is a zero-IO acknowledgement, never a deletion");
        SavesSystem.FailRead=SavesSystem.FailWrite=false;
        // 完整装备快照大于一片，乱序/重复仍只生成一名亡魂。
        WraithInfo large=Record(20);large.itemTreeData.entries.Add(new ItemTreeData.Entry { typeID=500001,Variable=new string('X',25000) });
        host.Receive("player-0","asset-0",large,tomb,-1,new[] {2,0,2,1});host.OnUpdate(0.1f,0.1f);
        Check(host.SpawnCount==22,"out-of-order large snapshot reassembles once with full equipment payload");
        // 全 owner 只有一个发送预算，真实墓箱事件只排队，不制造客户端实体。
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="player-0";guest.SelectSlot(1,"asset-0");
        SavesSystem.Seed(1,new List<WraithInfo> { large,Record(21) });guest.SelectSlot(1,"asset-0");
        guest.Queue(20,tomb);guest.Queue(21,tomb);BossRushCoop.Sent.Clear();Time.unscaledTime=1;
        guest.SendTick();guest.SendTick();Check(BossRushCoop.Sent.Count==1 && guest.SpawnCount==0,"multiple tombs obey owner-wide one-piece send budget; guest never spawns authoritative AI");
        string stableRun=BossRushCoop.RunId;BossRushCoop.RunId="";BossRushCoop.SessionReady=false;guest.OnUpdate(0.1f,0.1f);
        BossRushCoop.RunId=stableRun;BossRushCoop.SessionReady=true;guest.OnUpdate(0.1f,0.1f);
        Check(guest.Queued==2,"same room handshake retains partially sent personal tomb requests");
        BossRushCoop.LocalAssetId="asset-new";Time.unscaledTime=2;guest.SendTick();Check(BossRushCoop.Sent.Count==1,"queued old-slot snapshot does not resend as new-slot owner");
        guest.Cleanup();Check(guest.Destroyed==0,"client scene cleanup preserves native AI replica ownership");
        // 让 emoji 的高代理恰好位于旧 12000 字符分片的末尾，逐包 UTF-8 往返后仍应重建。
        var unicode=Record(22,"玩家🙂");unicode.itemTreeData.entries.Add(new ItemTreeData.Entry { typeID=500001,Variable="BOUNDARY" });
        string markerJson=BossRushCoopJson.ToJson(unicode);int markerAt=markerJson.IndexOf("BOUNDARY",StringComparison.Ordinal);
        Check(markerAt>=0 && markerAt<11999,"boundary record setup");
        string variable=new string('U',11999-markerAt)+"🙂"+new string('Z',12500);
        unicode.itemTreeData.entries[0].Variable=variable;
        string unicodeJson=BossRushCoopJson.ToJson(unicode);Check(char.IsHighSurrogate(unicodeJson[11999]) && char.IsLowSurrogate(unicodeJson[12000]),"fixture uses literal surrogate pair across original split boundary");
        var unicodeGuest=new DeathWraithRuntimeModule();unicodeGuest.Start();unicodeGuest.SelectSlot(1,"asset-0");
        SavesSystem.Seed(1,new List<WraithInfo> { unicode });unicodeGuest.Queue(22,tomb);BossRushCoop.Sent.Clear();
        for(int i=0;i<3;i++) { Time.unscaledTime=4+i*0.06f;unicodeGuest.SendTick(); }
        Check(BossRushCoop.Sent.Count==3,"unicode snapshot produces three throttled fragments");
        object[] wire=BossRushCoop.Sent.ToArray();BossRushCoop.IsHost=true;
        foreach(object part in wire)host.ReceiveSentPart("player-0",part);host.OnUpdate(0.1f,0.1f);
        Check(host.SpawnCount==23 && host.SnapshotVariable(22)==variable,"UTF-8 transport preserves split-boundary emoji, fingerprint and full equipment variable");
        int worlds=host.Actors, destroyed=host.Destroyed;
        host.OnSetFile_DeathWraith();
        Check(host.Actors==worlds && host.Destroyed==destroyed,"host personal slot switch retains every accepted world record from other owners");
        CharacterMainControl first=host.Actor("player-0","asset-0",22);
        BossRushCoop.IsHost=false;unicodeGuest.ReceiveWorld(host);
        Check(CoopAiPredictionHealthProtection.Marked.Contains(first) && !first.dropBoxOnDead,"late join attaches original wraith presentation and prediction protection");
        int names=BossRush.Utils.NPCNameTagHelper.Named.Count;unicodeGuest.ReceiveWorld(host);
        Check(BossRush.Utils.NPCNameTagHelper.Named.Count==names,"unchanged native replicas bind presentation only once");
        first.Destroyed=true;first.Health.Destroyed=true;first.gameObject.Destroyed=true;
        var replaced=new CharacterMainControl { Ai=first.Ai };BossRushCoop.Ai[first.Ai]=replaced;
        unicodeGuest.BindTick();
        Check(CoopAiPredictionHealthProtection.Marked.Contains(replaced) && !replaced.dropBoxOnDead
            && BossRush.Utils.NPCNameTagHelper.Named.Contains(replaced.transform),"same native AI identity with replacement body reattaches name, ownership and prediction protection");
        unicodeGuest.Cleanup();Check(!replaced.Destroyed,"guest cleanup preserves native replacement ownership");
        var entrance=new DeathWraithRuntimeModule();entrance.Start();entrance.SelectSlot(6,"asset-0");
        WraithInfo firstRecord=Record(31,"first tomb"),secondRecord=Record(32,"second tomb");
        firstRecord.itemTreeData.entries.Add(new ItemTreeData.Entry {typeID=500001,Variable="first equipment"});
        secondRecord.itemTreeData.entries.Add(new ItemTreeData.Entry {typeID=500002,Variable="second equipment"});
        SavesSystem.Seed(6,new List<WraithInfo> {firstRecord,secondRecord});
        Duckov.Utilities.GameplayDataSettings.Prefabs=new Duckov.Utilities.GameplayDataSettings.Settings {LootBoxPrefab_Tomb=new InteractableLootbox()};
        var prefab=Duckov.Utilities.GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb;
        foreach(uint raid in new uint[] {31,32})entrance.NotifyOriginalDeadBodySpawnRequested_DeathWraith(new Duckov.DeadBodyManager.DeathInfo {valid=true,raidID=raid,subSceneID="scene-a",worldPosition=tomb});
        var box=new InteractableLootbox();box.transform.position=tomb;
        entrance.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(box,null,tomb,new InteractableLootbox());Check(entrance.Queued==0,"ordinary corpse creation cannot consume the personal tomb contexts");
        entrance.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(box,null,tomb,prefab);entrance.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(box,null,tomb,prefab);
        entrance.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(box,null,tomb,prefab);Check(entrance.Queued==2,"two original tomb callbacks at the same coordinate retain two distinct raid requests");
        BossRushCoop.Sent.Clear();Time.unscaledTime=10;entrance.OnUpdate(.1f,.1f);Time.unscaledTime+=.06f;entrance.OnUpdate(.1f,.1f);
        Check(BossRushCoop.Sent.Count==2&&entrance.SpawnCount==0,"production tomb entry reaches actual SendToHost for every personal record");
        wire=BossRushCoop.Sent.ToArray();BossRushCoop.IsHost=true;foreach(object part in wire)host.ReceiveSentPart("player-0",part);host.OnUpdate(.1f,.1f);
        Check(host.Actor("player-0","asset-0",31)!=null&&host.Actor("player-0","asset-0",32)!=null
            &&host.SnapshotVariable(31)=="first equipment"&&host.SnapshotVariable(32)=="second equipment","shared tomb coordinate creates separate actors and retains each complete equipment snapshot");
        // The native new Core can load while the host's scene generation still names its predecessor.
        var blocked=new DeathWraithRuntimeModule();blocked.Start();blocked.SelectSlot(6,"asset-0");
        BossRushCoop.IsHost=false;BossRushCoop.HasBoundScene=false;
        var activeAi=host.Actor("player-0","asset-0",31).Ai;var unboundBody=new CharacterMainControl {Ai=activeAi};BossRushCoop.Ai[activeAi]=unboundBody;
        blocked.ReceiveWorld(host);
        Check(!CoopAiPredictionHealthProtection.Marked.Contains(unboundBody) && unboundBody.dropBoxOnDead,"unbound Core cannot consume old-generation wraith AI projection");
        BossRushCoop.HasBoundScene=true;blocked.OnUpdate(.1f,.1f);
        Check(CoopAiPredictionHealthProtection.Marked.Contains(unboundBody),"bound Core consumes retained wraith projection without another packet");
        blocked.Queue(31,tomb);int beforeSend=BossRushCoop.Sent.Count;Time.unscaledTime+=2;
        BossRushCoop.HasBoundScene=false;blocked.SendTick();
        Check(BossRushCoop.Sent.Count==beforeSend && blocked.Queued==1,"unbound Core retains personal tomb request without sending previous scene generation");
        BossRushCoop.HasBoundScene=true;blocked.SendTick();
        Check(BossRushCoop.Sent.Count==beforeSend+1 && blocked.Queued==1,"bound Core retries original personal tomb request with full record");
        blocked.Cleanup();
        BossRushCoop.IsHost=true;var emptyHost=new DeathWraithRuntimeModule();emptyHost.Start();int beforeWorld=BossRushCoop.Sent.Count;
        BossRushCoop.HasBoundScene=false;emptyHost.OnUpdate(.1f,.1f);Check(BossRushCoop.Sent.Count==beforeWorld,"unbound host cannot publish wraith world facts under preceding scene generation");
        BossRushCoop.HasBoundScene=true;host.Receive("player-0","asset-0",Record(33),tomb);int spawnCount=host.SpawnCount;
        BossRushCoop.HasBoundScene=false;host.OnUpdate(.1f,.1f);
        Check(host.SpawnCount==spawnCount && !host.CurrentSpawn(33),"unbound host cannot spawn or accept wraith await continuation for prior scene");
        BossRushCoop.HasBoundScene=true;host.OnUpdate(.1f,.1f);
        Check(host.SpawnCount==spawnCount+1 && host.CurrentSpawn(33),"host retains all wraith entries and retries spawning after scene binding");
        Check(host.Killed(host.Actor("player-0","asset-0",31)) && host.Packets==0,"real wraith death keeps original personal record and queues no clear");
        BossRushCoop.IsHost=false;BossRushCoop.HasBoundScene=false;
        Check(SavesSystem.At(6).Count==2 && SavesSystem.At(6).Exists(r=>r.raidID==31) && SavesSystem.At(6).Exists(r=>r.raidID==32),"unbound scene retains every stored record after a native wraith kill");
        BossRushCoop.HasBoundScene=true;
        // Same public generation, but a different actual native travel must not inherit a cached projection.
        string previousToken=BossRushCoop.SceneToken;BossRushCoop.SceneToken="next-native-travel";Duckov.Scenes.MultiSceneCore.Instance=new object();
        BossRushCoop.IsHost=true;
        Check(!host.CurrentSpawn(33),"wraith await entry cannot cross native travel at unchanged public generation");
        BossRushCoop.IsHost=false;
        var nextTravel=new DeathWraithRuntimeModule();nextTravel.Start();nextTravel.SelectSlot(6,"asset-0");
        int markedBefore=CoopAiPredictionHealthProtection.Marked.Count;nextTravel.ReceiveWorld(host);
        Check(CoopAiPredictionHealthProtection.Marked.Count==markedBefore,"old wraith world token is rejected even when public generation is unchanged and Core is bound");
        BossRushCoop.IsHost=true;var freshHost=new DeathWraithRuntimeModule();freshHost.Start();
        foreach(object oldPart in wire)freshHost.ReceiveSentPart("player-0",oldPart);
        Check(freshHost.Actors==0,"old native-travel fragments cannot enter a new wraith assembly at the same public generation");
        freshHost.Receive("player-0","asset-0",Record(34),tomb);freshHost.OnUpdate(.1f,.1f);
        BossRushCoop.IsHost=false;nextTravel.ReceiveWorld(freshHost);
        Check(freshHost.Actors==1 && CoopAiPredictionHealthProtection.Marked.Count==markedBefore+1,"current native-travel wraith world retains all valid current records");
        var early=new DeathWraithRuntimeModule();early.Start();early.SelectSlot(6,"asset-0");BossRushCoop.SceneToken=null;early.Queue(32,tomb);
        BossRushCoop.SceneToken="late-observed-token";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;early.OnUpdate(.1f,.1f);
        Check(early.Queued==1 && BossRushCoop.Sent.Count==1,"tomb callback before token observation is retained only for its original actual Core");
        object earlyWire=BossRushCoop.Sent[0];BossRushCoop.IsHost=true;var earlyHost=new DeathWraithRuntimeModule();earlyHost.Start();earlyHost.ReceiveSentPart("player-0",earlyWire);
        Check(earlyHost.Actors==1,"late token adoption reaches actual host assembly as the original valid record");BossRushCoop.IsHost=false;
        var obsoleteQueue=new DeathWraithRuntimeModule();obsoleteQueue.Start();BossRushCoop.SceneToken=null;obsoleteQueue.Queue(32,tomb);
        Duckov.Scenes.MultiSceneCore.Instance=new object();BossRushCoop.SceneToken="another-native-travel";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;obsoleteQueue.OnUpdate(.1f,.1f);
        Check(obsoleteQueue.Queued==0 && BossRushCoop.Sent.Count==0 && SavesSystem.At(6).Exists(r=>r.raidID==32),"early-token queue cannot migrate across Core replacement and original personal record remains intact");
        var rejoined=new DeathWraithRuntimeModule();rejoined.Start();rejoined.SelectSlot(6,"asset-0");rejoined.OnUpdate(.1f,.1f);
        BossRushCoop.RunId="new-host-room";BossRushCoop.SceneToken=null;Duckov.Scenes.MultiSceneCore.Instance=new object();rejoined.Queue(32,tomb);
        BossRushCoop.SceneToken="new-room-late-token";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;rejoined.OnUpdate(.1f,.1f);
        Check(rejoined.Queued==1 && BossRushCoop.Sent.Count==1,"new-room tomb queued before token observation retains its exact Core and every original record");
        var oldRoom=new DeathWraithRuntimeModule();oldRoom.Start();oldRoom.SelectSlot(6,"asset-0");oldRoom.Queue(32,tomb);
        BossRushCoop.RunId="later-host-room";BossRushCoop.SceneToken="later-room-token";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;oldRoom.OnUpdate(.1f,.1f);
        Check(oldRoom.Queued==0 && BossRushCoop.Sent.Count==0 && SavesSystem.At(6).Exists(r=>r.raidID==32),"old-room tomb cannot inherit another host room even when the native Core reference survives");
        var welcomeQueue=new DeathWraithRuntimeModule();welcomeQueue.Start();welcomeQueue.SelectSlot(6,"asset-0");BossRushCoop.RunId="";BossRushCoop.SceneToken=null;welcomeQueue.Queue(32,tomb);
        BossRushCoop.RunId="welcome-bound-room";BossRushCoop.SceneToken="welcome-bound-token";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;welcomeQueue.OnUpdate(.1f,.1f);
        Check(welcomeQueue.Queued==1 && BossRushCoop.Sent.Count==1,"empty welcome room adopts the actual original Core room without dropping the tomb record");
        BossRushCoop.RunId="following-room-same-core";BossRushCoop.Sent.Clear();Time.unscaledTime+=2;welcomeQueue.OnUpdate(.1f,.1f);
        Check(welcomeQueue.Queued==0 && BossRushCoop.Sent.Count==0,"room adopted after welcome remains frozen against a later room on the same actual Core");
        BossRushCoop.IsHost=true;var lifetime=new DeathWraithRuntimeModule();lifetime.Start();
        lifetime.Receive("player-0","asset-0",Record(777),tomb);lifetime.OnUpdate(.1f,.1f);var killed=lifetime.Actor("player-0","asset-0",777);
        lifetime.Killed(killed);lifetime.Receive("player-0","asset-0",Record(777),tomb);lifetime.OnUpdate(.1f,.1f);
        Check(lifetime.SpawnCount==1 && lifetime.Packets==0,"owner raid fingerprint remains a single dead tombstone for this generation");
        BossRushCoop.SceneGeneration++;BossRushCoop.SceneToken="next-lifetime-travel";lifetime.OnUpdate(.1f,.1f);
        lifetime.Receive("player-0","asset-0",Record(777),tomb);lifetime.OnUpdate(.1f,.1f);
        Check(lifetime.SpawnCount==2 && lifetime.Actor("player-0","asset-0",777)!=killed,"new native travel regenerates the original still-valid record");
        lifetime.SelectSlot(6,"asset-0");lifetime.Touch(31);lifetime.FlushDeathWraithListIfDirty_DeathWraith();
        Check(!SavesSystem.At(6).Exists(r=>r.raidID==31) && SavesSystem.At(6).Exists(r=>r.raidID==32),"opening official tomb still removes only its corresponding stored record");
        Console.WriteLine("PASS DeathWraithCoop: 16 owners/multiple raids, native id/tomb position, fragmented UTF-8 snapshots, persistent records retained after kills, official tomb deletion, slot/generation isolation");return 0;
    }
}
