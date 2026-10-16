using System;
using System.Linq;
using System.Reflection;
using BossRush;
using UnityEngine;

partial class Program
{
 static string Id(){return Guid.NewGuid().ToString("N");}
 static long HelloSequence(BossRushCoopRuntimeModule r){return (long)typeof(BossRushCoopRuntimeModule).GetField("helloSequence",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r);}
 static BossRushCoopReady LastReady(BossRushCoopTransport t){return t.Sent.Select(BossRushCoopJson.FromJson<BossRushCoopEnvelope>).Where(x=>x.topic=="$ready").Select(x=>BossRushCoopJson.FromJson<BossRushCoopReady>(x.json)).Last();}
 static BossRushCoopReadyMember Member(BossRushCoopPlayer p){return new BossRushCoopReadyMember{id=p.Id,assetId=p.AssetId,assetRevision=p.AssetRevision,readyEpoch=p.ReadyEpoch};}
 static void ReadyAssetIdentity()
 {
  Saves.SavesSystem.CurrentSlot=1;
  var host=Start(true);var native=BossRushCoopTransport.Current;
  var coldPeer=new object();BossRushCoopTransport.NativePeers.Add(coldPeer);native.Packet(coldPeer,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=Id(),assetRevision=1});native.Sent.Clear();
  host.MarkSceneReady("before-core");Check(ReadyPackets(native)==0&&!host.AllPlayersSceneReady("before-core")&&!host.ArePlayersSceneReady("before-core",new[]{host.LocalId}),"host without actual Core cannot publish or query readiness");
  host.OnDestroy();host=Start(true);native=BossRushCoopTransport.Current;
  Scene(501);host.OnUpdate(0,0);host.MarkSceneReady("bound");Check(host.AllPlayersSceneReady("bound"),"actual bound host can mark its scene ready");
  var actualCore=Duckov.Scenes.MultiSceneCore.Instance;Duckov.Scenes.MultiSceneCore.Instance=null;native.Sent.Clear();host.MarkSceneReady("gap");Check(ReadyPackets(native)==0&&!host.AllPlayersSceneReady("bound")&&!host.ArePlayersSceneReady("bound",new[]{host.LocalId}),"host Core gap invalidates both existing barrier queries and new readiness");Duckov.Scenes.MultiSceneCore.Instance=actualCore;
  object peer=new object();BossRushCoopTransport.NativePeers.Add(peer);string a=Id(),b=Id(),key="GroundZero:G:paid-A:wave:1";
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=a,assetRevision=1});var guest=host.Players.Single(x=>!x.IsLocal);string token=guest.ReconnectToken,epochA=guest.ReadyEpoch;
  var welcome=native.Sent.Select(BossRushCoopJson.FromJson<BossRushCoopEnvelope>).Where(x=>x.topic=="$welcome").Select(x=>BossRushCoopJson.FromJson<BossRushCoopWelcome>(x.json)).Last();
  Check(welcome.assetId==a&&welcome.assetRevision==1&&welcome.readyEpoch==epochA&&!string.IsNullOrEmpty(epochA),"host welcome actually returns current asset revision and assigned epoch");
  host.MarkSceneReady(key);var oldReady=ReadyFor(host,peer,key);native.Packet(peer,"$ready",oldReady,host.SessionId);Check(host.AllPlayersSceneReady(key),"current authenticated asset and epoch completes actual barrier");
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=2,manifest="same",assetId=b,assetRevision=2});Check(!host.AllPlayersSceneReady(key)&&guest.ReadyEpoch!=epochA,"new remote asset retires previous barrier and rotates epoch");
  native.Packet(peer,"$ready",oldReady,host.SessionId);Check(!host.AllPlayersSceneReady(key),"in-flight old-asset ready cannot certify new asset");
  var wrong=ReadyFor(host,peer,key);wrong.assetRevision=1;native.Packet(peer,"$ready",wrong,host.SessionId);Check(!host.AllPlayersSceneReady(key),"current asset with old revision cannot ready");
  wrong=ReadyFor(host,peer,key);wrong.readyEpoch=epochA;native.Packet(peer,"$ready",wrong,host.SessionId);Check(!host.AllPlayersSceneReady(key),"current asset revision with old epoch cannot ready");
  native.Packet(peer,"$ready",ReadyFor(host,peer,key),host.SessionId);Check(host.AllPlayersSceneReady(key),"new asset can explicitly prepare after valid handshake");
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=3,manifest="same",assetId=a,assetRevision=3});Check(!host.AllPlayersSceneReady(key)&&guest.ReadyEpoch!=epochA,"A to B to A has a new monotonic incarnation");
  native.Packet(peer,"$ready",oldReady,host.SessionId);Check(!host.AllPlayersSceneReady(key),"first A packet cannot revive A after a slot roundtrip");
  wrong=ReadyFor(host,peer,key);wrong.readyEpoch=epochA;native.Packet(peer,"$ready",wrong,host.SessionId);Check(!host.AllPlayersSceneReady(key),"forged latest A revision still needs current host epoch");
  native.Packet(peer,"$ready",ReadyFor(host,peer,key),host.SessionId);Check(host.AllPlayersSceneReady(key),"returned A can really prepare again");
  string epoch3=guest.ReadyEpoch;native.Packet(peer,"$hello",new BossRushCoopHello{sequence=4,manifest="same",assetId=b,assetRevision=2});Check(guest.AssetId==a&&guest.AssetRevision==3&&guest.ReadyEpoch==epoch3,"new hello sequence cannot roll asset revision backward");
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=5,manifest="same",assetId=b,assetRevision=3});Check(guest.AssetId==a&&guest.ReadyEpoch==epoch3,"same revision cannot be reused for another asset");
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=6,manifest="same",assetId=a,assetRevision=3});Check(guest.ReadyEpoch==epoch3&&host.AllPlayersSceneReady(key),"periodic same-identity hello preserves actual preparation");
  var snapshot=LastReady(native);Check(snapshot.revision>0&&snapshot.members.Length==2&&snapshot.members.Any(x=>x.id==guest.Id&&x.assetId==a&&x.assetRevision==3&&x.readyEpoch==epoch3),"actual complete ready sender codec preserves all nested member identities");
  var beforeReconnect=ReadyFor(host,peer,key);int gen=host.Generation;BossRushCoopTransport.NativePeers.Remove(peer);Time.unscaledTime=1;host.OnUpdate(1,1);Check(!guest.IsConnected&&guest.ReadyEpoch==null&&!host.ArePlayersSceneReady(key,new[]{host.LocalId,guest.Id}),"actual native disconnect retires epoch and required member");
  object next=new object();BossRushCoopTransport.NativePeers.Add(next);native.Packet(next,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=a,assetRevision=3,reconnectToken=token});Check(guest.IsConnected&&guest.Peer==next&&guest.ReadyEpoch!=epoch3&&host.Generation==gen&&!host.AllPlayersSceneReady(key),"same generation reconnect assigns a fresh readiness epoch");
  native.Packet(peer,"$ready",beforeReconnect,host.SessionId);native.Packet(next,"$ready",beforeReconnect,host.SessionId);Check(!host.AllPlayersSceneReady(key),"old physical peer and replay via new peer both fail readiness");
  native.Packet(next,"$ready",ReadyFor(host,next,key),host.SessionId);Check(host.AllPlayersSceneReady(key),"new connection can explicitly ready without forcing same-Core reload");
  long oldHostRevision=LocalRevision(host);string oldHostAsset=host.GetLocalAssetId();Saves.SavesSystem.Select(2);Check(host.GetLocalAssetId()!=oldHostAsset&&LocalRevision(host)>oldHostRevision&&!host.AllPlayersSceneReady(key),"local host asset change clears its previous readiness");host.MarkSceneReady(key);Check(host.AllPlayersSceneReady(key),"new host asset can explicitly prepare its bound actual Core");host.OnDestroy();

  Saves.SavesSystem.CurrentSlot=1;var client=Start(false);native=BossRushCoopTransport.Current;Scene(601);object upstream=new object();BossRushCoopTransport.NativeHost=upstream;BossRushCoopTransport.NativePeers.Add(upstream);
  string localA=client.GetLocalAssetId(),hostAsset=Id(),hostEpoch=Id(),clientEpoch=Id();long revA=LocalRevision(client);
  var acceptedWelcome=new BossRushCoopWelcome{helloSequence=1,session="ready-room",playerId="me",generation=1,assetId=localA,assetRevision=revA,readyEpoch=clientEpoch};
  var badWelcome=new BossRushCoopWelcome{helloSequence=1,session="ready-room",playerId="me",generation=1,assetId=Id(),assetRevision=revA,readyEpoch=clientEpoch};native.Packet(upstream,"$welcome",badWelcome);Check(client.SessionId==string.Empty,"wrong-asset welcome cannot complete handshake");
  badWelcome.assetId=localA;badWelcome.assetRevision=revA+1;native.Packet(upstream,"$welcome",badWelcome);Check(client.SessionId==string.Empty,"wrong-revision welcome cannot consume current hello sequence");native.Packet(upstream,"$welcome",acceptedWelcome);
  var entries=new[]{new BossRushCoopRosterEntry{id="me",nativeId="native-local",assetId=localA,assetRevision=revA,readyEpoch=clientEpoch,connected=true},new BossRushCoopRosterEntry{id="host",assetId=hostAsset,assetRevision=1,readyEpoch=hostEpoch,connected=true}};
  long rosterRevision=0;Action roster=()=>native.Packet(upstream,"$roster",new BossRushCoopRoster{generation=1,sceneId="GroundZero",revision=++rosterRevision,ready=true,players=entries},client.SessionId);roster();
  Check(client.HasBoundScene&&client.Ready,"actual current asset handshake and target-Core roster bind client");native.Sent.Clear();client.MarkSceneReady(key);var sentReady=LastReady(native);Check(sentReady.assetId==localA&&sentReady.assetRevision==revA&&sentReady.readyEpoch==clientEpoch,"actual client sender includes current welcome identity");
  Func<BossRushCoopReadyMember[]> currentMembers=()=>client.Players.Select(Member).ToArray();Action<long,BossRushCoopReadyMember[]> apply=(rev,members)=>native.Packet(upstream,"$ready",new BossRushCoopReady{generation=1,key=key,revision=rev,members=members},client.SessionId);
  var initialMembers=currentMembers();apply(10,initialMembers);Check(client.AllPlayersSceneReady(key),"real host-authenticated complete nested snapshot completes barrier");
  apply(11,new BossRushCoopReadyMember[0]);Check(!client.AllPlayersSceneReady(key),"empty complete snapshot withdraws all previous members");apply(10,initialMembers);Check(!client.AllPlayersSceneReady(key),"out-of-order old complete snapshot cannot re-add members");apply(11,initialMembers);Check(!client.AllPlayersSceneReady(key),"same snapshot revision cannot restore removed membership");
  var badMembers=currentMembers();badMembers.Single(x=>x.id=="me").readyEpoch=Id();apply(12,badMembers);Check(!client.AllPlayersSceneReady(key),"wrong local member epoch is excluded from complete snapshot");apply(13,currentMembers());Check(client.AllPlayersSceneReady(key),"new valid complete snapshot restores current members");
  entries[1].assetId=Id();entries[1].assetRevision=2;entries[1].readyEpoch=Id();roster();Check(!client.AllPlayersSceneReady(key),"authoritative remote asset change removes old barrier member");apply(14,initialMembers);Check(!client.AllPlayersSceneReady(key),"higher ready snapshot revision still cannot certify stale member asset");apply(15,currentMembers());Check(client.AllPlayersSceneReady(key),"current remote identity snapshot really restores preparation");
  entries[1].connected=false;roster();Check(!client.ArePlayersSceneReady(key,new[]{"me","host"}),"client disconnected required participant blocks barrier");entries[1].connected=true;entries[1].readyEpoch=Id();roster();Check(!client.AllPlayersSceneReady(key),"roster reconnect cannot revive cached ready membership");apply(16,currentMembers());Check(client.AllPlayersSceneReady(key),"reconnected current epoch requires fresh complete ready snapshot");
  native.Sent.Clear();Saves.SavesSystem.Select(2);Check(!client.Ready&&!client.AllPlayersSceneReady(key),"client asset switch invalidates handshake and existing barrier immediately");Time.unscaledTime=3;client.OnUpdate(3,3);Check(ReadyPackets(native)==0,"asset switch does not periodically resend original asset keys");
  acceptedWelcome.helloSequence=HelloSequence(client);native.Packet(upstream,"$welcome",acceptedWelcome);Check(!client.Ready,"old-asset welcome cannot certify new local slot");
  Saves.SavesSystem.Select(1);Check(client.GetLocalAssetId()==localA&&LocalRevision(client)>revA,"switch back to A keeps debt identity but increments readiness revision");acceptedWelcome.helloSequence=HelloSequence(client);native.Packet(upstream,"$welcome",acceptedWelcome);Check(!client.Ready,"first A welcome cannot certify later A incarnation");
  string newEpoch=Id();acceptedWelcome.assetRevision=LocalRevision(client);acceptedWelcome.readyEpoch=newEpoch;native.Packet(upstream,"$welcome",acceptedWelcome);Check(client.Ready&&ReadyPackets(native)==0,"fresh A handshake accepts but does not resurrect old prepared keys");
  apply(17,initialMembers);Check(!client.AllPlayersSceneReady(key),"delayed old-A member snapshot cannot certify returned A");native.Sent.Clear();client.MarkSceneReady(key);sentReady=LastReady(native);Check(sentReady.assetRevision==LocalRevision(client)&&sentReady.readyEpoch==newEpoch,"explicit new A preparation uses new revision and host epoch");apply(18,currentMembers());Check(client.AllPlayersSceneReady(key),"new A can participate after explicit preparation");
  Time.unscaledTime=5;client.OnUpdate(2,2);long inFlightHello=HelloSequence(client);BossRushCoopTransport.NativePeers.Clear();Time.unscaledTime=5.1f;client.OnUpdate(.1f,.1f);object replacement=upstream;BossRushCoopTransport.NativeHost=replacement;BossRushCoopTransport.NativePeers.Add(replacement);Check(!client.Ready&&!client.AllPlayersSceneReady(key),"reconnected host peer cannot reuse old handshake before welcome");
  acceptedWelcome.helloSequence=inFlightHello;native.Sent.Clear();native.Packet(replacement,"$welcome",acceptedWelcome);Check(!client.Ready&&ReadyPackets(native)==0,"pre-disconnect in-flight welcome cannot certify even a reused native peer object");
  Time.unscaledTime=8;client.OnUpdate(2.9f,2.9f);acceptedWelcome.helloSequence=HelloSequence(client);acceptedWelcome.readyEpoch=Id();native.Sent.Clear();native.Packet(replacement,"$welcome",acceptedWelcome);sentReady=LastReady(native);Check(client.Ready&&sentReady.key==key&&sentReady.readyEpoch==acceptedWelcome.readyEpoch&&sentReady.readyEpoch!=newEpoch,"same-Core reconnect preserves preparation only by re-publishing through fresh welcome epoch");
  client.OnDestroy();Saves.SavesSystem.CurrentSlot=1;
 }
}
