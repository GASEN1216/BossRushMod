using System;
using System.Collections;
using System.Reflection;
using BossRush;
using UnityEngine;
using Bubbles = Duckov.UI.DialogueBubbles.DialogueBubblesManager;

// 执行真实遭遇 owner、气泡调度和 BossVoice；只替代 Unity/官方显示入口。
internal static partial class Program
{
    private static readonly BindingFlags ChatterPrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void ResetChatterWorld()
    {
        Reset();
        Bubbles.Instance = new GameObject().AddComponent<Bubbles>();
        Bubbles.Instance.isActiveAndEnabled = true;
        Bubbles.Fail = Bubbles.ThrowSynchronously = false;
        Bubbles.ImmediateResult = null;
        Bubbles.Lines.Clear(); Bubbles.Shown = 0;
        BossRushUI.Paused = DialogueManager.IsDialogueActive = false;
        L10n.IsChinese = true;
    }

    private static SkyIslandEnemyRecord[] ChatterActors(World world)
    {
        foreach (object group in (IEnumerable)typeof(SkyIslandEncounters).GetField("encounters", ChatterPrivate).GetValue(world.Encounters))
        {
            if ((string)group.GetType().GetField("Id", ChatterPrivate).GetValue(group) != "D") continue;
            var actors = (SkyIslandEnemyRecord[])group.GetType().GetField("Actors", ChatterPrivate).GetValue(group);
            foreach (SkyIslandEnemyRecord actor in actors) actor.Silent = true;
            actors[1].Silent = false;
            return actors;
        }
        throw new Exception("D encounter missing");
    }

    private static bool LastMobLine(SkyIslandChatterMoment moment)
    {
        return Bubbles.Lines.Count > 0 && Array.IndexOf(SkyIslandChatterLines.Scav(moment), Bubbles.Lines[Bubbles.Lines.Count - 1]) >= 0;
    }

    private static void AdvanceVoice(SkyIslandBossVoice voice, float time)
    {
        Time.time = time;
        typeof(SkyIslandBossVoice).GetMethod("Update", ChatterPrivate).Invoke(voice, null);
    }

    private static void CheckChatterBehavior()
    {
        // 事件的过期是硬边界，重复观测不能续命；新进度合并旧进度。
        var pending = new SkyIslandChatterEvent();
        pending.Observe(1, 0f);
        pending.Observe(1, 11f);
        Check(pending.Pending(11.9f) && !pending.Pending(12f), "event expires at boundary without renewal");
        pending.Observe(1, 13f);
        Check(!pending.Pending(13f), "expired event never replays from same observation");
        pending.Observe(2, 14f); pending.Consume();
        Check(!pending.Pending(14f), "successful event consumed once");
        pending.Observe(3, 15f); pending.Observe(4, 16f); pending.Consume();
        Check(!pending.Pending(16f), "newest event replaces old backlog");

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            world.Tick();
            Check(LastMobLine(SkyIslandChatterMoment.Noticed), "visual/forced target emits first notice without sound flag");
            int count = Bubbles.Shown;
            world.Tick(70);
            Check(Bubbles.Shown == count, "sustained combat never picks idle lines");
            actors[1].Ai.searchedEnemy = CharacterMainControl.Create().mainDamageReceiver;
            world.Tick(70);
            Check(Bubbles.Shown == count, "fighting another faction also suppresses idle");
            actors[1].Ai.searchedEnemy = null;
            actors[1].Ai.noticed = true; actors[1].Ai.noticeTime = Time.time;
            actors[1].Ai.NoticeFromCharacter = CharacterMainControl.Create();
            world.Tick();
            Check(Bubbles.Shown == count, "recent noise suppresses idle but does not announce player");
            world.Tick(13);
            Check(Bubbles.Shown == count + 1 && LastMobLine(SkyIslandChatterMoment.Idle), "idle resumes after disengagement, despite sticky noticed");
        }

