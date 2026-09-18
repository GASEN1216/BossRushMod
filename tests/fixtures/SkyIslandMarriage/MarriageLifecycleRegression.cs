using System;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class MarriageLifecycleRegression
{
    private static void Register(string id, CharacterMainControl npc)
    {
        if (id == "goblin") ModBehaviour.Instance.goblinNPCInstance = npc.gameObject;
        else if (id == "nurse") ModBehaviour.Instance.nurseNPCInstance = npc.gameObject;
        else PermanentDuckNpcRegistry.Instances[id] = npc;
    }
    private static void Start(string id, CharacterMainControl npc, bool married)
    {
        if (married) NPCMarriageSystem.HandleRingGiftAccepted(id, npc.transform, null);
        else NPCMarriageSystem.HandleDivorceRequested(id, npc.transform, null);
    }
    internal static void Run(Func<string,CharacterMainControl> create, Func<bool,SkyIslandSession> reset, Action<bool,string> check)
    {
        foreach (string id in new[] { "sky_qinghe", "sky_weibai", "goblin", "nurse" })
        foreach (bool married in new[] { false, true })
        {
            reset(false); var npc = create(id); Register(id, npc);
            AffinityManager.Spouse = married ? null : id;
            Start(id, npc, married);
            check(AffinityManager.IsMarriedToPlayer(id) == married && MarriageAsync.Delays.Count == 1 && npc != null,
                "real entry changes relationship, then waits before cleanup");
            MarriageAsync.FinishDelay(); MarriageAsync.Drain();
            check(npc == null && ModBehaviour.Instance.GetSpouseInstance(id) == null,
                "normal marriage/divorce still cleans the original NPC: " + id);
            check(married ? ModBehaviour.Instance.WeddingAttempts == 1 : ModBehaviour.Instance.Invalidations == 1,
                "normal operation retains relocation/restore invalidation");

            foreach (string boundary in new[] { "scene", "slot", "player", "host", "npc", "dead", "loading", "relationship" })
            {
                reset(false); npc = create(id); Register(id, npc);
                AffinityManager.Spouse = married ? null : id;
                Start(id, npc, married);
                var protectedNpc = npc;
                switch (boundary)
                {
                    case "scene":
                        UnityEngine.Object.Destroy(npc.gameObject);
                        check(npc == null, "scene teardown must destroy original NPC");
                        SceneManager.Current = new Scene { handle = 2, name = "SkyIslandRaid" };
                        protectedNpc = create(id); Register(id, protectedNpc); break;
                    case "slot": Saves.SavesSystem.CurrentSlot++; break;
                    case "player": CharacterMainControl.Main = create("new-player"); break;
                    case "host":
                        // Keep old host alive to prove ownership, not only Unity fake null.
                        ModBehaviour.Instance = new GameObject("next-host").AddComponent<ModBehaviour>(); Register(id, npc); break;
                    case "npc": protectedNpc = create(id); Register(id, protectedNpc); break;
                    case "dead": CharacterMainControl.Main.Health.IsDead = true; break;
                    case "loading": SceneLoader.IsSceneLoading = true; break;
                    case "relationship": AffinityManager.Spouse = married ? "other" : id; break;
                }
                MarriageAsync.FinishDelay(); MarriageAsync.Drain();
                check(protectedNpc != null && ModBehaviour.Instance.GetSpouseInstance(id) == protectedNpc.gameObject,
                    "late operation must preserve current NPC/registry: " + id + "/" + married + "/" + boundary);
                check(ModBehaviour.Instance.Invalidations == 0 && ModBehaviour.Instance.PlaceholderRemovals == 0
                    && ModBehaviour.Instance.WeddingAttempts == 0, "late operation must not touch current restore or wedding state");
            }
        }

        foreach (string id in new[] { "sky_qinghe", "sky_weibai" })
        {
            // Same object, scene, slot and final relationship: only operation generation distinguishes the two weddings.
            reset(false); var npc = create(id); Register(id, npc);
            Start(id, npc, true); Start(id, npc, false); Start(id, npc, true);
            MarriageAsync.FinishDelay(); MarriageAsync.FinishDelay();
            check(npc != null && ModBehaviour.Instance.Invalidations == 0 && ModBehaviour.Instance.WeddingAttempts == 0,
                "new marriage supersedes old marriage and divorce on same NPC");
            MarriageAsync.FinishDelay(); MarriageAsync.Drain(); check(npc == null, "latest marriage still completes");

            reset(false); npc = create(id); Register(id, npc); AffinityManager.Spouse = id;
            Start(id, npc, false); Start(id, npc, true); Start(id, npc, false);
            MarriageAsync.FinishDelay(); MarriageAsync.FinishDelay();
            check(npc != null && ModBehaviour.Instance.Invalidations == 0, "old divorce cannot preempt a newer divorce");
            MarriageAsync.FinishDelay(); MarriageAsync.Drain();
            check(npc == null && ModBehaviour.Instance.Invalidations == 1, "only latest divorce invalidates restore");

            reset(false); npc = create(id); Register(id, npc); Start(id, npc, true); AffinityManager.Following = true;
            MarriageAsync.FinishDelay(); MarriageAsync.Drain();
            check(npc != null && ModBehaviour.Instance.WeddingAttempts == 0, "new follow state survives old wedding relocation");

            reset(false); npc = create(id); Register(id, npc); ModBehaviour.Instance.WeddingTarget = npc.transform;
            Start(id, npc, true); MarriageAsync.FinishDelay(); MarriageAsync.Drain();
            check(npc != null && ModBehaviour.Instance.WeddingAttempts == 1, "base wedding preserves relocated spouse");

            reset(false); npc = create(id); Register(id, npc); MarriageAsync.Video = new TaskCompletionSource<bool>();
            Start(id, npc, true); Saves.SavesSystem.CurrentSlot++;
            check(!MarriageAsync.VideoValid(), "video receives the original operation's validity predicate");
            MarriageAsync.Video.SetResult(false); MarriageAsync.Drain();
            check(MarriageAsync.Delays.Count == 0 && MarriageAsync.Feedback == 0 && MarriageAsync.DialogueCalls == 0,
                "invalid video continuation cannot start text, feedback or relocation");

            reset(false); npc = create(id); Register(id, npc); MarriageAsync.VideoResult = false;
            MarriageAsync.Dialogue = new TaskCompletionSource<bool>(); Start(id, npc, true);
            check(npc != null && MarriageAsync.Feedback == 0, "valid fallback must finish text before feedback");
            MarriageAsync.Dialogue.SetResult(true);
            check(MarriageAsync.Feedback == 1 && MarriageAsync.Delays.Count == 1, "completed fallback starts feedback and delayed relocation");
            MarriageAsync.FinishDelay(); MarriageAsync.Drain(); check(npc == null, "valid fallback completes marriage normally");

            reset(false); npc = create(id); Register(id, npc); MarriageAsync.VideoResult = false;
            MarriageAsync.Dialogue = new TaskCompletionSource<bool>(); Start(id, npc, true);
            check(MarriageAsync.DialogueCalls == 1 && MarriageAsync.Delays.Count == 0, "fallback waits for actual text sequence");
            UnityEngine.Object.Destroy(npc.gameObject); MarriageAsync.Drain();
            check(MarriageAsync.Dialogue.Task.IsCanceled && MarriageAsync.Feedback == 0 && MarriageAsync.Delays.Count == 0
                && MarriageAsync.ForceEnds == 0, "destroyed wedding speaker cancels only its own dialogue and skips feedback");
        }
    }
}
