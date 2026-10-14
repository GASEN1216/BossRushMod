using System;
using BossRush;
using UnityEngine;
internal static class Program
{
    static int checks;static void Check(bool c,string message){if(!c)throw new Exception(message);checks++;}
    static InteractableLootbox Box(float x,BossRushCoopTransport native)
    {var box=new InteractableLootbox();box.transform.position=new Vector3(x,1,1);box.gameObject.BeforeDestroy=()=>native.UnregisterLoot(box);return box;}
    static void Main()
    {
        var native=new BossRushCoopTransport();var world=new BossRushCoopWorldLoot(native);
        var original=Box(5,native);Check(world.Register(original),"first world box registered");int oldKey=native.Key(original);
        var overlap=Box(5,native);native.RegisterLoot(overlap); // Loader has already overwritten the old native position map.
        Check(world.Register(overlap),"host overlap gets distinct key");
        Check(native.Positions[oldKey].Box==original&&native.Key(overlap)!=oldKey,"old native position repaired before new registration");
        Check(native.Positions[native.Key(overlap)].Box==overlap,"both host boxes independently indexed");
        for(int i=0;i<300;i++){var stacked=Box(5,native);native.RegisterLoot(stacked);Check(world.Register(stacked),"dense shared rewards retain every box beyond the former 256 offset cap");}
        Check(native.Positions[oldKey].Box==original,"dense registration preserves original native inventory owner");
        var shell=Box(900,native);world.Register(shell);world.Owned(shell);
        var authored=Box(900,native);authored.Inventory=shell.Inventory;native.RegisterLoot(authored);
        Check(world.Register(authored),"late authored box replaces auto shell");
        Check(shell==null&&authored!=null&&authored.Inventory!=shell.Inventory,"auto shell destroyed with separate authored inventory");
        Check(native.Positions[native.Key(authored)].Box==authored,"old shell unregister cannot remove new native mapping");
        Check(world.Register(authored)&&native.Positions[native.Key(authored)].Box==authored,"same box registration is idempotent");
        int authoredKey=native.Key(authored);UnityEngine.Object.Destroy(authored.gameObject);Check(authored==null&&!native.Positions.ContainsKey(authoredKey),"world destruction runs native unregister");

        var host=new CharacterMainControl();var guest=new CharacterMainControl();var fighter=new CharacterMainControl();CharacterMainControl.Main=host;
        BossRushCoop.Players.Add(new BossRushCoopPlayer{Id="host",Character=host,IsLocal=true});BossRushCoop.Players.Add(new BossRushCoopPlayer{Id="guest",Character=guest});
        BossRushCoop.Entities["a:-20"]=fighter;LevelManager.Instance.ControllingCharacter=host;GameCamera.Instance.Target=host;
        var control=new BossRushCoopControl();BossRushCoop.DropStates=true;
        string lease=control.Begin("guest","a:-20");Check(lease!=null&&!control.Contains(lease)&&!fighter.Ai.enabled,"reserved remote lease is not confirmed control");
        int before=BossRushCoop.StatesSent;Time.unscaledTime=0.6f;control.Tick();Check(BossRushCoop.StatesSent>before&&!control.Contains(lease),"lost grant state retries while awaiting input");
        Check(control.Begin("host","a:-20")==null,"same entity cannot have two operators");
        var input=new Input {lease=lease,generation=1,sequence=1,move=new Vector3(0.5f,0,0),aim=new Vector3(2,0,2),ads=true};
        BossRushCoop.Inject("control-input","host",false,input);Check(!control.Contains(lease),"wrong operator cannot confirm lease");
        BossRushCoop.Inject("control-input","guest",false,input);Check(control.Contains(lease)&&fighter.Ads&&fighter.Move.x==0.5f,"actual authenticated input confirms and drives unique AI");
        input.move=new Vector3(1,0,0);BossRushCoop.Inject("control-input","guest",false,input);Check(fighter.Move.x==0.5f,"duplicate sequence cannot replay input");
        BossRushCoop.Players[1].AssetId="asset-B";control.Tick();Check(!control.Contains(lease)&&fighter.Ai.enabled&&!fighter.Ads&&!fighter.Fire&&!fighter.Run,"slot switch releases lease and all held input");
        BossRushCoop.DropStates=false;lease=control.Begin("host","a:-20");Check(!control.Contains(lease),"local reserved lease awaits AI binding");control.Tick();
        Check(control.Contains(lease)&&LevelManager.Instance.ControllingCharacter==fighter,"local body binding confirms lease");
        object oldGrant=BossRushCoop.LastState;
        control.End(lease);Check(LevelManager.Instance.ControllingCharacter==host&&GameCamera.Instance.Target==host&&fighter.Ai.enabled,"local release restores original body camera and AI");
        object oldRelease=BossRushCoop.LastState;
        BossRushCoop.Inject("control-state","host",true,oldGrant);control.Tick();
        Check(!control.Contains(lease)&&LevelManager.Instance.ControllingCharacter==host,"delayed grant cannot reclaim released camera and body");
        string newerLease=control.Begin("host","a:-20");control.Tick();
        BossRushCoop.Inject("control-state","host",true,oldGrant);
        BossRushCoop.Inject("control-state","host",true,oldRelease);control.Tick();
        Check(control.Contains(newerLease)&&LevelManager.Instance.ControllingCharacter==fighter,"old grant or release cannot displace newer control");
        control.End(newerLease);

        var scene=new SceneOwner(control,world);
        Duckov.Scenes.MultiSceneCore.Instance=new Duckov.Scenes.MultiSceneCore{SceneInfo=new Duckov.Scenes.Info{ID="arena"},gameObject=new GameObject{scene=new UnityEngine.SceneManagement.Scene{handle=1,isLoaded=true}}};
        scene.Refresh();scene.localReadyKeys.Add("old");scene.barrier.Mark("host","old");scene.aiCharacters[-20]=fighter;
        int generation=scene.Generation;scene.Refresh();Check(scene.Generation==generation,"same loaded scene does not increment repeatedly");
        var remoteLife=new BossRushCoopPlayer{HostAlive=true};scene.players.Add(remoteLife);
        Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene=new UnityEngine.SceneManagement.Scene{handle=2,isLoaded=true};scene.Refresh();
        Check(scene.Generation==generation+1&&world.AppliedRevision==0&&scene.localReadyKeys.Count==0&&scene.aiCharacters.Count==0&&!scene.barrier.AllReady("old",new[]{"host"}),"same scene ID reload clears generation-owned AI readiness and world state");
        Check(!remoteLife.HostAlive,"actual scene owner clear method retires prior remote alive fact");
        control.Dispose();
        BossRushCoop.IsHost=false;
        var gravity=new GravityOwner();gravity.RefreshZombieModeGravityWellTargets(7,new Vector3(13.25f,1.5f,-7.75f),4.25f,12.5f);
        var area=BossRushCoopJson.FromJson<CoopZombieArea>(gravity.Text);
        Check(gravity.Action=="gravity"&&gravity.Count==0&&area.Origin.x==13.25f&&area.Origin.y==1.5f&&area.Origin.z==-7.75f&&area.Radius==4.25f&&area.Strength==12.5f,"actual client gravity command retains native Vector3 origin and tuning through the true codec");
        var sender=new BossRushCoopWorldLoot(native);var receiver=new BossRushCoopWorldLoot(native);
        BossRushCoop.WorldPackets.Clear();sender.PublishBoxes(1,"old");receiver.Receive(BossRushCoop.WorldPackets[0]);Check(receiver.PendingCount==1,"complete first world snapshot accepted");
        BossRushCoop.WorldPackets.Clear();sender.PublishBoxes(2048,new string('箱',50)+"😀");var packets=BossRushCoop.WorldPackets.ToArray();Check(packets.Length>1,"supported 2048 world boxes use fragments");
        foreach(string packet in packets){var envelope=new BossRushCoopEnvelope{version=1,session="room",topic="world-loot",messageId="test",json=packet};Check(System.Text.Encoding.UTF8.GetByteCount(BossRushCoopJson.ToJson(envelope))<262144,"every actual encoded world envelope stays within packet cap");}
        receiver.Receive(packets[0],false);Check(receiver.PendingCount==1,"non-host fragment rejected");
        for(int i=packets.Length-1;i>0;i--){receiver.Receive(packets[i]);receiver.Receive(packets[i]);Check(receiver.PendingCount==1,"partial or duplicate fragment cannot replace complete world and delete boxes");}
        receiver.Receive(packets[0]);Check(receiver.PendingCount==2048&&receiver.PendingName.EndsWith("😀"),"reverse fragments preserve all 2048 boxes and surrogate pairs");
        BossRushCoop.WorldPackets.Clear();sender.PublishBoxes(2,"new");receiver.Receive(BossRushCoop.WorldPackets[0]);foreach(string packet in packets)receiver.Receive(packet);Check(receiver.PendingCount==2&&receiver.PendingName=="new","old complete snapshot cannot overwrite new version");
        receiver.ChangeGeneration();BossRushCoop.SceneGeneration++;foreach(string packet in packets)receiver.Receive(packet);Check(receiver.PendingCount==-1,"same-map generation clears fragments and rejects stale world");
        Console.WriteLine("CoopWorldRuntime: "+checks+" PASS");
    }
    [Serializable]private class Input{public string lease;public int generation;public long sequence;public Vector3 move,aim;public bool ads;}
}