        // 闲话的 35 秒冷却不会把事件拖到过期；失败也不消费。
        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world); world.Tick();
            Check(LastMobLine(SkyIslandChatterMoment.Idle), "idle baseline sent");
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            world.Tick();
            Check(Bubbles.Shown == 1 && actors[1].Notice.Pending(Time.time), "busy budget retains notice");
            Bubbles.Fail = true; world.Tick(6);
            Check(Bubbles.Shown == 1 && actors[1].Notice.Pending(Time.time), "failed official display retains notice");
            Bubbles.Fail = false; L10n.IsChinese = false; world.Tick();
            Check(Bubbles.Shown == 2 && LastMobLine(SkyIslandChatterMoment.Noticed), "retry uses current language and short event cooldown");
            Check(!actors[1].Notice.Pending(Time.time), "only delivered notice is consumed");
            actors[1].Ai.searchedEnemy = null; world.Tick(7);
            Check(Bubbles.Shown == 2, "event does not shorten subsequent idle cooldown");
            world.Tick(60);
            Check(Bubbles.Shown == 3 && LastMobLine(SkyIslandChatterMoment.Idle), "idle eventually resumes after event cooldown");
        }

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            actors[2].Silent = false;
            actors[1].Ai.searchedEnemy = actors[2].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            world.Tick();
            Check(Bubbles.Shown == 1 && !actors[1].Notice.Pending(Time.time) && actors[2].Notice.Pending(Time.time), "unselected simultaneous notice remains pending");
            world.Tick(6);
            Check(Bubbles.Shown == 2 && !actors[2].Notice.Pending(Time.time), "second eligible actor gets its turn");
        }

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            world.Encounters.ValidationChatter.TrySay(new GameObject().transform, 2f, new[] { "busy" });
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            actors[2].Life.GetComponent<CharacterMainControl>().Health.Die();
            world.Tick();
            Check(Bubbles.Shown == 1, "busy budget emits neither event");
            world.Tick(3);
            Check(Bubbles.Shown == 2 && LastMobLine(SkyIslandChatterMoment.AllyDown), "ally death survives busy budget and wins priority");
            world.Tick(6);
            Check(Bubbles.Shown == 3 && LastMobLine(SkyIslandChatterMoment.Noticed), "lower priority notice survives until its turn");
            world.Tick(60);
            Check(Bubbles.Shown == 3, "unchanged corpse count and notice do not replay in combat");
        }

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            actors[1].Life.transform.position = new Vector3(30, 0, 0);
            world.Tick();
            Check(actors[1].Notice.Pending(Time.time) && Bubbles.Shown == 0, "range rejection keeps event");
            world.Tick(SkyIslandChatter.EventLifetime);
            actors[1].Life.transform.position = new Vector3(0, 0, 0); world.Tick();
            Check(Bubbles.Shown == 0, "expired out-of-range event is not shouted late");
        }

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            DialogueManager.IsDialogueActive = true; world.Tick();
            Check(Bubbles.Shown == 0, "dialogue silences dispatch");
            DialogueManager.IsDialogueActive = false; BossRushUI.Paused = true; world.Tick();
            Check(Bubbles.Shown == 0, "pause silences dispatch");
            BossRushUI.Paused = false; world.Tick();
            Check(LastMobLine(SkyIslandChatterMoment.Noticed), "muted notice is available after resume");
            // 同一槽位的旧实例销毁，新实例有全新的待播状态。
            UnityEngine.Object.Destroy(actors[1].Life.gameObject); world.Tick(6);
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver; world.Tick(6);
            Check(LastMobLine(SkyIslandChatterMoment.Noticed) && Bubbles.Shown == 2, "replacement actor does not inherit consumed notice");
        }

        ResetChatterWorld();
        using (var world = new World("D"))
        {
            world.Tick(); var actors = ChatterActors(world);
            Bubbles.Fail = true;
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver;
            actors[1].Ai.NoticeFromCharacter = world.Player;
            actors[1].Ai.noticed = true; actors[1].Ai.noticeTime = Time.time;
            world.Tick();
            Check(actors[1].Notice.Pending(Time.time), "notice waits on failed display");
            actors[1].Ai.searchedEnemy = CharacterMainControl.Create().mainDamageReceiver;
            Bubbles.Fail = false; world.Tick();
            Check(Bubbles.Shown == 0 && !actors[1].Notice.Pending(Time.time), "new target overrides old player sound and discards obsolete notice");
            actors[1].Ai.searchedEnemy = world.Player.mainDamageReceiver; world.Tick();
            Check(Bubbles.Shown == 0, "discarded first notice does not replay after retargeting");
        }

        CheckBossVoiceBehavior();
        ResetChatterWorld();
    }

    private static void CheckBossVoiceBehavior()
    {
        ResetChatterWorld();
        var boss = CharacterMainControl.Create();
        CharacterMainControl.Main = CharacterMainControl.Create();
        boss.GetComponent<AICharacterController>().searchedEnemy = CharacterMainControl.Main.mainDamageReceiver;
        var chatter = new SkyIslandChatter(35f, 60f, () => true);
        bool valid = true;
        var context = new SkyIslandBossContext { Valid = () => valid,
            Bark = (speaker, height, pool, force) => chatter.TrySay(speaker, height, pool, force, SkyIslandChatter.BossCooldown) };
        var voice = boss.gameObject.AddComponent<SkyIslandBossVoice>();
        voice.BindChampion(boss, "zheling", context);
        voice.BindChampion(boss, "zheling", context);
        Check(boss.Health.OnHurtEvent.ListenerCount == 1 && boss.Health.OnDeadEvent.ListenerCount == 1, "voice rebinding is idempotent");
        AdvanceVoice(voice, 0);
        Check(Bubbles.Shown == 1, "boss opening follows current target without heard sound");
        Time.time = 1; boss.Health.CurrentHealth = 55; boss.Health.OnHurtEvent.Invoke();
        Time.time = 2; boss.Health.CurrentHealth = 25; boss.Health.OnHurtEvent.Invoke();
        Check(Bubbles.Shown == 1, "both fast thresholds wait for opening cooldown");
        AdvanceVoice(voice, 6);
        Check(Bubbles.Shown == 2 && Array.IndexOf(SkyIslandChatterLines.Champion("zheling", SkyIslandChatterMoment.Wounded), Bubbles.Lines[1]) >= 0,
            "latest bloodline delivered by Update without another hurt");
        AdvanceVoice(voice, 20);
        Check(Bubbles.Shown == 2, "merged thresholds do not create stale second line");
        boss.Health.CurrentHealth = 20; boss.Health.OnHurtEvent.Invoke();
        Check(Bubbles.Shown == 2, "repeated hurt under same threshold does not replay");

        voice.BindChampion(boss, "zheling", context);
        chatter.Clear(); Bubbles.Lines.Clear(); Bubbles.Shown = 0;
        boss.Health.CurrentHealth = 55; boss.Health.OnHurtEvent.Invoke();
        Check(Bubbles.Shown == 1, "slow first threshold can speak immediately");
        Time.time = 27; boss.Health.CurrentHealth = 25; boss.Health.OnHurtEvent.Invoke();
        Check(Bubbles.Shown == 2, "separate second threshold speaks once");

        voice.BindChampion(boss, "zheling", context);
        Bubbles.Fail = true; Time.time = 30; boss.Health.CurrentHealth = 55; boss.Health.OnHurtEvent.Invoke();
        Bubbles.Fail = false; valid = false; AdvanceVoice(voice, 33);
        Check(Bubbles.Shown == 2, "invalid boss context does not dispatch pending line");
        valid = true; AdvanceVoice(voice, 34);
        Check(Bubbles.Shown == 3, "display failure and temporary invalid context did not consume line");
        Time.time = 35; boss.Health.CurrentHealth = 25; boss.Health.OnHurtEvent.Invoke();
        boss.Health.Die();
        Check(Bubbles.Shown == 4 && Array.IndexOf(SkyIslandChatterLines.Champion("zheling", SkyIslandChatterMoment.Down), Bubbles.Lines[3]) >= 0,
            "death overrides own cooldown and discards pending wounded line");
        AdvanceVoice(voice, 60);
        Check(Bubbles.Shown == 4, "dead boss cannot resume pending wounded line");
        UnityEngine.Object.Destroy(boss.gameObject);
        Check(boss.Health.OnHurtEvent.ListenerCount == 0 && boss.Health.OnDeadEvent.ListenerCount == 0, "destroy detaches both health listeners");

        ResetChatterWorld();
        boss = CharacterMainControl.Create(); voice = boss.gameObject.AddComponent<SkyIslandBossVoice>();
        chatter.Clear(); voice.BindChampion(boss, "zheling", context);
        Bubbles.Fail = true; boss.Health.CurrentHealth = 20; boss.Health.OnHurtEvent.Invoke();
        Bubbles.Fail = false; AdvanceVoice(voice, SkyIslandChatter.EventLifetime);
        Check(Bubbles.Shown == 0, "expired bloodline does not speak after display recovers");
    }
}
