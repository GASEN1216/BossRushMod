using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
namespace UnityEngine { public struct Vector3 { public float x,y,z; } public struct Quaternion { public float x,y,z,w; } }
namespace BossRush
{
    internal sealed class RepeatState { }
    internal sealed partial class ModeERuntimeModule
    {
        private readonly Dictionary<string,CoopModeEUtilityRequest> coopUtilityRequests = new Dictionary<string,CoopModeEUtilityRequest>();
        private readonly Dictionary<string,CoopModeECoalRequest> coopCoalRequests = new Dictionary<string,CoopModeECoalRequest>();
        private float coopUtilityRetry,coopCoalRetry;
        internal CoopGameplayChannel<RepeatState> coopChannel;
        internal int RefundTicks,Refreshes;
        // These host endpoints do not implement queue ownership or request reuse.
        private void RefundCancelledCoopUtilities() { RefundTicks++; }
        private void RefundCancelledCoopCoal() { RefundTicks++; }
        private void RefreshCoopCoalSession() { Refreshes++; }
        internal void Add(string id)
        {
            coopUtilityRequests.Add(id,new CoopModeEUtilityRequest {Id=id,AssetId=BossRushCoop.LocalAssetId,Run=BossRushCoop.RunId});
            coopCoalRequests.Add(id,new CoopModeECoalRequest {Id=id,AssetId=BossRushCoop.LocalAssetId,Run=BossRushCoop.RunId});
        }
        internal void AuditTick() { TickCoopUtilities(1); TickCoopCoal(1); }
    }
    internal sealed partial class ModeFRuntimeModule
    {
        private readonly Dictionary<string,CoopModeFFortificationRequest> coopFortRequests = new Dictionary<string,CoopModeFFortificationRequest>();
        private float coopFortRetry;
        internal CoopGameplayChannel<RepeatState> coopChannel;
        internal int RefundTicks,Refreshes;
        private void RefundCancelledCoopFortifications() { RefundTicks++; }
        private void RefreshCoopFortificationSession() { Refreshes++; }
        internal void Add(string id) {coopFortRequests.Add(id,new CoopModeFFortificationRequest {Id=id,AssetId=BossRushCoop.LocalAssetId,Run=BossRushCoop.RunId});}
        internal void AuditTick() {TickCoopFortificationTransactions(1);}
    }
}
internal static partial class Program
{
    private static int checks;
    private static void Check(bool value,string description) {checks++;if(!value)throw new Exception(description);}
    private static int Pending(CoopGameplayChannel<RepeatState> channel)
    {return ((System.Collections.ICollection)typeof(CoopGameplayChannel<RepeatState>).GetField("pendingRequests",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(channel)).Count;}
    private static void Main()
    {
        var host=new TestPeer {Id="host",Host=true};var guest=new TestPeer {Id="guest"};host.BindScene();guest.BindScene();
        BossRushCoop.Peers.Add(host);BossRushCoop.Peers.Add(guest);BossRushCoop.Current=guest;guest.Bound=false;
        var e=new ModeERuntimeModule {coopChannel=new CoopGameplayChannel<RepeatState>("E-repeat",null,null,null)};
        var f=new ModeFRuntimeModule {coopChannel=new CoopGameplayChannel<RepeatState>("F-repeat",null,null,null)};
        e.Add("paid-A");e.Add("paid-B");f.Add("consumed-A");f.Add("consumed-B");
        for(int i=0;i<1000;i++){e.AuditTick();f.AuditTick();}
        Check(Pending(e.coopChannel)==4 && Pending(f.coopChannel)==2,"actual E/F periodic methods cannot multiply the same paid transaction while unbound");
        Check(e.RefundTicks==2000 && f.RefundTicks==1000,"unbound network wait keeps every personal refund tick");
        Check(BossRushCoop.SendAttempts==0,"actual callers do not send before binding");
        e.Add("paid-C");f.Add("consumed-C");e.AuditTick();f.AuditTick();
        Check(Pending(e.coopChannel)==6 && Pending(f.coopChannel)==3,"a new legitimate transaction retains its full independent obligation");
        int effects=0;BossRushCoop.Current=host;
        var se=new CoopGameplayChannel<RepeatState>("E-repeat",null,null,c=>effects++);
        var sf=new CoopGameplayChannel<RepeatState>("F-repeat",null,null,c=>effects++);
        BossRushCoop.Current=guest;guest.Bound=true;e.coopChannel.Tick(0);f.coopChannel.Tick(0);BossRushCoop.Flush();
        Check(effects==9 && Pending(e.coopChannel)==0 && Pending(f.coopChannel)==0,"actual binding dispatches every unique transaction through real codec and ACK");
        e.AuditTick();f.AuditTick();BossRushCoop.Flush();
        Check(effects==18,"ACK does not suppress later business recovery reports while personal receipts remain pending");
        e.coopChannel.Dispose();f.coopChannel.Dispose();se.Dispose();sf.Dispose();
        EntryStartup();
        Console.WriteLine("PASS RepeatCallers: "+checks+" production-linked assertions; 1000 actual E/F periodic ticks");
    }
}
