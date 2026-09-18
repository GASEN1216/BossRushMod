using System;
using BossRush;

class Program
{
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    static void Kill(string key)
    {
        var target = new Health
        {
            IsDead = true,
            Character = new CharacterMainControl
            {
                isBossCharacter = true, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = key }
            }
        };
        CodexKillCollector.OnGlobalDead(target, new DamageInfo
        {
            fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 100
        });
    }

    static void Main()
    {
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        int initial = CodexBossCatalog.Count;
        Check(initial == 8, "production catalog includes custom and zombie entries");
        for (int i = 0; i < initial - 1; i++)
            CodexPersistence.Current.GetOrCreate(CodexBossCatalog.All[i].Key, "boss").Kills = 1;
        string missing = CodexBossCatalog.All[initial - 1].Key;
        const string champion = "BossRush_Campaign_FinalBoss_Name";
        Kill(champion);
        CodexBossInfo info;
        Check(CodexPersistence.Current.UnlockedCount == initial && CodexPersistence.Current.Find(champion) != null,
            "real global kill collector records new campaign champion");
        Check(CodexBossCatalog.TryGet(champion, out info) && CodexBossCatalog.Count == initial + 1,
            "new historical boss is visible immediately before panel rebuild");
        Check(!BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll)
            && !CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "historical entry cannot substitute for missing original catalog key");
        int revision = CodexBossCatalog.BuildCount;
        Kill(champion);
        Check(CodexBossCatalog.BuildCount == revision && CodexPersistence.Current.Find(champion).Kills == 2,
            "repeat kills do not rebuild unchanged catalog");
        Kill(missing);
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll)
            && CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "all-collected achievement unlocks after every actual key is killed");

        BossRushAchievementManager.Unlocked.Clear();
        CodexMilestones.ResetStaticCaches();
        CodexBossCatalog.Invalidate();
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(CodexBossCatalog.TryGet(champion, out info) && BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll),
            "panel evaluation rebuilds invalidated catalog before checking existing save");

        // Adversarial count: the same unlocked entry appears twice, while a required key has zero kills.
        // A raw UnlockedCount >= Count check passes; key membership must reject it.
        BossRushAchievementManager.Unlocked.Clear();
        CodexPersistence.Current.Find(missing).Kills = 0;
        CodexPersistence.Current.Entries.Add(CodexPersistence.Current.Find(champion));
        Check(CodexPersistence.Current.UnlockedCount >= CodexBossCatalog.Count, "fixture creates equal counts with an unlocked-key gap");
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(!BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll),
            "all-collected evaluates keys rather than aggregate counts");

        CodexPersistence.Current = new CodexData();
        CodexBossCatalog.NotifySlotChanged();
        CodexMilestones.ResetStaticCaches();
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(!CodexBossCatalog.TryGet(champion, out info) && CodexBossCatalog.Count == initial,
            "slot change removes previous slot historical membership");
        Check(!CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current), "empty new slot cannot be all-collected");
        CheckBossTimerIsolation();
        CheckSaveTransactions();
        CheckCatalogAndLanguage();
        CheckCodec();
        CheckCapacityAndCleanup();
        CheckKillEligibility();
        Console.WriteLine("Codex regression checks=" + checks);
    }

    static void Reset()
    {
        CodexPersistence.Current = new CodexData();
        CodexPersistence.HasWriteBarrier = CodexPersistence.IsStoreFaulted = CodexPersistence.RejectStore = false;
        CodexPersistence.StoreCalls = 0;
        CodexKillCollector.ResetStaticCaches();
        CodexBossCatalog.ResetStaticCaches();
        CodexMilestones.ResetStaticCaches();
        BossRushAchievementManager.Unlocked.Clear();
        ModBehaviour.Instance.Pool.Clear();
    }

    static void CheckSaveTransactions()
    {
        Reset();
        var original = CodexPersistence.Current;
        CodexPersistence.RejectStore = true;
        Kill("rejected");
        CodexBossInfo ignored;
        Check(ReferenceEquals(original, CodexPersistence.Current) && original.Entries.Count == 0
            && !CodexBossCatalog.TryGet("rejected", out ignored) && BossRushAchievementManager.Unlocked.Count == 0,
            "rejected first kill cannot change current data, catalog or rewards");
        CodexPersistence.RejectStore = false;
        Kill("accepted");
        original = CodexPersistence.Current;
        string saved = CodexPersistence.SavedJson;
        CodexPersistence.RejectStore = true;
        Kill("accepted");
        Check(ReferenceEquals(original, CodexPersistence.Current) && original.Find("accepted").Kills == 1
            && CodexPersistence.SavedJson == saved, "rejected repeat kill preserves committed record and bytes");
        CodexPersistence.RejectStore = false;
        CodexPersistence.HasWriteBarrier = true;
        int calls = CodexPersistence.StoreCalls;
        Kill("barrier");
        Check(CodexPersistence.StoreCalls == calls && original.Find("barrier") == null,
            "known write barrier blocks mutation and serialization");
        CodexPersistence.HasWriteBarrier = false;
        CodexPersistence.IsStoreFaulted = true;
        Kill("fault");
        Check(CodexPersistence.StoreCalls == calls && original.Find("fault") == null,
            "known storage fault blocks mutation and serialization");
        original.Find("accepted").FastestKillSeconds = 5;
        BossRushAchievementManager.Unlocked.Clear();
        CodexMilestones.EvaluateOnPanelOpen(original);
        Check(BossRushAchievementManager.Unlocked.Count == 0, "faulted panel cannot award milestones");
        CodexPersistence.IsStoreFaulted = false;
        CodexMilestones.EvaluateOnPanelOpen(original);
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "panel restores earned fast-kill achievement from saved measurement");
        original.Find("accepted").Kills = int.MaxValue;
        Kill("accepted");
        Check(CodexPersistence.Current.Find("accepted").Kills == int.MaxValue, "kill count saturates without losing unlock");
    }

    static void CheckCatalogAndLanguage()
    {
        Reset();
        ModBehaviour.Instance.Pool.Add(new EnemyPresetInfo { name = DragonKingConfig.BossNameKey, displayName = "stale king" });
        ModBehaviour.Instance.Pool.Add(new EnemyPresetInfo { name = "official", displayName = "old name" });
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        CodexBossInfo king, official;
        Check(CodexBossCatalog.Count == 9 && CodexBossCatalog.TryGet(DragonKingConfig.BossNameKey, out king)
            && king.IsCustomBoss && king.DisplayName != "stale king", "custom boss already in shared pool keeps custom classification");
        CodexBossCatalog.TryGet("official", out official);
        LocalizationHelper.Text["official"] = "中文名";
        Check(official.DisplayName == "中文名", "current language takes precedence over cached pool display name");
        LocalizationHelper.Text["official"] = "English name";
        Check(official.DisplayName == "English name", "same catalog object follows language switch without rebuilding");
        LocalizationHelper.Text.Clear();
        Check(official.DisplayName == "old name", "missing translation retains recorded fallback name");
        var ghost = CodexPersistence.Current.GetOrCreate("empty_history", "empty");
        CodexBossCatalog.SynchronizeHistoricalEntries(CodexPersistence.Current);
        CodexBossInfo ignored;
        Check(!CodexBossCatalog.TryGet(ghost.Key, out ignored), "zero-kill historical entry cannot become an unreachable requirement");
        foreach (CodexBossInfo info in CodexBossCatalog.All)
        {
            L10n.IsChinese = true;
            string cn = CodexBossCatalog.GetEncounterHint(info);
            L10n.IsChinese = false;
            Check(!string.IsNullOrEmpty(cn) && cn != CodexBossCatalog.GetEncounterHint(info), "each catalog kind offers bilingual encounter guidance");
        }
        L10n.IsChinese = true;
        Check(CodexBossCatalog.BuildZombieBossKey((ZombieModeBossKind)999) == null, "unknown zombie kind cannot invent a collectible");
        string key = CodexBossCatalog.BuildZombieBossKey(ZombieModeBossKind.Titan);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) CodexBossCatalog.BuildZombieBossKey(ZombieModeBossKind.Titan);
        Check(GC.GetAllocatedBytesForCurrentThread() == before && key == "zombie_boss_Titan", "zombie identity hot lookup allocates zero bytes and preserves frozen key");
        ModBehaviour.Instance.Pool = null;
        CodexBossCatalog.Invalidate();
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        foreach (CodexBossInfo info in CodexBossCatalog.All)
            CodexPersistence.Current.GetOrCreate(info.Key, info.DisplayName).Kills = 1;
        Check(!CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "unavailable official pool cannot grant completion from fallback entries alone");
        ModBehaviour.Instance.Pool = new System.Collections.Generic.List<EnemyPresetInfo>();
    }

    static void CheckCodec()
    {
        Check(CodexCodec.ReadSchemaVersion("{\"schemaVersion\":1.2}") == -1,
            "invalid fractional schema version cannot be rounded into a writable v1 save");
        Check(CodexCodec.Decode("{\"schemaVersion\":1,\"entries\":[{\"k\":\"old\"}]}").Find("old").Kills == 0,
            "v1 optional fields retain defaults");
        string[] invalid =
        {
            "{\"schemaVersion\":1}", "{\"schemaVersion\":1,\"entries\":{}}",
            "{\"schemaVersion\":1,\"entries\":[null]}", "{\"schemaVersion\":1,\"entries\":[{}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\"},{\"k\":\"a\"}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"kills\":\"5\"}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"kills\":-1}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"first\":9223372036854775807}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"fast\":1e100}]}"
        };
        foreach (string json in invalid) Check(CodexCodec.Decode(json) == null, "damaged collection rejects whole payload: " + json);
        var entries = new System.Text.StringBuilder();
        for (int i = 0; i <= CodexTuning.MaxEntries; i++)
        {
            if (i > 0) entries.Append(',');
            entries.Append("{\"k\":\"").Append(i).Append("\"}");
        }
        Check(CodexCodec.Decode("{\"entries\":[" + entries + "]}") == null,
            "oversized stored collection is protected, never truncated and overwritten");
        var data = new CodexData();
        var entry = data.GetOrCreate("escaped\\\"", "中文");
        entry.Kills = 7; entry.FirstMode = "raid"; entry.FastestKillSeconds = 0.001f;
        string encoded = CodexCodec.Encode(data);
        Check(CodexCodec.Encode(CodexCodec.Decode(encoded)) == encoded,
            "real codec round trip preserves escaped key, unicode and sub-frame timing bytes");
    }

    static void CheckCapacityAndCleanup()
    {
        Reset();
        var hit = new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 10 };
        var targets = new Health[CodexTuning.MaxFightStartTracked + 1];
        UnityEngine.Time.time = 10;
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = new Health { Character = new CharacterMainControl { isBossCharacter = true,
                Team = Teams.wolf, characterPreset = new CharacterRandomPreset { nameKey = "capacity_" + i } } };
            CodexKillCollector.OnGlobalHurt(targets[i], hit);
        }
        Check(CodexKillCollector.TrackedFightCount == CodexTuning.MaxFightStartTracked,
            "capacity keeps existing timers while rejecting only extra timing");
        UnityEngine.Time.time = 19;
        targets[0].IsDead = true;
        CodexKillCollector.OnGlobalDead(targets[0], hit);
        Check(CodexPersistence.Current.Find("capacity_0").FastestKillSeconds == 9,
            "oldest boss keeps measured time when table is full");
        targets[1].Character.Team = Teams.player;
        targets[1].IsDead = true;
        CodexKillCollector.OnGlobalDead(targets[1], hit);
        Check(CodexKillCollector.TrackedFightCount == CodexTuning.MaxFightStartTracked - 2
            && CodexPersistence.Current.Find("capacity_1") == null, "allegiance change clears timing without counting a friendly kill");
        CodexKillCollector.NotifySceneChanged();
        Check(CodexKillCollector.TrackedFightCount == 0, "scene transition clears all outstanding timers");
    }

    static void CheckKillEligibility()
    {
        Reset();
        var hit = new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 10 };
        var target = new Health { Character = new CharacterMainControl { isBossCharacter = true,
            Team = Teams.wolf, characterPreset = new CharacterRandomPreset { nameKey = "excluded" } } };
        ModBehaviour.ModeHRunning = true;
        CodexKillCollector.OnGlobalHurt(target, hit);
        target.IsDead = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexKillCollector.TrackedFightCount == 0 && CodexPersistence.Current.Entries.Count == 0,
            "Mode H player-attributed kill is excluded before timer and collection");
        ModBehaviour.ModeHRunning = false;
        target.IsCompanion = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.IsCompanion = false;
        target.Character.Team = Teams.player;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.Character.Team = Teams.wolf;
        LevelManager.Instance.IsBaseLevel = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        LevelManager.Instance.IsBaseLevel = false;
        target.IsMainCharacterHealth = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.IsMainCharacterHealth = false;
        target.Character.isBossCharacter = false;
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexPersistence.Current.Entries.Count == 0, "companions, allies, base, player and ordinary enemies cannot unlock entries");
        ModBehaviour.Instance.IsZombieModeActive = true;
        foreach (ZombieModeBossKind kind in Enum.GetValues(typeof(ZombieModeBossKind)))
        {
            var boss = new Health { Character = new CharacterMainControl { Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "ordinary_preset" },
                Component = new ZombieModeEnemyRuntimeMarker { IsBoss = true, BossKind = kind } } };
            UnityEngine.Time.time = 100;
            CodexKillCollector.OnGlobalHurt(boss, hit);
            UnityEngine.Time.time = 109;
            boss.IsDead = true;
            CodexKillCollector.OnGlobalDead(boss, hit);
            CodexKillCollector.OnGlobalDead(boss, hit);
            var entry = CodexPersistence.Current.Find(CodexBossCatalog.BuildZombieBossKey(kind));
            Check(entry != null && entry.Kills == 1 && entry.FastestKillSeconds == 9
                && entry.FirstMode == CodexTuning.ModeIdZombie,
                "zombie marker drives unique identity, timing and duplicate-death exclusion: " + kind);
        }
        target.Character.isBossCharacter = true;
        target.Character.Component = new ZombieModeEnemyRuntimeMarker { IsBoss = false };
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexPersistence.Current.Find("excluded") == null && CodexPersistence.Current.Entries.Count == 5,
            "ordinary zombie marker cannot inherit a boss preset identity");
        ModBehaviour.Instance.IsZombieModeActive = false;
    }

    static void CheckBossTimerIsolation()
    {
        CodexKillCollector.ResetStaticCaches();
        var player = new CharacterMainControl { IsMainCharacter = true };
        var info = new DamageInfo { fromCharacter = player, finalDamage = 10 };
        var boss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "timer_boss" }
        } };
        UnityEngine.Time.time = 100;
        CodexKillCollector.OnGlobalHurt(boss, info);
        for (int i = 0; i < CodexTuning.MaxFightStartTracked + 10; i++)
        {
            var trash = new Health { Character = new CharacterMainControl
            {
                isBossCharacter = false, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "trash" }
            } };
            CodexKillCollector.OnGlobalHurt(trash, info);
            trash.IsDead = true;
            CodexKillCollector.OnGlobalDead(trash, info);
        }
        Check(CodexKillCollector.TrackedFightCount == 1, "long run trash kills do not fill or evict boss fight timer");
        UnityEngine.Time.time = 109;
        boss.IsDead = true;
        CodexKillCollector.OnGlobalDead(boss, info);
        CodexKillCollector.OnGlobalHurt(boss, info);
        Check(CodexKillCollector.TrackedFightCount == 0
            && Math.Abs(CodexPersistence.Current.Find("timer_boss").FastestKillSeconds - 9f) < .001f,
            "boss retains nine-second timing and lethal OnHurt does not reopen dead timer");
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "ten-second achievement remains attainable after long trash fight");
        var assistedBoss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "assisted_boss" }
        } };
        CodexKillCollector.OnGlobalHurt(assistedBoss, info);
        assistedBoss.IsDead = true;
        CodexKillCollector.OnGlobalDead(assistedBoss, new DamageInfo { finalDamage = 10 });
        Check(CodexKillCollector.TrackedFightCount == 0 && CodexPersistence.Current.Find("assisted_boss") == null,
            "environment or companion final blow closes timer without counting personal kill");

        BossRushAchievementManager.Unlocked.Clear();
        var instantBoss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "same_frame_boss" }
        } };
        CodexKillCollector.OnGlobalHurt(instantBoss, info);
        instantBoss.IsDead = true;
        CodexKillCollector.OnGlobalDead(instantBoss, info);
        Check(Math.Abs(CodexPersistence.Current.Find("same_frame_boss").FastestKillSeconds - 0.001f) < .00001f,
            "same-frame observed first hit and final blow retain a positive measured-time bucket");
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "same-frame kill with a real first-hit observation qualifies for ten-second achievement");
        BossRushAchievementManager.Unlocked.Clear();
        Kill("unknown_start_boss");
        Check(CodexPersistence.Current.Find("unknown_start_boss").FastestKillSeconds == 0f
            && !BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "missing timer remains unknown rather than inventing a one-hit duration");
    }
}
