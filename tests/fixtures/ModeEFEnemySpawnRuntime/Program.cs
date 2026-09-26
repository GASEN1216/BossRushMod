using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Random = UnityEngine.Random;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label) { if (!value) throw new Exception(label); checks++; }
    private static EnemyPresetInfo Preset(string name,Teams team,float health=100)
    { return new EnemyPresetInfo { name=name,displayName=name,team=(int)team,baseHealth=health }; }
    private sealed class Session
    {
        internal List<EnemyPresetInfo> Bosses = new List<EnemyPresetInfo>();
        internal List<EnemyPresetInfo> Minions = new List<EnemyPresetInfo>();
        internal readonly ModeEFSpawnPreparation Preparation = new ModeEFSpawnPreparation();
        internal readonly EnemySpawnRuntime Core = new EnemySpawnRuntime();
        internal readonly ModeEFEnemySpawnRuntime Runtime;
        internal readonly List<string> Events = new List<string>();
        internal int Token=7, Classifications;
        internal bool Active=true, InitThrows;
        internal Teams LastFaction;
        internal bool LastPromoted;
        internal Session()
        {
            UniTask.Reset(); Random.Reset(); SpawnPositionHelper.Calls.Clear(); ModBehaviour.Logs.Clear();
            CharacterMainControl.Main=new CharacterMainControl();
            Runtime=new ModeEFEnemySpawnRuntime(Preparation,Core);
            Runtime.BindPresetQueries(() => Bosses,() => Minions,
                p => { Classifications++; return p!=null && p.name=="king"; },p => p!=null && p.name=="dragon",
                () => { Events.Add("arena"); if (InitThrows) throw new Exception("init"); },() => Events.Add("minions"));
            Runtime.BindSpawnCallbacks((ft,fs,et,es) => Active && ((ft==0 && et==0) || ft==Token || et==Token),
                (ctx,faction,promoted) => { LastFaction=faction; LastPromoted=promoted; Events.Add("commit:"+ctx.preset.name);
                    Runtime.ResolveModeESpawnAttempt();return true; });
        }
        internal Task Start(Teams team,params float[] points)
        {
            Preparation.SpawnAllocation[team]=points.Select(p=>new Vector3(p)).ToList();
            Preparation.Flattened=Preparation.SpawnAllocation.Values.SelectMany(v=>v).ToArray();
            return Runtime.ModeESpawnAllBosses(Token,17);
        }
        internal void Drain(Task task)
        { while (UniTask.Pending.Count>0) UniTask.Advance(); task.GetAwaiter().GetResult(); }
    }
    private static void CacheAndClassification()
    {
        var s=new Session();var wolf=Preset("wolf",Teams.wolf);var minion=Preset("m",Teams.scav);
        s.Bosses.AddRange(new[] { wolf,Preset("king",Teams.wolf),Preset("dragon",Teams.wolf),null,Preset("",Teams.wolf) });
        s.Minions.Add(minion);
        s.Runtime.EnsureModeEFSpawnPoolsReady("test");
        Check(string.Join(",",s.Events)=="arena,minions","cache preparation preserves initializer order");
        Check(s.Runtime.ContainsBossPreset(Preset("wolf",Teams.wolf)),"cache identity accepts final preset name clone");
        Check(!s.Runtime.ContainsBossPreset(Preset("king",Teams.wolf)),"king excluded from all faction boss caches");
        Check(s.Runtime.ContainsMinionPreset(minion),"minions remain in independent source pool");
        int classified=s.Classifications;s.Runtime.BuildModeEFactionPresetCaches();
        Check(s.Classifications==classified,"same source identity and counts reuse faction cache");
        s.Bosses.Add(Preset("added",Teams.usec));s.Runtime.BuildModeEFactionPresetCaches();
        Check(s.Runtime.ContainsBossPreset(Preset("added",Teams.usec)),"source count change rebuilds faction cache");
        s.Bosses=new List<EnemyPresetInfo>{Preset("replacement",Teams.bear)};s.Runtime.BuildModeEFactionPresetCaches();
        Check(!s.Runtime.ContainsBossPreset(wolf) && s.Runtime.ContainsBossPreset(s.Bosses[0]),"new source identity rebuilds cache");
        s.InitThrows=true;s.Events.Clear();s.Runtime.EnsureModeEFSpawnPoolsReady("failure");
        Check(s.Events.SequenceEqual(new[]{"arena"}) && ModBehaviour.Logs.Any(x=>x.Contains("failure")),"prewarm exception preserves stopped chain and diagnostic");
    }
    private static void CadenceAndAccounting()
    {
        var s=new Session();s.Bosses.Add(Preset("ordinary",Teams.scav));
        Task run=s.Start(Teams.scav,500,100,400,200,300);
        Check(s.Runtime.TotalSpawnExpected==5 && s.Runtime.SpawnResolved==1,"all pending tasks count before first delay");
        Check(!run.IsCompleted && s.Core.Requests.Count==1,"first spawn yields before further dispatch");
        s.Drain(run);
        Check(s.Core.Requests.Select(x=>x.Position.x).SequenceEqual(new float[]{100,200,300,400,500}),"spawn tasks sorted nearest first");
        Check(UniTask.Delays.SequenceEqual(new[]{800,800,800,500}),"opening and later delays preserved exactly");
        Check(s.Runtime.SpawnResolved==5 && s.Core.Requests.Count==5,"each counted spawn commits exactly once");
        Check(Random.Calls.Count==5,"one random boss selection per dispatch");
        Check(s.Core.Requests.All(x=>x.Boss && x.SkipDragon && x.SkipKing && x.Deferred && x.Wave==1 && x.Equipment && x.Multiplier && x.Normalize && !x.SkipLoot),"shared core options preserve gear multiplier loot and activation");
        s.Runtime.ResolveModeESpawnAttempt();Check(s.Runtime.SpawnResolved==5,"completion saturates at expected count");
        s.Runtime.DragonKingSpawned=true;s.Runtime.DragonDescendantSpawned=true;s.Runtime.ResetSpawnTracking();
        Check(s.Runtime.TotalSpawnExpected==0 && s.Runtime.SpawnResolved==0 && !s.Runtime.DragonKingSpawned && !s.Runtime.DragonDescendantSpawned,"shared reset clears count and reservations");
        Check(s.Runtime.ContainsBossPreset(s.Bosses[0]),"reset preserves existing preset cache lifetime");
    }
    private static void FactionsAndRandomness()
    {
        var s=new Session();s.Bosses.Add(Preset("wolf",Teams.wolf));s.Minions.AddRange(new[]{Preset("weak",Teams.wolf,1),Preset("strong",Teams.wolf,9)});
        Random.Fraction=.8f;Task run=s.Start(Teams.wolf,100,200);s.Drain(run);
        Check(s.Core.Requests.Select(x=>x.Preset.name).SequenceEqual(new[]{"wolf","strong"}),"wolf fills boss allocation then weighted promoted minion");
        Check(s.LastPromoted && s.LastFaction==Teams.wolf,"promotion and requested faction reach final commit");
        Check(Random.Calls.SequenceEqual(new[]{"int:0:1","float:0:10"}),"weighted minion consumes original float roll only");
        s=new Session();s.Minions.Add(Preset("solo",Teams.wolf));s.Drain(s.Start(Teams.wolf,100));
        Check(Random.Calls.Count==0 && s.LastPromoted,"single weighted minion requires no random draw");
        s=new Session();s.Minions.Add(Preset("foreign",Teams.scav));s.Drain(s.Start(Teams.bear,100));
        Check(s.Core.Requests.Single().Preset.name=="foreign" && s.LastFaction==Teams.bear && s.LastPromoted,"bear uses all-faction minion fallback retaining bear faction");
        s=new Session();s.Minions.Add(Preset("m",Teams.usec));s.Drain(s.Start(Teams.usec,100));
        Check(s.LastPromoted && Random.Calls.SequenceEqual(new[]{"int:0:1"}),"non-wolf minion fallback preserves integer random");
    }
    private static void FailureAndLateCallbacks()
    {
        var s=new Session();s.Drain(s.Start(Teams.scav,100,200));
        Check(s.Runtime.TotalSpawnExpected==2 && s.Runtime.SpawnResolved==2 && s.Core.Requests.Count==0,"missing preset resolves precounted opening pressure");
        s.Runtime.SpawnSingleModeEBoss(Teams.scav,new Vector3(100));
        Check(s.Runtime.TotalSpawnExpected==2 && s.Runtime.SpawnResolved==2,"uncounted missing-preset request does not invent pressure");
        s=new Session();s.Bosses.Add(Preset("dragon",Teams.scav));s.Core.Throw=true;s.Drain(s.Start(Teams.scav,100));
        Check(!s.Runtime.DragonDescendantSpawned && s.Runtime.SpawnResolved==1,"synchronous factory failure clears reserved dragon and resolves attempt");
        s=new Session();s.Bosses.Add(Preset("dragon",Teams.scav));s.Core.Fail=true;s.Drain(s.Start(Teams.scav,100));
        Check(!s.Runtime.DragonDescendantSpawned && s.Runtime.SpawnResolved==1,"factory failure callback resolves and releases dragon reservation");
        s=new Session();s.Bosses.Add(Preset("dragon",Teams.scav));s.Core.AutoCommit=false;s.Drain(s.Start(Teams.scav,100));
        var old=s.Core.Requests.Single();Check(!old.SkipDragon && s.Runtime.DragonDescendantSpawned,"dragon candidate reserves shared slot before asynchronous core");
        s.Token++;s.Runtime.ResetSpawnTracking();s.Runtime.DragonDescendantSpawned=true;old.Failed();
        Check(s.Runtime.DragonDescendantSpawned && s.Runtime.SpawnResolved==0,"stale failure cannot reset new run reservation or accounting");
        Check(!old.Active(),"captured old session predicate invalidates core request");
        s=new Session();s.Bosses.Add(Preset("dragon",Teams.scav));s.Core.ActualPreset=Preset("retry",Teams.scav);s.Drain(s.Start(Teams.scav,100));
        Check(!s.Runtime.DragonDescendantSpawned && s.Events.Last()=="commit:retry","retry final preset releases reservation before commit registration");
        s.Runtime.SyncModeEDragonDescendantSpawnFlag(false,Preset("dragon",Teams.scav),"F");
        Check(s.Runtime.DragonDescendantSpawned,"F shares final dragon reservation update");
    }
    private static void CancellationAndPosition()
    {
        var s=new Session();s.Bosses.Add(Preset("ordinary",Teams.scav));Task run=s.Start(Teams.scav,100,200,300);
        s.Active=false;s.Drain(run);
        Check(s.Core.Requests.Count==1 && UniTask.Delays.SequenceEqual(new[]{800}),"invalidated session stops remaining dispatch after original pending delay");
        s=new Session();s.Bosses.Add(Preset("ordinary",Teams.scav));s.Drain(s.Start(Teams.scav,10,120));
        Check(s.Core.Requests.First().Position.x==120 && SpawnPositionHelper.Calls.First()=="safe:2:0","near player uses same faction points for safe placement");
        s=new Session();s.Bosses.Add(Preset("ordinary",Teams.scav));s.Preparation.Flattened=new[]{new Vector3(130)};
        s.Runtime.SpawnSingleModeEBoss(Teams.scav,new Vector3(10));
        Check(s.Core.Requests.Single().Position.x==130 && s.Runtime.TotalSpawnExpected==1,"single dispatch without faction points uses flattened fallback and counts once");
        s=new Session();s.Bosses.Add(Preset("ordinary",Teams.scav));CharacterMainControl.Main.transform.position=new Vector3(500);
        UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject);s.Runtime.SpawnSingleModeEBoss(Teams.scav,new Vector3(100));
        Check(SpawnPositionHelper.Calls.Single()=="ground:100","destroyed Unity player falls back to zero position");
    }
    private static void Main()
    {
        CacheAndClassification();CadenceAndAccounting();FactionsAndRandomness();FailureAndLateCallbacks();CancellationAndPosition();
        Console.WriteLine("ModeEFEnemySpawnRuntime: "+checks+" assertions PASS");
    }
}
