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
        Check(host.Killed(actor) && host.Killed(actor) && host.Packets==1,"native kill is idempotent and produces one original-owner record clear");
        Check(host.PacketOwner(0)=="player-0","kill routes to original record owner irrespective of killer");
        SavesSystem.Seed(0,new List<WraithInfo> { Record(7,"host-private") });
        SavesSystem.Seed(1,new List<WraithInfo> { Record(7),Record(8) });
        SavesSystem.Seed(2,new List<WraithInfo> { Record(7,"other") });
        var guest=new DeathWraithRuntimeModule();guest.Start();guest.SelectSlot(2,"asset-other");
        int reads=SavesSystem.Reads;Check(!guest.ApplyPacket(host,0) && SavesSystem.Reads==reads,"wrong-slot receipt does not even open personal record store");
        guest.SelectSlot(1,"asset-0");SavesSystem.FailRead=true;
        Check(!guest.ApplyPacket(host,0),"personal read failure retains clear obligation");SavesSystem.FailRead=false;Time.realtimeSinceStartup=2;
        SavesSystem.FailWrite=true;Check(!guest.ApplyPacket(host,0),"personal cache write failure cannot acknowledge record removal");
        Check(SavesSystem.At(1).Count==2,"failed cache save leaves durable personal list unchanged");
        SavesSystem.FailWrite=false;Time.realtimeSinceStartup=4;
        Check(guest.ApplyPacket(host,0) && guest.ApplyPacket(host,0),"retry and duplicate clear succeed once after write barrier opens");
        Check(SavesSystem.At(1).Count==1 && SavesSystem.At(1)[0].raidID==8 && SavesSystem.At(0).Count==1 && SavesSystem.At(2).Count==1,"only addressed original owner's killed record removed; other raids and all other slots retained");
        // 同 raid 新快照不能被旧回执清掉。
        var changed=Record(7,"new-death");SavesSystem.Seed(3,new List<WraithInfo> { changed });guest.SelectSlot(3,"asset-0");
        Check(guest.ApplyPacket(host,0) && SavesSystem.At(3).Count==1,"late receipt does not remove a replacement record with a different fingerprint");
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
        Console.WriteLine("PASS DeathWraithCoop: 16 owners/multiple raids, native id/tomb position, fragmented UTF-8 snapshots, private clear retry, slot/generation isolation");return 0;
    }
}
