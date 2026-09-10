using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception("FAIL " + name); }
    private sealed class World : IDisposable
    {
        internal readonly GameObject Root = new GameObject("world");
        internal readonly CharacterMainControl Player = CharacterMainControl.Create();
        internal readonly HashSet<string> Saved = new HashSet<string>();
        internal readonly SkyIslandEncounters Encounters;
        internal bool Valid = true, Accept = true;
        internal int Attempts;
        internal int StormTrophies;
        internal World(string nearby)
        {
            foreach (SkyIslandEncounterDefinition definition in SkyIslandContent.CreateFallback().Encounters)
            {
                var marker = new GameObject(definition.Marker); marker.transform.SetParent(Root.transform, false);
                marker.transform.position = new Vector3(definition.Id == nearby ? 0 : 1000, 0, 0);
            }
            Physics.Ground = Root.transform;
            // 内容表由会话加载一次后传入；夹具走与生产同一条 Load 路径，保证 ContentSource 仍是真实结果。
            Encounters = new SkyIslandEncounters(Root, Player, new Pathfinding.GraphMask(), 1, SkyIslandContent.Load(), () => Valid,
                id => Saved.Contains(id), id => { Attempts++; if (Accept) Saved.Add(id); }, (message, error) => { },
                position => { StormTrophies++; });
        }
        internal void Tick(float seconds = 1) { Time.time += seconds; Encounters.Tick(); }
        public void Dispose() { Valid = false; Encounters.Dispose(); }
    }
    private static void Reset()
    {
        CharacterRandomPreset.Created.Clear(); CharacterRandomPreset.Clones.Clear(); CharacterRandomPreset.Block=null;
        CharacterRandomPreset.MissingHealth=false; SkyIslandResidents.Faces.Clear(); UnityEngine.Object.Delayed.Clear(); Time.time=0;
    }
    private static void Kill(CharacterMainControl character)
    { character.Health.Die(); UnityEngine.Object.Destroy(character.gameObject); }
    private static void Main()
    {
        Reset();
        using (var world = new World("D"))
        {
            Check(world.Encounters.ContentSource == "Json", "formal configuration wired");
            world.Tick(); Check(CharacterRandomPreset.Created.Count == 2, "D first group spawned");
            Check(world.Encounters.HasLivingEnemies, "live enemies hold safety gate");
            Kill(CharacterRandomPreset.Created[0]); world.Tick();
            Check(world.Attempts == 0, "one dead does not finish group");
            Kill(CharacterRandomPreset.Created[1]); world.Accept=false; world.Tick();
            Check(world.Attempts == 1 && !world.Saved.Contains("D"), "failed save retains completed combat");
            Check(!world.Encounters.HasLivingEnemies, "destroyed corpses release combat gate");
            world.Accept=true; world.Tick();
            Check(world.Saved.Contains("D") && world.Attempts == 2, "completion retries after corpse objects destroyed");
            Check(!world.Encounters.IsCleared("D"), "D aggregate waits second group");
            world.Saved.Add("D_02"); Check(world.Encounters.IsCleared("D"), "D aggregate accepts both groups");
            // 口径限于**同一次出击内**：接受后的清场不重投、不补位重生。
            // 跨出击的刷新语义另见下面「returning raid」两组。
            world.Tick(); Check(world.Attempts == 2 && CharacterRandomPreset.Created.Count == 2, "accepted clear never respawns within one raid");
            Check(CharacterRandomPreset.Clones.TrueForAll(p => p.dropBoxOnDead), "official drop path enabled");
            Check(CharacterRandomPreset.Created.TrueForAll(c => c.Team == Teams.wolf), "hostile safety net applied");
            Check(UnityEngine.Object.Delayed.Count == 2, "preset release deferred beyond character destruction");
        }
        // ---- returning raid：老档进岛，自动组必须重新生成 ----
        // 这是「跑通一遍之后全岛零敌人、39 个搜刮点却每趟重刷」那条无风险刷宝路径的堵口。
        // 新 owner + 已填好的持久清场集合 = 第二次出击。
        Reset();
        using (var world = new World("D"))
        {
            world.Saved.Add("D"); world.Saved.Add("D_02");
            Check(world.Encounters.RemainingClearable == 13,
                "saved clears must not shrink contract availability on a later raid");
            world.Tick();
            Check(CharacterRandomPreset.Created.Count == 2, "auto encounters respawn on a later raid");
            Check(world.Encounters.HasLivingEnemies, "a returning raid actually has risk again");
            Check(world.Encounters.HasLivingEnemiesWithin(new Vector3(0, 0, 0), 35f),
                "nearby combat is detectable for the story-panel gate");
            Check(!world.Encounters.HasLivingEnemiesWithin(new Vector3(0, 0, 500), 35f),
                "abandoned distant enemies must not gate the whole island");
            Kill(CharacterRandomPreset.Created[0]); Kill(CharacterRandomPreset.Created[1]); world.Tick();
            Check(world.Attempts == 1, "re-clearing a saved group still credits the contract once");
        }
        // ---- returning raid：具名剧情对手仍是一次性 ----
        Reset();
        using (var world = new World("Zheling"))
        {
            world.Saved.Add("Zheling");
            world.Tick();
            Check(CharacterRandomPreset.Created.Count == 0, "a defeated named foe never returns");
            Check(!world.Encounters.BeginChallenge("Zheling"), "a defeated named foe cannot be re-challenged");
            Check(world.Encounters.IsCleared("Zheling"), "the saved fact still reads as cleared");
            Check(world.Attempts == 0, "a suppressed manual group must not re-submit its clear");
        }
        Reset();
        using (var world = new World("C"))
        {
            world.Tick(); Kill(CharacterRandomPreset.Created[0]);
            UnityEngine.Object.Destroy(CharacterRandomPreset.Created[1].gameObject); world.Tick();
            Check(CharacterRandomPreset.Created.Count == 3, "only missing living slot replaced");
            Kill(CharacterRandomPreset.Created[2]); world.Tick();
            Check(world.Saved.Contains("C"), "lost live object cannot soft lock encounter");
        }
        Reset();
        using (var world = new World("C"))
        {
            CharacterRandomPreset.MissingHealth=true; world.Tick();
            Check(CharacterRandomPreset.Created.Count == 1 && CharacterRandomPreset.Created[0].gameObject == null, "Bind exception destroys unretained character");
            Check(UnityEngine.Object.Delayed.Count == 1, "failed Bind retains preset through destruction");
            CharacterRandomPreset.MissingHealth=false; world.Tick(11);
            Check(CharacterRandomPreset.Created.Count == 3, "partial preparation retries missing actors");
        }
        Reset();
        var delayed = new World("C");
        var pending = new TaskCompletionSource<CharacterMainControl>(); CharacterRandomPreset.Block=pending;
        delayed.Tick(); Check(CharacterRandomPreset.Clones.Count == 1, "one request in flight");
        CharacterRandomPreset cloneInFlight = CharacterRandomPreset.Clones[0]; delayed.Dispose();
        Check(cloneInFlight != null && !UnityEngine.Object.Delayed.Contains(cloneInFlight), "exit does not destroy borrowed preset");
        var late = CharacterMainControl.Create(); pending.SetResult(late);
        Check(late.gameObject == null, "late result reclaimed");
        Check(UnityEngine.Object.Delayed.Contains(cloneInFlight), "late result owns delayed preset cleanup");
        Check(delayed.Attempts == 0, "cancelled generation never commits clear");
        Reset();
        using (var world = new World("Zheling"))
        {
            world.Tick(); Check(CharacterRandomPreset.Created.Count == 0, "manual challenge never auto spawns");
            Check(world.Encounters.BeginChallenge("Zheling"), "explicit challenge starts");
            Check(SkyIslandResidents.Faces.Contains("sky_zheling"), "combat uses same story face");
            Check(!world.Encounters.BeginChallenge("Zheling"), "double challenge rejected");
            Kill(CharacterRandomPreset.Created[0]); world.Tick(); Check(world.Saved.Contains("Zheling"), "challenge submits actual death");
        }
        Console.WriteLine("PASS SkyIslandEncounters: " + checks + " assertions (production owner with Unity / async substitutes)");
    }
}
