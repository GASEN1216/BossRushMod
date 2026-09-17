using System;
using System.IO;

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

public class CharacterMainControl
{
    public bool IsMainCharacter;
    public bool isBossCharacter;
    public bool Marked;
}

public class Health
{
    public bool IsMainCharacterHealth;
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
    public class ModBehaviour
    {
        public static ModBehaviour Instance;
        public int Wave;
        public int GetCampaignCurrentWave() { return Wave; }
        public bool HasCampaignBountyMark(CharacterMainControl victim) { return victim.Marked; }
        public static void DevLog(string value) { }
        public static void CriticalLog(string key, string value) { }
    }

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
        internal static CampaignChapterDef GetActiveChapterDef() { return CampaignContentCatalog.GetChapter(Active); }
        internal static bool NotifyObjectivesSatisfied(string chapter)
        {
            if (Reject) return false;
            if (chapter != Active) throw new Exception("wrong campaign owner");
            Notifications++;
            return true;
        }
    }
}
