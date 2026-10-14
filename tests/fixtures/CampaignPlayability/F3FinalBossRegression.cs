using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;

namespace BossRush
{
    internal static class BossBgmCoordinator { internal static int ActiveOwnerLeaseCount; }
    public partial class ModBehaviour
    {
        internal void ValidationSafeCleanup() { CleanupCampaignFinalBoss(true); }
    }
    internal sealed partial class F3GameplayValidationRunner
    {
        private readonly ModBehaviour _host;
        private const float CaseTimeoutSeconds = 5f;
        internal string Outcome, Reason, Metrics;
        internal F3GameplayValidationRunner(ModBehaviour owner) { _host = owner; }
        private bool ShouldAbort() { return false; }
        private void Record(string id, string outcome, long elapsed, string metrics, string reason)
        { Outcome = outcome; Reason = reason; Metrics = metrics; }
        private IEnumerator WaitSeconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }
        internal IEnumerator Begin() { return RunCampaignFinalBoss(); }
    }
}

internal static class F3FinalBossRegression
{
    private static F3GameplayValidationRunner Prepare(out ModBehaviour owner, out CharacterMainControl boss)
    {
        BossRushCoop.IsConnected = BossRushCoop.IsHost = false;
        Time.realtimeSinceStartup = 0f;
        SceneLoader.IsSceneLoading = false;
        LevelManager.AfterInit = true;
        UnityEngine.SceneManagement.SceneManager.ActiveHandle++;
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health() };
        boss = new CharacterMainControl { Health = new Health { Invincible = true, OnDeadEvent = new DeathEvent() } };
        boss.gameObject = new GameObject { Character = boss };
        owner = new ModBehaviour { Arena = true, SpawnResult = new TaskCompletionSource<CharacterMainControl>() };
        ModBehaviour.Instance = owner;
        owner.SpawnResult.SetResult(boss);
        return new F3GameplayValidationRunner(owner);
    }
    private static void Finish(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine);
        int frames = 0;
        while (stack.Count > 0)
        {
            if (++frames > 100) throw new Exception("F3 final case did not terminate");
            IEnumerator current = stack.Peek();
            if (!current.MoveNext()) { stack.Pop(); continue; }
            IEnumerator nested = current.Current as IEnumerator;
            if (nested != null) stack.Push(nested);
            else Time.realtimeSinceStartup += 0.25f;
        }
    }
    internal static void Run(Action<bool, string> check)
    {
        ModBehaviour owner; CharacterMainControl boss;
        var runner = Prepare(out owner, out boss); var routine = runner.Begin();
        check(routine.MoveNext() && boss.Health.HurtCalls == 0 && runner.Outcome == null,
            "F3 waits through the real boss entrance instead of wasting its only Hurt");
        Time.realtimeSinceStartup = 1.1f; boss.Health.Invincible = false; Finish(routine);
        check(runner.Outcome == "PASS" && boss.Health.HurtCalls == 1 && owner.CampaignFinalBossDeathPresentationCount == 1
            && runner.Metrics.Contains("hurt_accepted=True"), "F3 accepted official Hurt reaches one real campaign death callback");

        runner = Prepare(out owner, out boss); Finish(runner.Begin());
        check(runner.Outcome == "FAIL" && runner.Reason == "boss_not_damage_ready" && boss.Health.HurtCalls == 0,
            "a permanently invincible boss remains a failing case");

        runner = Prepare(out owner, out boss); routine = runner.Begin(); routine.MoveNext(); SceneLoader.IsSceneLoading = true; Finish(routine);
        check(runner.Outcome == "FAIL" && boss.Health.HurtCalls == 0, "real scene load invalidates the waiting final case");

        runner = Prepare(out owner, out boss); routine = runner.Begin(); routine.MoveNext(); UnityEngine.Object.Destroy(boss.gameObject); Finish(routine);
        check(runner.Outcome == "FAIL" && boss.Health.HurtCalls == 0, "destroyed boss cannot be attacked by the old case");

        runner = Prepare(out owner, out boss); routine = runner.Begin(); routine.MoveNext(); owner.CleanupCampaignFinalBoss(false); Finish(routine);
        check(runner.Outcome == "FAIL" && boss.Health.HurtCalls == 0, "ended final owner invalidates its waiting attack");

        runner = Prepare(out owner, out boss); boss.Health.Invincible = false; boss.Health.AcceptHurt = false; Finish(runner.Begin());
        check(runner.Outcome == "FAIL" && runner.Reason == "official_hurt_rejected" && boss.Health.HurtCalls == 1
            && owner.CampaignFinalBossDeathPresentationCount == 0, "a rejected official Hurt is reported explicitly without fabricated death");
    }
}
