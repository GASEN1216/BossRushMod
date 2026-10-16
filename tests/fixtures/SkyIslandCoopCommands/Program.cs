using System;
using BossRush;
using UnityEngine;
using ItemStatsSystem;

internal static class Program
{
    private static void Check(bool value,string label) { if(!value)throw new Exception(label); }
    private static void Reset()
    {
        SkyIslandInventoryTransaction.Reset();BossRushCoop.Requests.Clear();BossRushCoop.IsHost=false;
        BossRushCoop.IsConnected=true;BossRushCoop.Players.Clear();
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="sky-core";
        Time.time=Time.unscaledTime=0;
        CharacterMainControl.Main=new CharacterMainControl { Id="guest" };
        BossRushCoop.LocalAssetId="asset-1";
        BossRushCoop.Players["guest"]=new BossRushCoopPlayer { Id="guest",Character=CharacterMainControl.Main };
    }
    public static int Main()
    {
        Reset();var host=new SkyIslandGnats();var guest=new SkyIslandGnats();host.Ready(true);guest.Ready(true);
        Check(!guest.QueueCoopUse(SkyIslandFieldBuff.Zapper,new Item()),"zapper original CA_UseItem must refund until actual host admission");
        guest.TickUse();Check(SkyIslandInventoryTransaction.Inventory==1 && BossRushCoop.Requests.Count==0,"official synchronous decrement finishes before reservation");
        Time.unscaledTime=1;guest.TickUse();
        Check(SkyIslandInventoryTransaction.Inventory==0 && SkyIslandInventoryTransaction.Reserved==1,"one personal item reserved");
        BossRushCoop.IsHost=true;BossRushCoop.HasBoundScene=false;host.Receive(BossRushCoop.Requests[0]);
        Check(host.WorldUses==0&&host.Packets==0,"unbound host cannot admit consumable world placement or commit personal cost");
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="another-core";host.Receive(BossRushCoop.Requests[0]);
        Check(host.WorldUses==0&&host.Packets==0,"old Core consumable request cannot use a new map with unchanged public generation");
        BossRushCoop.SceneToken="sky-core";host.Receive(BossRushCoop.Requests[0]);host.Receive(BossRushCoop.Requests[0]);
        Check(host.WorldUses==1 && host.Packets==2,"duplicate request applies shared world only once and returns same decision");
        BossRushCoop.IsHost=false;Check(guest.ApplyPacket(host,0),"successful receipt accepted");Check(guest.ApplyPacket(host,1),"duplicate receipt accepted");
        Check(SkyIslandInventoryTransaction.Commits==1 && SkyIslandInventoryTransaction.Refunds==0 && !guest.CoopUsePending,"personal cost committed once after actual world success");
        Reset();host=new SkyIslandGnats { AcceptWorld=false };guest=new SkyIslandGnats();host.Ready(true);guest.Ready(true);
        guest.QueueCoopUse(SkyIslandFieldBuff.Zapper,new Item());Time.unscaledTime=1;guest.TickUse();
        BossRushCoop.IsHost=true;host.Receive(BossRushCoop.Requests[0]);BossRushCoop.IsHost=false;
        guest.ApplyPacket(host,0);guest.ApplyPacket(host,0);
        Check(SkyIslandInventoryTransaction.Inventory==1 && SkyIslandInventoryTransaction.Refunds==1 && SkyIslandInventoryTransaction.Commits==0,"rejected placement returns same personal item once");
        Reset();host=new SkyIslandGnats();guest=new SkyIslandGnats();host.Ready(true);guest.Ready(true);BossRushCoop.IsHost=true;
        host.Bite(CharacterMainControl.Main);host.Kill("gnat-1",CharacterMainControl.Main);host.Kill("gnat-1",CharacterMainControl.Main);
        Check(host.Bites==0 && host.Culls==0 && host.Packets==2,"remote victim/killer never advances host personal progress");
        Check(host.Destination(0)=="guest" && host.Destination(1)=="guest","bite and cull receipts route to actual player identity");
        BossRushCoop.IsHost=false;guest.ApplyPacket(host,0);guest.ApplyPacket(host,1);
        Check(guest.Bites==1 && guest.Culls==1,"only addressed owner receives itch and cull progression");
        Reset();host=new SkyIslandGnats();guest=new SkyIslandGnats();host.Ready(true);guest.Ready(true);
        guest.QueueCoopUse(SkyIslandFieldBuff.Zapper,new Item());Time.unscaledTime=1;guest.TickUse();
        BossRushCoop.LocalAssetId="asset-2";Time.unscaledTime=2;guest.TickUse();
        Check(BossRushCoop.Requests.Count==1 && SkyIslandInventoryTransaction.Reserved==1,"old-slot request pauses and keeps original escrow");
        BossRushCoop.IsHost=true;BossRushCoop.Players["guest"].AssetId="asset-2";host.Receive(BossRushCoop.Requests[0]);
        Check(host.WorldUses==0,"host rejects former-slot request after player's current identity changes");
        BossRushCoop.Players["guest"].AssetId="asset-1";host.Receive(BossRushCoop.Requests[0]);BossRushCoop.IsHost=false;
        Check(!guest.ApplyPacket(host,0) && SkyIslandInventoryTransaction.Commits==0,"late receipt cannot commit cost in different slot");
        BossRushCoop.LocalAssetId="asset-1";Check(guest.ApplyPacket(host,0) && SkyIslandInventoryTransaction.Commits==1,"original slot can complete the same escrow receipt");
        Reset();guest=new SkyIslandGnats();
        Check(!guest.QueueCoopUse(SkyIslandFieldBuff.Zapper,new Item()) && !guest.CoopUsePending,"uninitialized session cannot reserve cost or send a use");
        WorldRegression.Run();
        SceneProjection.Run(Check);
        Console.WriteLine("PASS SkyIslandCoopCommands: host request idempotence, deferred reservation, commit/refund, personal bite/cull routing, asset escrow isolation, 16-player world skills, native arrival, gun noise and equipment");return 0;
    }
}
