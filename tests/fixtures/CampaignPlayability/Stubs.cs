using System;
using System.IO;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Color { public Color(float r, float g, float b, float a) { } }
}

namespace Duckov.Utilities
{
    public class Tag { public string name; }
}

namespace ItemStatsSystem
{
    public struct ItemMetaData { public int id; public Duckov.Utilities.Tag[] tags; }
    public static class ItemAssetsCollection
    {
        public static ItemMetaData GetMetaData(int id)
        {
            return new ItemMetaData { id = id, tags = new[] {
                new Duckov.Utilities.Tag { name = id == 1 ? "MeleeWeapon" : "Gun" }
            } };
        }
    }
}

public class CharacterMainControl : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject;
    public static CharacterMainControl Main;
    public Health Health;
    public Teams Team;
    public bool IsMainCharacter;
    public bool isBossCharacter;
    public bool Marked;
    public int GetInstanceID() { return 1; }
}
public enum Teams { player, wolf, middle }
public static class Team
{
    public static bool IsEnemy(Teams self, Teams other) { return self != Teams.middle && other != Teams.middle && self != other; }
}

public class Health : UnityEngine.Object
{
    public bool IsMainCharacterHealth;
    public bool IsDead;
    public DeathEvent OnDeadEvent;
    public CharacterMainControl Character;
    public CharacterMainControl TryGetCharacter() { return Character; }
}

public struct DamageInfo
{
    public float finalDamage;
    public int fromWeaponItemID;
    public CharacterMainControl fromCharacter;
}

namespace BossRush
{
    public partial class ModBehaviour
    {
        public static ModBehaviour Instance;
        public int Wave { get { return currentEnemyIndex + 1; } set { currentEnemyIndex = value - 1; } }
        public bool modeDActive, modeEActive, modeFActive, modeGActive, bossRushArenaActive, IsActive, infiniteHellMode;
        public int ModeDWaveIndex, infiniteHellWaveIndex, currentEnemyIndex;
        public ZombieRun zombieModeRunState;
        public FRun modeFState = new FRun();
        private bool campaignFinalBossActive;
        public bool IsCampaignConfiguredEnabled() { return true; }
        private void TickCampaignFinalBossAltar() { }
        private bool ConsumeModeFPlayerBountyKillLatch(int id) { return false; }
        public static void DevLog(string value) { }
        public static void CriticalLog(string key, string value) { }
        public void ShowMessage(string value) { }
    }
    public class ZombieRun { public int LifecyclePhase, CurrentWave; }
    public class FRun { public Dictionary<int, int> BountyMarksByCharacterId = new Dictionary<int, int>(); }
    internal static class ZombieModePhaseGuards { public static bool IsRunActive(int phase) { return phase == 1; } }
    internal static class L10n { internal static bool IsChinese; internal static string T(string cn, string en) { return IsChinese ? cn : en; } }
    internal static class CampaignAssetCache { internal static object GetChapterPoster(int order) { return null; } }
    internal static class CampaignPersistence { internal static bool HasWriteBarrier, IsStoreFaulted; }
    internal static class CampaignBoardView { internal static void OpenForOwner(ModBehaviour owner) { } }

    internal static class JsonDataRegistry
    {
        internal static bool TryReadDataFile(string directory, string file, out string json)
        {
            json = File.ReadAllText(Path.Combine("Assets", "Data", directory, file));
            return true;
        }
    }

    // Only the persistence sink and selected contract are substituted. All counting,
    // damage filtering, retry and completion decisions execute production sources.
    internal static class CampaignProgressService
    {
        internal static string Active;
        internal static int Notifications;
        internal static bool Reject;
        internal static CampaignChapterState State = CampaignChapterState.ContractActive;
        internal static HashSet<string> Clues = new HashSet<string>();
        internal static bool IsClueUnlocked(string id) { return Clues.Contains(id); }
        internal static CampaignChapterState GetState(string id) { return State; }
        internal static CampaignChapterDef GetActiveChapterDef() { return CampaignContentCatalog.GetChapter(Active); }
        internal static bool NotifyObjectivesSatisfied(string chapter)
        {
            if (Reject) return false;
            if (chapter != Active) throw new Exception("wrong campaign owner");
            Notifications++;
            State = CampaignChapterState.ReadyToDeliver;
            return true;
        }
    }
}

namespace Duckov.NoteIndexs
{
    public class Note { public string key; public object image; public bool hide; }
    public class NoteIndex
    {
        public static NoteIndex Instance;
        public static Action<string> onNoteStatusChanged;
        public List<Note> Notes = new List<Note>();
        public HashSet<string> UnlockedNotes = new HashSet<string>();
        public Dictionary<string, Note> Index = new Dictionary<string, Note>();
        public static bool SetNoteDynamic(Note note) { Instance.Index[note.key] = note; return true; }
        public static bool GetNoteUnlocked(string key) { return Instance != null && Instance.UnlockedNotes.Contains(key); }
        public static void SetNoteUnlocked(string key) { if (Instance != null) Instance.UnlockedNotes.Add(key); }
    }
}
