using System;
using System.Linq;
using System.Reflection;
using BossRush;
using UnityEngine;
class Program {
 static int checks;
 static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
 static BossRushCoopRuntimeModule Start(bool server){BossRushCoopTransport.Server=server;BossRushCoopTransport.NativePeers.Clear();BossRushCoopTransport.PeerCharacters.Clear();BossRushCoopTransport.RemoteCharacters.Clear();CharacterMainControl.Main=new CharacterMainControl();Duckov.Scenes.MultiSceneCore.Instance=null;BossRushCoop.Disconnected.Clear();Time.unscaledTime=0;var r=new BossRushCoopRuntimeModule();typeof(BossRushCoopRuntimeModule).GetField("manifest",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(r,"same");r.OnAwake(new ModBehaviour());return r;}
 // A forward-compatible wire input, not a replacement for production roster parsing
 // or the extracted production player predicate. The old DTO ignores alive.
 [Serializable] sealed class LifeEntry {public string id,nativeId,assetId;public bool connected,alive;}
 [Serializable] sealed class LifeRoster {public int generation;public long revision;public bool ready;public LifeEntry[] players;}
 static void Scene(int handle){Duckov.Scenes.MultiSceneCore.Instance=new Duckov.Scenes.MultiSceneCore{SceneInfo=new Duckov.Scenes.SceneInfo{ID="GroundZero"},gameObject=new GameObject{scene=new UnityEngine.SceneManagement.Scene{handle=handle,isLoaded=true}}};}
 static void AliveFacts(){
  var client=Start(false);var native=BossRushCoopTransport.Current;var upstream=new object();BossRushCoopTransport.NativeHost=upstream;BossRushCoopTransport.NativePeers.Add(upstream);Scene(101);
  native.Packet(upstream,"$welcome",new BossRushCoopWelcome{helloSequence=1,session="life-room",playerId="me",generation=7});
  Check(!new BossRushCoopPlayer{IsConnected=true,Character=new CharacterMainControl()}.IsAlive,"healthy unknown remote body is never assumed alive");
  var entries=new LifeEntry[16];entries[0]=new LifeEntry{id="me",nativeId="native-local",assetId=Guid.NewGuid().ToString("N"),connected=true,alive=false};
  for(int i=1;i<16;i++){entries[i]=new LifeEntry{id="p"+i,nativeId="body"+i,assetId=Guid.NewGuid().ToString("N"),connected=true,alive=false};BossRushCoopTransport.RemoteCharacters.Add(entries[i].nativeId,new CharacterMainControl());}
  CharacterMainControl.Main.Health.IsDead=true;
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=1,ready=true,players=entries},"life-room");
  Check(client.Players.Count==16&&client.Players.All(p=>!p.IsAlive),"host dead facts override fifteen healthy native client proxies");
  CharacterMainControl.Main.Health.IsDead=false;Check(client.Players.Single(p=>p.IsLocal).IsAlive,"local native health takes priority over roster dead fact");
  var first=client.Players.Single(p=>p.Id=="p1");BossRushCoopTransport.RemoteCharacters["body1"].Health.IsDead=true;entries[1].alive=true;
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=2,ready=true,players=entries},"life-room");
  Check(first.IsAlive,"remote authoritative live fact ignores stale proxy IsDead");
  entries[1].alive=false;native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=3,ready=true,players=entries},"life-room");Check(!first.IsAlive,"new host death stops remote participation");
  entries[1].alive=true;
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=2,ready=true,players=entries},"life-room");Check(!first.IsAlive,"older roster revision cannot resurrect dead player");
  native.Packet(upstream,"$roster",new LifeRoster{generation=6,revision=900,ready=true,players=entries},"life-room");Check(!first.IsAlive,"older generation cannot resurrect even with higher revision");
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=900,ready=true,players=entries},"previous-room");Check(!first.IsAlive,"previous session cannot resurrect even with higher revision");
  native.Packet(new object(),"$roster",new LifeRoster{generation=7,revision=900,ready=true,players=entries},"life-room");Check(!first.IsAlive,"non-host peer cannot publish alive facts");
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=4,ready=true,players=entries},"life-room");Check(first.IsAlive,"new host life fact accepted");
  BossRushCoopTransport.RemoteCharacters["body1"]=new CharacterMainControl();Time.unscaledTime=1;client.OnUpdate(1,1);Check(!first.IsAlive,"native replacement body waits for a fresh host fact");
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=5,ready=true,players=entries},"life-room");Check(first.IsAlive,"fresh roster binds actual replacement body");
  entries[1].assetId=Guid.NewGuid().ToString("N");entries[1].alive=false;native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=6,ready=true,players=entries},"life-room");Check(!first.IsAlive,"new asset dead fact is independent of previous slot life");
  entries[1].alive=true;native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=7,ready=true,players=entries},"life-room");Check(first.IsAlive,"current asset receives fresh authoritative life");
  Scene(102);Time.unscaledTime=2;client.OnUpdate(1,1);Check(!first.IsAlive,"same-map new scene instance immediately invalidates prior life");
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=8,ready=true,players=entries},"life-room");Check(!first.IsAlive,"pre-reload generation cannot restore life before host advances");
  native.Packet(upstream,"$roster",new LifeRoster{generation=8,revision=9,ready=true,players=entries},"life-room");Check(first.IsAlive,"new host generation binds loaded scene life");
  native.Packet(upstream,"$roster",new LifeRoster{generation=7,revision=99,ready=true,players=entries},"life-room");Check(client.Generation==8&&first.IsAlive,"old scene roster cannot revert current generation");
  native.Packet(upstream,"$roster",new LifeRoster{generation=8,revision=10,ready=true,players=entries.Take(1).ToArray()},"life-room");Check(!first.IsAlive,"missing member in authoritative snapshot is unknown rather than alive");
  native.Packet(upstream,"$roster",new LifeRoster{generation=8,revision=11,ready=true,players=entries},"life-room");Check(first.IsAlive,"complete snapshot restores current missing member");
  BossRushCoopTransport.NativePeers.Clear();Check(!first.IsAlive,"host disconnection cannot fall back to client proxy life");BossRushCoopTransport.NativePeers.Add(upstream);Check(!first.IsAlive,"reconnect awaits fresh host life rather than restoring disconnected cache");
  native.Packet(upstream,"$roster",new LifeRoster{generation=8,revision=12,ready=true,players=entries},"life-room");Check(first.IsAlive,"fresh host snapshot restores same-room reconnect life");client.OnDestroy();
  var host=Start(true);native=BossRushCoopTransport.Current;
  for(int i=0;i<15;i++){var peer=new object();BossRushCoopTransport.NativePeers.Add(peer);BossRushCoopTransport.PeerCharacters.Add(peer,new CharacterMainControl());native.Packet(peer,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=Guid.NewGuid().ToString("N")});}
  Time.unscaledTime=1;host.OnUpdate(1,1);Time.unscaledTime=2;host.OnUpdate(1,1);
  var guest=host.Players.First(p=>!p.IsLocal);guest.Character.Health.IsDead=true;CharacterMainControl.Main.Health.IsDead=true;native.Sent.Clear();Time.unscaledTime=3;host.OnUpdate(1,1);
  var packet=native.Sent.Select(BossRushCoopJson.FromJson<BossRushCoopEnvelope>).Last(e=>e.topic=="$roster");var fact=BossRushCoopJson.FromJson<LifeRoster>(packet.json);
  Check(fact.players.Length==16&&fact.players.Count(p=>p.alive)==14,"actual host sender encodes sixteen native life facts including two deaths");
  Check(!host.Players.Single(p=>p.IsLocal).IsAlive&&!guest.IsAlive,"host uses official native IsDead without health-value assumptions");
  native.Packet(guest.Peer,"$hello",new BossRushCoopHello{sequence=2,manifest="same",assetId=Guid.NewGuid().ToString("N")});Check(guest.Character==null&&!guest.IsAlive,"asset handshake retires old body's life until native rebind");host.OnDestroy();
 }
 static int ReadyPackets(BossRushCoopTransport native){return native.Sent.Count(x=>BossRushCoopJson.FromJson<BossRushCoopEnvelope>(x).topic=="$ready");}
 static void ReadyTraffic(){
  var host=Start(true);var native=BossRushCoopTransport.Current;
  for(int i=0;i<15;i++){var peer=new object();BossRushCoopTransport.NativePeers.Add(peer);native.Packet(peer,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=Guid.NewGuid().ToString("N")});}
  native.Sent.Clear();for(int frame=0;frame<120;frame++)host.MarkSceneReady("arena:round");
  Check(ReadyPackets(native)==15,"16-person repeated frame readiness sends one immediate fact per peer, below native queue capacity");
  foreach(var peer in BossRushCoopTransport.NativePeers.ToArray())native.Packet(peer,"$ready",new BossRushCoopReady{generation=host.Generation,key="arena:round"},host.SessionId);
  Check(host.AllPlayersSceneReady("arena:round"),"all 16 actual runtime participants unblock together");host.OnDestroy();
  var client=Start(false);native=BossRushCoopTransport.Current;var upstream=new object();BossRushCoopTransport.NativeHost=upstream;BossRushCoopTransport.NativePeers.Add(upstream);
  native.Packet(upstream,"$welcome",new BossRushCoopWelcome{helloSequence=1,session="retry-room",playerId="me",generation=1});
  native.Sent.Clear();for(int frame=0;frame<120;frame++)client.MarkSceneReady("arena:round");Check(ReadyPackets(native)==1,"client readiness does not send once per frame");
  Time.unscaledTime=3;client.OnUpdate(3,3);Check(ReadyPackets(native)==2,"existing timed hello retries a readiness packet lost in transit");
  native.Packet(upstream,"$roster",new BossRushCoopRoster{generation=2,revision=1,ready=true,players=new[]{new BossRushCoopRosterEntry{id="me",connected=true}}},"retry-room");
  native.Sent.Clear();client.MarkSceneReady("arena:round");Check(ReadyPackets(native)==1,"new generation immediately sends same-named readiness again");client.OnDestroy();
 }
 static void Main(){AliveFacts();ReadyTraffic();
  var host=Start(true);var peer=new object();BossRushCoopTransport.NativePeers.Add(peer);var native=BossRushCoopTransport.Current;
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=Guid.NewGuid().ToString("N")});
  var guest=host.Players.Single(p=>!p.IsLocal);string id=guest.Id,token=guest.ReconnectToken;
  Check(host.Ready&&guest.IsConnected,"authenticated peer completes host readiness");
  string oldAsset=guest.AssetId,newAsset=Guid.NewGuid().ToString("N");
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=3,manifest="same",assetId=newAsset});
  native.Packet(peer,"$hello",new BossRushCoopHello{sequence=2,manifest="same",assetId=oldAsset});
  Check(guest.AssetId==newAsset,"out-of-order hello cannot restore previous slot identity");
  BossRushCoopTransport.NativePeers.Clear(); // Direct upstream path emits no API disconnect event
  Time.unscaledTime=1;host.OnUpdate(1,1);
  Check(!guest.IsConnected&&guest.Character==null&&BossRushCoop.Disconnected.Contains(id)&&host.Ready,"native missing peer retires membership without event");
  var next=new object();BossRushCoopTransport.NativePeers.Add(next);
  native.Packet(next,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=guest.AssetId,reconnectToken=token});
  Check(guest.IsConnected&&guest.Peer==next&&host.Players.Count==2,"reconnect preserves participant identity");
  BossRushCoopTransport.NativePeers.Clear();var fast=new object();BossRushCoopTransport.NativePeers.Add(fast);
  native.Packet(fast,"$hello",new BossRushCoopHello{sequence=1,manifest="same",assetId=guest.AssetId,reconnectToken=token});
  Check(guest.IsConnected&&guest.Peer==fast&&host.Players.Count==2,"reconnect before polling also preserves participant identity");host.OnDestroy();
  var client=Start(false);var upstream=new object();BossRushCoopTransport.NativePeers.Add(upstream);BossRushCoopTransport.NativeHost=upstream;native=BossRushCoopTransport.Current;
  native.Packet(upstream,"$welcome",new BossRushCoopWelcome{helloSequence=1,session="old",playerId="me",generation=8});
  native.Packet(upstream,"$roster",new BossRushCoopRoster{generation=8,revision=900,ready=true,players=new[]{new BossRushCoopRosterEntry{id="me",connected=true},new BossRushCoopRosterEntry{id="former",connected=true}}},"old");
  Check(client.Ready&&client.Players.Count==2,"old room roster accepted");
  BossRushCoopTransport.NativePeers.Clear();Check(!client.Ready,"native disconnection immediately removes client readiness");BossRushCoopTransport.NativePeers.Add(upstream);
  Time.unscaledTime=3;client.OnUpdate(1,1);
  native.Packet(upstream,"$welcome",new BossRushCoopWelcome{helloSequence=2,session="new",playerId="me-new",generation=1});
  native.Packet(upstream,"$roster",new BossRushCoopRoster{generation=1,revision=1,ready=true,players=new[]{new BossRushCoopRosterEntry{id="me-new",connected=true}}},"new");
  Check(client.Ready&&client.Generation==1&&client.Players.Count==1&&client.Players[0].Id=="me-new","new room accepts low revision and clears stale players and generation");
  native.Packet(upstream,"$welcome",new BossRushCoopWelcome{helloSequence=1,session="old",playerId="me",generation=8});
  Check(client.SessionId=="new"&&client.Generation==1&&client.Players.Count==1,"delayed welcome cannot roll client back into previous room");
  client.OnDestroy();Console.WriteLine("CoopSession: PASS "+checks+" production runtime assertions; native transport is controlled stub");
 }
}
