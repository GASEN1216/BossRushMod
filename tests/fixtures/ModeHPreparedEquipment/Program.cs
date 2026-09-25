using System;
using System.Collections.Generic;

namespace BossRush
{
    static class Program
    {
        static int checks;
        static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
        static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }
        static void Main()
        {
            Item template = new Item();
            template.Set("MaxHealth", 100).Set("RunSpeed", 4).Set("WalkSpeed", 2).Set("Moveability", 1)
                .Set("GunDamageMultiplier", 9).Set("MeleeDamageMultiplier", 8).Set("GunDistanceMultiplier", 7)
                .Set("GunShootSpeedMultiplier", 1).Set("BodyArmor", 0).Set("HeadArmor", 0)
                .Set("GunCritRateGain", 0).Set("MeleeCritRateGain", 0).Set("GunCritDamageGain", 0).Set("MeleeCritDamageGain", 0);
            ItemAssetsCollection.Prefabs[1] = template;
            var preset = new CharacterRandomPreset { health = 400, damageMultiplier = 1.5f,
                meleeDamageMultiplier = .7f, moveSpeedFactor = 1.2f, gunDistanceMultiplier = 1.3f, gunCritRateGain = .5f };
            preset.Add("RunSpeed", 4, 6); // median 5 before moveSpeedFactor, not random again on spawn
            Item preview = template.Copy();
            ModeHRuntimeModule.Prepare(preview, preset);
            Check(Near(preview.GetStatValue("MaxHealth"), 300), "difficulty EnemyHealthFactor included");
            Check(Near(preview.GetStatValue("GunDamageMultiplier"), 1.5f)
                && Near(preview.GetStatValue("GunDistanceMultiplier"), 1.3f), "official set fields do not multiply template defaults");
            Check(Near(preview.GetStatValue("RunSpeed"), 6f), "setStats range is frozen before movement multiplier");
            Item spawned = template.Copy().Set("RunSpeed", 77).Set("GunDamageMultiplier", 42).Set("MaxHealth", 9999);
            ModeHRuntimeModule.Prepare(spawned, preset);
            foreach (string key in new[] { "MaxHealth", "RunSpeed", "GunDamageMultiplier", "MeleeDamageMultiplier" })
                Check(Near(spawned.GetStatValue(key), preview.GetStatValue(key)), "spawn and preview use same frozen stat: " + key);
            Item gun = new Item { Gun = new ItemSetting_Gun(), TypeID = 10 };
            gun.Set("Damage", 50).Set("CritRate", .2f).Set("CritDamageFactor", 2).Set("ShootSpeed", 4)
                .Set("ShotCount", 1).Set("MoveSpeedMultiplier", .8f).Set("BulletDistance", 20);
            Item ammo = new Item { TypeID = 100, IsBullet = true };
            ammo.Constants.Values["damageMultiplier"] = 1.25f;
            ammo.Constants.Values["CritRateGain"] = .25f;
            ammo.Constants.Values["CritDamageFactorGain"] = .5f;
            ammo.Set("damageMultiplier", 12345); // deliberately conflicts: official damage uses Constants
            var stats = ModeHRuntimeModule.Read(preview, gun, ammo);
            Check(Near(stats.Damage, 93.75f), "bullet Constants damage multiplier used instead of stat");
            Check(Near(stats.CritChance, .35f), "crit rate multiplies all gains");
            Check(Near(stats.MoveSpeed, 4.8f) && Near(stats.Range, 26f), "held weapon movement penalty and character range used");
            Check(stats.Power > 0f, "public equipment power remains positive");
            gun.Set("Damage", 2).Set("ShotCount", 8);
            Check(Near(ModeHRuntimeModule.Read(preview, gun, ammo).Damage, 8f), "per-pellet minimum damage summed for shotgun trigger");
            Item melee = new Item().Set("Damage", 40).Set("CritRate", .1f).Set("CritDamageFactor", 2)
                .Set("AttackSpeed", 2).Set("AttackRange", 2.5f).Set("MoveSpeedMultiplier", .9f);
            stats = ModeHRuntimeModule.Read(preview, melee, null);
            Check(Near(stats.Damage, 28f) && Near(stats.Range, 2.5f), "melee uses its own multiplier and reach");
            ItemAssetsCollection.Prefabs[100] = ammo;
            ItemAssetsCollection.Prefabs[101] = new Item { TypeID = 101, IsBullet = false };
            ItemAssetsCollection.SearchIds = new[] { 101, 100 };
            Check(ModeHLoadoutKitRegistry.ResolvePreparedAmmoTypeId(gun, gun.Gun) == 100,
                "fresh gun with TargetBulletID -1 resolves an actual compatible bullet");
            gun.Gun.TargetBulletID = 101;
            Check(ModeHLoadoutKitRegistry.ResolvePreparedAmmoTypeId(gun, gun.Gun) == 100,
                "invalid default bullet falls back to compatible type");
            gun.Gun.TargetBulletID = 100;
            ItemAssetsCollection.SearchIds = new int[0];
            Check(ModeHLoadoutKitRegistry.ResolvePreparedAmmoTypeId(gun, gun.Gun) == 100, "valid target remains frozen");
            gun.Gun.TargetBulletID = -1;
            Check(ModeHLoadoutKitRegistry.ResolvePreparedAmmoTypeId(gun, gun.Gun) == 0, "no ammo fails closed");
            Console.WriteLine("PASS ModeHPreparedEquipment " + checks + " assertions");
        }
    }
    class ModeHPreparedFighterStats { public float Health, Damage, MoveSpeed, Range, Armor, HeadArmor, CritChance, Power; }
    class Stat { public float BaseValue; }
    class Item
    {
        public int TypeID;
        public bool IsBullet;
        public ItemSetting_Gun Gun;
        public Constants Constants = new Constants();
        readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        public Item Set(string key, float value) { stats[key] = new Stat { BaseValue = value }; return this; }
        public Stat GetStat(string key) { Stat value; return stats.TryGetValue(key, out value) ? value : null; }
        public float GetStatValue(string key) { Stat stat = GetStat(key); return stat != null ? stat.BaseValue : 0f; }
        public float GetStatValue(int hash) { foreach (var row in stats) if (row.Key.GetHashCode() == hash) return row.Value.BaseValue; return 0; }
        public T GetComponent<T>() where T : class { return Gun as T; }
        public Item Copy() { var copy = new Item(); foreach (var row in stats) copy.Set(row.Key, row.Value.BaseValue); return copy; }
    }
    class Constants
    {
        public Dictionary<string, float> Values = new Dictionary<string, float>();
        public float GetFloat(string key, float fallback) { float value; return Values.TryGetValue(key, out value) ? value : fallback; }
        public string GetString(string key, string fallback) { return "AR"; }
    }
    class ItemSetting_Gun
    {
        public int TargetBulletID = -1;
        public bool IsValidBullet(Item item) { return item.IsBullet; }
    }
    struct ItemFilter { public object[] requireTags; public int minQuality, maxQuality; public string caliber; }
    static class ItemAssetsCollection
    {
        public static Dictionary<int, Item> Prefabs = new Dictionary<int, Item>();
        public static int[] SearchIds;
        public static Item GetPrefab(int id) { Item value; return Prefabs.TryGetValue(id, out value) ? value : null; }
        public static int[] GetAllTypeIds(ItemFilter filter) { return SearchIds; }
    }
    static class GameplayDataSettings
    {
        internal static class Tags { public static object Bullet = new object(); }
        internal static class ItemAssets { public const int DefaultCharacterItemTypeID = 1; }
    }
    static class LevelManager { public static Rule Rule = new Rule(); }
    class Rule { public float EnemyHealthFactor = .75f; }
    class CharacterRandomPreset
    {
        public float health, damageMultiplier, meleeDamageMultiplier, moveSpeedFactor, gunDistanceMultiplier, gunCritRateGain;
        private List<Entry> setStats = new List<Entry>();
        public void Add(string name, float min, float max) { setStats.Add(new Entry { statName = name, statBaseValue = new Vector2 { x = min, y = max } }); }
        class Entry { public string statName; public Vector2 statBaseValue; }
    }
    struct Vector2 { public float x, y; }
    static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Clamp(float x, float min, float max) { return Math.Max(min, Math.Min(max, x)); }
        public static float Clamp01(float x) { return Clamp(x, 0, 1); }
        public static float Round(float x) { return (float)Math.Round(x); }
        public static float Sqrt(float x) { return (float)Math.Sqrt(x); }
    }
}
