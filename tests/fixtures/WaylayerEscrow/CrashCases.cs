using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;

internal static partial class Program
{
    [Serializable] private sealed class CrashRecord
    {
        public string Id, Mode;
        public int Pid, State, Protocol, WorldCount, PrivateAlive, GuestCount, RecoveredTickets;
        public bool PublicationStarted, Success;
    }
    private static void CrashCheckpoint(string directory,string mode,string id)
    {
        Host();var physical=BossRushCoopJson.FromJson<CoopWaylayerJournal.Document>((string)Saves.SavesSystem.Disk[Saves.SavesSystem.CurrentFilePath]["BossRush_CoopWaylayerJournal"]);
        var commit=physical.Commits.Single(v=>v.Id==id);
        int held=0;
        var entries=(IDictionary)typeof(CoopWaylayerEscrow).GetField("host",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);
        foreach(DictionaryEntry entry in entries)
        {
            var item=(Item)entry.Value.GetType().GetField("Custody",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(entry.Value);
            if(item!=null&&!item.IsBeingDestroyed&&!SkyIslandInventoryTransaction.HasOwner(item))held++;
        }
        var record=new CrashRecord {Id=id,Mode=mode,Pid=Process.GetCurrentProcess().Id,State=commit.State,Protocol=commit.PublicationProtocol,
            PublicationStarted=commit.PublicationStarted,WorldCount=chief.Bag.Content.Where(v=>v!=null).Sum(v=>v.StackCount),PrivateAlive=held,
            GuestCount=guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)};
        File.WriteAllText(Path.Combine(directory,"checkpoint.tmp"),BossRushCoopJson.ToJson(record));
        File.Move(Path.Combine(directory,"checkpoint.tmp"),Path.Combine(directory,"checkpoint.json"));
        // Parent terminates this process. No Dispose, finally or journal flush runs.
        Thread.Sleep(Timeout.Infinite);
    }
    private static void CrashProcess(string[] args)
    {
        string phase=args[0],directory=args[1],mode=args[2];
        Saves.SavesSystem.PhysicalRoot=directory;
        if(phase=="--crash-stage")
        {
            Setup();Guest();var journal=Journal(guest);Add(guestPlayer,5002,3,"physical-process-original-"+mode);
            var native=journal.PrepareLoad(ItemTreeData.FromItem(guestPlayer.CharacterItem));
            guestPlayer.CharacterItem=ItemTreeData.Clone(native);
            File.WriteAllText(Path.Combine(directory,"native-guest.json"),BossRushCoopJson.ToJson(native));
            var packet=Start();var response=Response();Host();int start=Saves.SavesSystem.PhysicalWrites;
            Saves.SavesSystem.ThrowAfterDisk=true;
            if(mode=="publication")
            {
                Saves.SavesSystem.FailReadAfterWrites=start+3;Saves.SavesSystem.FailBeforeDiskAfterWrites=start+3;
                Saves.SavesSystem.BeforeRefusedWrite=()=>CrashCheckpoint(directory,mode,packet.Id);
            }
            else if(mode=="success-before")
            {Saves.SavesSystem.FailReadAfterWrites=start+4;Saves.SavesSystem.FailBeforeDiskAfterWrites=start+3;}
            else if(mode=="success-after")Saves.SavesSystem.FailReadAfterWrites=start+4;
            else throw new Exception("unknown crash mode");
            Respond(response);CrashCheckpoint(directory,mode,packet.Id);return;
        }
        if(phase=="--claim-recover")
        {
            Setup(true);Guest();var loaded=Journal(guest);guestPlayer.CharacterItem=ItemTreeData.Clone(loaded.PrepareLoad(BossRushCoopJson.FromJson<ItemTreeData>(File.ReadAllText(Path.Combine(directory,"native-claimed.json")))));Handshake();Guest();guest.Tick(2);Host();host.Tick(2);Guest();guest.Tick(2);
            var final=new CrashRecord {Pid=Process.GetCurrentProcess().Id,GuestCount=guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)};Host();final.WorldCount=UsableTickets(host);
            File.WriteAllText(Path.Combine(directory,"claimed-reload.json"),BossRushCoopJson.ToJson(final));return;
        }
        if(phase!="--crash-recover")throw new Exception("unknown child phase");
        var saved=BossRushCoopJson.FromJson<CrashRecord>(File.ReadAllText(Path.Combine(directory,"checkpoint.json")));
        Setup(true);Guest();var j=Journal(guest);
        guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(BossRushCoopJson.FromJson<ItemTreeData>(File.ReadAllText(Path.Combine(directory,"native-guest.json")))));
        BossRushCoop.RunId="new-process-original-host-"+mode;Handshake();Guest();guest.Tick(1);
        var query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;
        Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);
        var decision=(CoopWaylayerEscrow.Decision)BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",decision,"host",true);
        BossRushCoop.Receive("guest","sky.waylayer.result",decision,"host",true);
        saved.Pid=Process.GetCurrentProcess().Id;saved.Success=decision.Success;saved.State=j.FindDebt(saved.Id).State;
        saved.GuestCount=guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount);
        Host();var committed=Journal(host).FindCommit(saved.Id);
        if(committed.Tree==null||committed.Tree.RootData==null)throw new Exception("durable original responsibility disappeared");
        host.Tick(2);saved.WorldCount=UsableTickets(host);saved.RecoveredTickets=saved.WorldCount;
        if(saved.Success)
        {
            var ticket=TicketItem(host,saved.Id);if(ticket==null||!CoopWaylayerClaims.Locked(ticket))throw new Exception("RELEASE_BLOCKER: durable success has no usable protected original after restart");
            var claim=SendClaim(ticket);ReceiveClaim(claim);DeliverClaim();Guest();j.BeforeNativeSave(guestPlayer.CharacterItem);
            File.WriteAllText(Path.Combine(directory,"native-claimed.json"),BossRushCoopJson.ToJson(ItemTreeData.FromItem(guestPlayer.CharacterItem)));Saves.SavesSystem.SaveFile(false);
            saved.GuestCount=guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount);Host();saved.WorldCount=UsableTickets(host);
        }
        File.WriteAllText(Path.Combine(directory,"recovery.json"),BossRushCoopJson.ToJson(saved));
    }
    private static Process StartCrashProcess(string phase,string directory,string mode)
    {
        string target=Assembly.GetExecutingAssembly().Location;
        bool dll=target.EndsWith(".dll",StringComparison.OrdinalIgnoreCase);
        string arguments=(dll?"\""+target+"\" ":"")+phase+" \""+directory+"\" "+mode;
        return Process.Start(new ProcessStartInfo {FileName=dll?"dotnet":target,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true});
    }
    private static async Task CrashCases()
    {
        string root=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"pc06-crash-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach(string mode in new[]{"publication","success-before","success-after"})
        {
            string directory=Path.Combine(root,mode);Directory.CreateDirectory(directory);
            CrashRecord checkpoint;
            using(var writer=StartCrashProcess("--crash-stage",directory,mode))
            {
                string path=Path.Combine(directory,"checkpoint.json");var timeout=Stopwatch.StartNew();
                while(!File.Exists(path)&&!writer.HasExited&&timeout.ElapsedMilliseconds<45000)await Task.Delay(20);
                if(!File.Exists(path)){if(!writer.HasExited)writer.Kill();throw new Exception("crash writer did not reach exact checkpoint: "+mode);}
                try
                {
                checkpoint=BossRushCoopJson.FromJson<CrashRecord>(File.ReadAllText(path));
                Check(!writer.HasExited&&checkpoint.Pid==writer.Id,"real child is alive at physical fault checkpoint: "+mode);
                Check(checkpoint.WorldCount==0&&checkpoint.GuestCount==2,"unconfirmed success never leaks into lootable boss inventory: "+mode);
                Check(checkpoint.Protocol==3&&checkpoint.PublicationStarted,"physical new-protocol barrier survives without cache: "+mode);
                Check(checkpoint.State==(mode=="success-after"?CoopWaylayerJournal.Success:CoopWaylayerJournal.Custody),"physical state matches exact failed write window: "+mode);
                if(mode!="publication")Check(checkpoint.PrivateAlive==1,"unconfirmed terminal retains the same private live original: "+mode);

                }
                finally {if(!writer.HasExited)writer.Kill();writer.WaitForExit(5000);}
                Check(writer.HasExited,"forced process termination completes without graceful journal flush: "+mode);
            }
            using(var reader=StartCrashProcess("--crash-recover",directory,mode))
            {
                Check(reader.WaitForExit(45000)&&reader.ExitCode==0,"independent process reloads only persisted JSON and settles: "+mode);
                var recovered=BossRushCoopJson.FromJson<CrashRecord>(File.ReadAllText(Path.Combine(directory,"recovery.json")));
                bool success=mode=="success-after";
                Check(recovered.Pid!=checkpoint.Pid&&recovered.Success==success,"new original-host process gives a determinate authenticated result: "+mode);
                Check(recovered.GuestCount==3&&recovered.WorldCount==0&&recovered.RecoveredTickets==(success?1:0),"duplicate reconciliation preserves exact original quantity: "+mode);
                if(success)
                {
                    using(var claimed=StartCrashProcess("--claim-recover",directory,mode))
                    {
                        Check(claimed.WaitForExit(45000)&&claimed.ExitCode==0,"third independent process replays claimed native save: "+mode);
                        var final=BossRushCoopJson.FromJson<CrashRecord>(File.ReadAllText(Path.Combine(directory,"claimed-reload.json")));
                        Check(final.Pid!=recovered.Pid&&final.GuestCount==3&&final.WorldCount==0,"claimed original remains singular across new native save reload with zero reconstructed tickets: "+mode);
                    }
                }
                Console.WriteLine("PC06Crash: mode="+mode+" writer_pid="+checkpoint.Pid+" reader_pid="+recovered.Pid+" guest="+recovered.GuestCount+" terminal="+recovered.State+" directory="+directory);
            }
        }
    }
}
