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
        internal readonly List<string> DefeatedIds = new List<string>();
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
                id => Saved.Contains(id), id => { Attempts++; if (Accept) Saved.Add(id); }, (message, error) => { }, id => id,
                (id, position) => { StormTrophies++; DefeatedIds.Add(id); });
        }
        internal void Tick(float seconds = 1) { Time.time += seconds; Encounters.Tick(); }
        public void Dispose() { Valid = false; Encounters.Dispose(); }
    }
    /// <summary>
    /// 一个自动组几个人、一共几组能清——**从内容表读，不在这里写第二份数字**。
    /// 2026-09-13 自动组人数 2→3、组数 16→21 时，写死的断言会全线崩；
    /// 这两个常量让夹具跟着生产表走，只在**行为**变了时才该红。
    /// </summary>
    private static int GroupSize(string id)
    {
        SkyIslandEncounterDefinition definition =
            Array.Find(SkyIslandContent.CreateFallback().Encounters, e => e.Id == id);
        return definition == null ? 0 : definition.Count;
    }
    private static int AutoGroups()
    {
        int count = 0;
        foreach (SkyIslandEncounterDefinition definition in SkyIslandContent.CreateFallback().Encounters)
            if (!definition.Manual) count++;
        return count;
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
            world.Tick(); Check(CharacterRandomPreset.Created.Count == GroupSize("D"), "D first group spawned");
            Check(world.Encounters.HasLivingEnemies, "live enemies hold safety gate");
            // 杀到只剩一个：组没清完就不该提交。人数从内容表读，改人数时这里不用跟着改。
            for (int i = 0; i < GroupSize("D") - 1; i++) Kill(CharacterRandomPreset.Created[i]);
            world.Tick();
            Check(world.Attempts == 0, "one dead does not finish group");
            Kill(CharacterRandomPreset.Created[GroupSize("D") - 1]); world.Accept=false; world.Tick();
            Check(world.Attempts == 1 && !world.Saved.Contains("D"), "failed save retains completed combat");
            Check(!world.Encounters.HasLivingEnemies, "destroyed corpses release combat gate");
            world.Accept=true; world.Tick();
            Check(world.Saved.Contains("D") && world.Attempts == 2, "completion retries after corpse objects destroyed");
            Check(!world.Encounters.IsCleared("D"), "D aggregate waits second group");
            world.Saved.Add("D_02"); Check(world.Encounters.IsCleared("D"), "D aggregate accepts both groups");
            // 口径限于**同一次出击内**：接受后的清场不重投、不补位重生。
            // 跨出击的刷新语义另见下面「returning raid」两组。
            world.Tick(); Check(world.Attempts == 2 && CharacterRandomPreset.Created.Count == GroupSize("D"), "accepted clear never respawns within one raid");
            Check(CharacterRandomPreset.Clones.TrueForAll(p => p.dropBoxOnDead), "official drop path enabled");
            Check(CharacterRandomPreset.Created.TrueForAll(c => c.Team == Teams.wolf), "hostile safety net applied");
            Check(UnityEngine.Object.Delayed.Count == GroupSize("D"), "preset release deferred beyond character destruction");
        }
        // ---- returning raid：老档进岛，自动组必须重新生成 ----
        // 这是「跑通一遍之后全岛零敌人、39 个搜刮点却每趟重刷」那条无风险刷宝路径的堵口。
        // 新 owner + 已填好的持久清场集合 = 第二次出击。
        Reset();
        using (var world = new World("D"))
        {
            world.Saved.Add("D"); world.Saved.Add("D_02");
            Check(world.Encounters.RemainingClearable == AutoGroups(),
                "saved clears must not shrink contract availability on a later raid");
            world.Tick();
            Check(CharacterRandomPreset.Created.Count == GroupSize("D"), "auto encounters respawn on a later raid");
            Check(world.Encounters.HasLivingEnemies, "a returning raid actually has risk again");
            Check(world.Encounters.HasLivingEnemiesWithin(new Vector3(0, 0, 0), 35f),
                "nearby combat is detectable for the story-panel gate");
            Check(!world.Encounters.HasLivingEnemiesWithin(new Vector3(0, 0, 500), 35f),
                "abandoned distant enemies must not gate the whole island");
            for (int i = 0; i < CharacterRandomPreset.Created.Count; i++) Kill(CharacterRandomPreset.Created[i]);
            world.Tick();
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
            // 只补「丢了 owner 且没死」的那一个槽：死掉的不补，所以总创建数 = 组人数 + 1。
            Check(CharacterRandomPreset.Created.Count == GroupSize("C") + 1, "only missing living slot replaced");
            for (int i = 2; i < CharacterRandomPreset.Created.Count; i++) Kill(CharacterRandomPreset.Created[i]);
            world.Tick();
            Check(world.Saved.Contains("C"), "lost live object cannot soft lock encounter");
        }
        Reset();
        using (var world = new World("C"))
        {
            CharacterRandomPreset.MissingHealth=true; world.Tick();
            Check(CharacterRandomPreset.Created.Count == 1 && CharacterRandomPreset.Created[0].gameObject == null, "Bind exception destroys unretained character");
            Check(UnityEngine.Object.Delayed.Count == 1, "failed Bind retains preset through destruction");
            CharacterRandomPreset.MissingHealth=false; world.Tick(11);
            // 第一次 Bind 失败销毁了 1 个，重试补齐整组 → 1 + 组人数。
            Check(CharacterRandomPreset.Created.Count == GroupSize("C") + 1, "partial preparation retries missing actors");
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
        // ---- 2026-09-14 噬风·回响：同一处风眼、独立的遭遇 id；本体倒下的回调带上 id，编排以回响模式绑定 ----
        Reset();
        using (var world = new World("Storm"))
        {
            world.Saved.Add("Storm");
            world.Tick();
            Check(!world.Encounters.BeginChallenge("Storm"), "the first Windeater fight stays one-shot once its fact is saved");
            int bound = SkyIslandStormBoss.Bound, echoBound = SkyIslandStormBoss.EchoBound;
            Check(world.Encounters.BeginChallenge(SkyIslandStormEchoRules.EncounterId),
                "the echo is its own manual group, so a saved first fight does not read it as cleared");
            Check(SkyIslandStormBoss.Bound == bound + 1 && SkyIslandStormBoss.EchoBound == echoBound + 1,
                "the echo lead binds the shared storm choreography in echo mode (and only the lead)");
            Check(!world.Encounters.BeginChallenge(SkyIslandStormEchoRules.EncounterId), "the echo cannot be started twice in one raid");
            SkyIslandStormBoss.LastDefeated();
            Check(world.DefeatedIds.Count == 1 && world.DefeatedIds[0] == SkyIslandStormEchoRules.EncounterId,
                "the defeat callback carries the echo id so the session can route its cache");
            Check(world.Encounters.IsBusy(SkyIslandStormEchoRules.EncounterId), "a live echo group counts as busy (it drives the returning gale)");
        }
        Console.WriteLine("PASS SkyIslandEncounters: " + checks + " assertions (production owner with Unity / async substitutes)");
    }
}
