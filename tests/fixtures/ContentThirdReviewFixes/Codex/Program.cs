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
        Console.WriteLine("Codex regression checks=" + checks);
    }
}
