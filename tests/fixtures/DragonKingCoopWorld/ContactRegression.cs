using System;
using BossRush;
using UnityEngine;

internal static class ContactRegression
{
    internal static void Run(Action<bool,string> check)
    {
        BossRushCoop.Players.Clear();BossRushCoop.Outbox.Clear();BossRushCoop.Handlers.Clear();
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="contact-room";BossRushCoop.SceneGeneration=3;
        Time.unscaledTime=1;
        var guest=new CharacterMainControl();guest.transform.position=new Vector3(1,0,0);
        var host=new CharacterMainControl();host.transform.position=new Vector3(1,0,0);
        var guestMember=new BossRushCoopPlayer { Id="guest",Character=guest };
        BossRushCoop.Players.Add(guestMember);BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="host",Character=host });
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=guest;
        var client=new DragonKingCoopContactRuntimeModule();client.OnAwake(null);client.OnUpdate(0,.1f);
        var firstBody=BossRushCoop.Outbox[0];
        check(firstBody.Json.Contains("\"Token\"") && firstBody.Json.Contains("\"Revision\""),"real shared codec carries body identity fields through JSON");
        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host;
        var server=new DragonKingCoopContactRuntimeModule();server.OnAwake(null);
        var controller=new DragonKingAbilityController();controller.Knock(guest);
        check(BossRushCoop.Outbox.Count==1,"contact before body handshake waits with the actual target frozen");
        BossRushCoop.Emit(firstBody,false);
        server.OnUpdate(0,.1f);
        var hit=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        check(hit.Target=="guest" && hit.Topic=="BossRush.Personal.DragonKing.Contact" && guest.Pushes==0 && guest.gameObject.Body.Impulses==0,"host sends frozen personal push rather than applying force to a remote kinematic model");
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=guest;
        BossRushCoop.Emit(hit,false);check(guest.Pushes==0,"untrusted client cannot push another player");
        BossRushCoop.Emit(hit,true);BossRushCoop.Emit(hit,true);
        check(guest.Pushes==1 && Math.Abs(guest.LastPush.sqrMagnitude-64f)<.001f,"real personal delivery duplicates produce one official movement impulse at original force");
        var ack=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host;
        BossRushCoop.Emit(ack,false);controller.Knock(host);
        check(host.Pushes==1 && host.gameObject.Body.Impulses==0,"host local body executes the same receipt callback");

        controller.Knock(guest);var staleBodyHit=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        var replacement=new CharacterMainControl();replacement.transform.position=guest.transform.position;guestMember.Character=replacement;
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=replacement;client.OnUpdate(0,.1f);
        var newBody=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        BossRushCoop.Emit(staleBodyHit,true);check(replacement.Pushes==0,"late contact cannot push a replacement body in the same map and slot");
        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host;
        BossRushCoop.Emit(newBody,false);BossRushCoop.Emit(firstBody,false);controller.Knock(replacement);
        var freshHit=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=replacement;
        BossRushCoop.Emit(freshHit,true);check(replacement.Pushes==1,"reordered body announcement cannot replace the current life token");

        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host;
        controller.Knock(replacement);var deathHit=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=replacement;
        replacement.Health.IsDead=true;BossRushCoop.Emit(deathHit,true);replacement.Health.IsDead=false;
        client.OnUpdate(0,.1f);BossRushCoop.Emit(deathHit,true);
        check(replacement.Pushes==1,"death and same-object revival invalidate old contact before it can move the revived player");

        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host;
        controller.Knock(replacement);var slotHit=BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1];
        BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";BossRushCoop.LocalAssetId="other-slot";CharacterMainControl.Main=replacement;
        BossRushCoop.Emit(slotHit,true);check(replacement.Pushes==1,"another save asset cannot receive the old impulse");
        BossRushCoop.LocalAssetId="asset";BossRushCoop.SceneGeneration=4;BossRushCoop.Emit(slotHit,true);
        check(replacement.Pushes==1,"same-map reload generation rejects old impulses");
        BossRushCoop.RunId="other-room";BossRushCoop.SceneGeneration=3;BossRushCoop.Emit(slotHit,true);
        check(replacement.Pushes==1,"new room rejects old impulses");
        client.OnDestroy();server.OnDestroy();check(BossRushCoop.Handlers.Count==0,"runtime destroy releases every body and receipt subscription");
        BossRushCoop.IsConnected=false;controller.Knock(host);check(host.gameObject.Body.Impulses==1,"single player retains original Rigidbody impulse");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;DragonKingAbilityController.Faction=true;controller.Knock(host);
        check(host.gameObject.Body.Impulses==2,"faction AI mode retains its original impulse path");
        DragonKingAbilityController.Faction=false;BossRushCoop.LocalPlayerId="host";BossRushCoop.LocalAssetId="asset";
        BossRushCoop.RunId="room";BossRushCoop.SceneGeneration=1;BossRushCoop.Outbox.Clear();
    }
}
