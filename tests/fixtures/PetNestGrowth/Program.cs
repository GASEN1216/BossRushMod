using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BossRush
{
    static class Program
    {
        static int checks;
        static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
        static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
        static CharacterMainControl Character()
        {
            var c = new CharacterMainControl();
            foreach (string key in new[] { "Helmat", "Armor", "MeleeWeapon", "PrimaryWeapon" })
                c.CharacterItem.Slots.Rows[key] = new Slot { Content = new Item { TypeID = -2 } };
            return c;
        }
        static async Task Main()
        {
            // Official Wiki examples: Speedy Ice 0.8, Deng 0.85, ordinary bosses 1, Prison 1.35.
            float last = 0;
            foreach (float source in new[] { 0.8f, 0.85f, 1f, 1.35f })
            {
                float current = PetNestGrowth.BaseDamageMultiplier(source);
                Check(current > last && current > 0.06f, "official bloodline rank retained and no old damage regression");
                last = current;
            }
            Check(Near(PetNestGrowth.BaseDamageMultiplier(.8f), .16f), "ice boss baseline");
            Check(Near(PetNestGrowth.BaseDamageMultiplier(1.35f), .27f), "prison boss baseline");
            Check(Near(PetNestGrowth.BaseDamageMultiplier(.3f), .12f), "custom low multiplier has useful floor");
            foreach (float x in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.MaxValue })
                Check(PetNestGrowth.BaseDamageMultiplier(x) >= .12f && PetNestGrowth.BaseDamageMultiplier(x) <= .28f,
                    "invalid and excessive multipliers bounded");
            for (int level = 2; level <= 10; level++)
                Check(PetNestGrowth.ModelScale(.62f, level) > PetNestGrowth.ModelScale(.62f, level - 1), "visible growth each level");
            Check(Near(PetNestGrowth.ModelScale(.62f, 1), .62f) && Near(PetNestGrowth.ModelScale(.62f, 10), .899f), "cub and adult endpoints");
            Check(Near(PetNestGrowth.ModelScale(.62f, 999), .899f) && Near(PetNestGrowth.ModelScale(.62f, -1), .62f), "bad save levels clamped");
            Check(PetNestGrowth.KillExperience(99) == 4 && PetNestGrowth.KillExperience(190) == 5
                && PetNestGrowth.KillExperience(550) == 9 && PetNestGrowth.KillExperience(1000) == 14, "health weighted kill XP");
            Check(PetNestGrowth.KillExperience(float.MaxValue) == 16
                && PetNestGrowth.KillExperience(float.NaN) == 4, "XP saturation before float to int conversion");
            Check(PetNestTuning.PetExpHomecoming + PetNestTuning.PetExpCompanionKillRunCap == 100, "active raid can earn one level without changing old XP schema");
            var clone = new CharacterRandomPreset { nameKey = "official", damageMultiplier = 1.35f };
            PetNestCompanionSpawner.NeutralizeClonePreset(clone);
            Check(Near(clone.damageMultiplier, .27f) && Near(clone.meleeDamageMultiplier, .27f), "production neutralization uses source damage");
            Check(!clone.hasSkill && clone.exp == 0 && !clone.hasSoul && !clone.dropBoxOnDead
                && clone.team == Teams.player && !clone.setActiveByPlayerDistance, "companion safety remains intact");
            clone = new CharacterRandomPreset { nameKey = DragonKingConfig.BossNameKey, damageMultiplier = 99f };
            PetNestCompanionSpawner.NeutralizeClonePreset(clone);
            Check(Near(clone.damageMultiplier, .12f) && clone.health == DragonKingConfig.BaseHealth, "custom bloodline uses custom stats instead of red boss template");
            foreach (string key in new[] { DragonDescendantConfig.BOSS_NAME_KEY, DragonKingConfig.BossNameKey, PhantomWitchConfig.BossNameKey })
            {
                ItemAssetsCollection.Reset();
                var c = Character();
                var oldHelm = c.CharacterItem.Slots.GetSlot("Helmat").Content;
                await PetNestCompanionSpawner.Equip(c, key);
                int weapon = key == DragonDescendantConfig.BOSS_NAME_KEY ? 500005 : key == DragonKingConfig.BossNameKey ? 500020 : 500044;
                Check(c.Held != null && c.Held.TypeID == weapon, "custom weapon really held: " + key);
                if (key != PhantomWitchConfig.BossNameKey)
                {
                    Check(c.Held.Gun.BulletCount == 30 && c.CharacterItem.Inventory.Items.Count == 5, "new gun starts loaded and has reload ammunition");
                    Check(oldHelm.Destroyed && c.CharacterItem.Slots.GetSlot("Helmat").Content.TypeID > 500000, "occupied armor slot replaced after new gear exists");
                }
            }
            ItemAssetsCollection.Reset();
            ItemAssetsCollection.MissingTypeId = 500003;
            var missing = Character();
            var retained = missing.CharacterItem.Slots.GetSlot("Helmat").Content;
            await PetNestCompanionSpawner.Equip(missing, DragonDescendantConfig.BOSS_NAME_KEY);
            Check(ReferenceEquals(retained, missing.CharacterItem.Slots.GetSlot("Helmat").Content) && !retained.Destroyed,
                "FallbackItem leaves old helmet intact");
            Check(missing.Held.TypeID == 500005, "one missing armor does not block available signature weapon");
            ItemAssetsCollection.Reset();
            var rejected = Character();
            rejected.CharacterItem.Slots.GetSlot("MeleeWeapon").Reject = true;
            await PetNestCompanionSpawner.Equip(rejected, PhantomWitchConfig.BossNameKey);
            Check(ItemAssetsCollection.Created[0].Destroyed && rejected.Held == null, "unpluggable new item cleaned up");
            ItemAssetsCollection.Reset();
            var gone = Character();
            ItemAssetsCollection.OnAwait = delegate { gone.CharacterItem = null; };
            await PetNestCompanionSpawner.Equip(gone, PhantomWitchConfig.BossNameKey);
            Check(ItemAssetsCollection.Created[0].Destroyed && gone.Held == null, "scene loss while loading destroys orphan equipment");
            Console.WriteLine("PASS PetNestGrowth " + checks + " assertions");
        }
    }
    enum Teams { player, enemy }
    class AISpecialAttachmentBase { }
    class CharacterRandomPreset
    {
        public string nameKey;
        public bool hasSkill = true, hasSoul = true, dropBoxOnDead = true, setActiveByPlayerDistance = true,
            canDieIfNotRaidMap, setMeleeDamageMultiplier;
        public int exp = 10;
        public Teams team = Teams.enemy;
        public float forceTracePlayerDistance, hasCashChance, aiCombatFactor, damageMultiplier,
            meleeDamageMultiplier, gunCritRateGain, health;
        public List<AISpecialAttachmentBase> specialAttachmentBases;
    }
    class CharacterMainControl
    {
        public Item CharacterItem = new Item();
        public Item Held;
        public void ChangeHoldItem(Item item) { Held = item; }
    }
    class Item
    {
        public int TypeID;
        public bool Destroyed;
        public SlotCollection Slots = new SlotCollection();
        public Inventory Inventory = new Inventory();
        public Constants Constants = new Constants();
        public int MaxStackCount = 60, StackCount;
        public ItemSetting_Gun Gun;
        public T GetComponent<T>() where T : class { return Gun as T; }
        public void DestroyTree() { Destroyed = true; }
    }
    class Inventory
    {
        public List<Item> Items = new List<Item>();
        public bool AddAndMerge(Item item, int index) { Items.Add(item); return true; }
    }
    class Constants { public string GetString(string key, string fallback) { return "BR"; } }
    struct ItemFilter { public object[] requireTags; public int minQuality, maxQuality; public string caliber; }
    static class GameplayDataSettings { internal static class Tags { internal static object Bullet = new object(); } }
    class ItemSetting_Gun
    {
        public Item Owner;
        public int TargetBulletID = -1, Capacity = 30;
        public int BulletCount { get { return GetBulletCount(); } }
        public int GetBulletCount() { int n = 0; foreach (var item in Owner.Inventory.Items) n += item.StackCount; return n; }
        public bool IsValidBullet(Item item) { return item.TypeID == 368; }
        public void SetTargetBulletType(int id) { TargetBulletID = id; }
    }
    class SlotCollection
    {
        public Dictionary<string, Slot> Rows = new Dictionary<string, Slot>();
        public Slot GetSlot(string key) { return Rows.ContainsKey(key) ? Rows[key] : null; }
    }
    class Slot
    {
        public Item Content;
        public bool Reject;
        public bool Plug(Item item, out Item replaced)
        {
            replaced = null;
            if (Reject) return false;
            replaced = Content; Content = item; return true;
        }
    }
    static class ItemAssetsCollection
    {
        public static int MissingTypeId;
        public static Action OnAwait;
        public static readonly List<Item> Created = new List<Item>();
        public static void Reset() { MissingTypeId = -1; OnAwait = null; Created.Clear(); }
        public static Item GetPrefab(int id) { return new Item { TypeID = id == MissingTypeId ? 0 : id }; }
        public static int[] Search(ItemFilter filter) { return new[] { 368 }; }
        public static Item InstantiateSync(int id) { return new Item { TypeID = id }; }
        public static async Task<Item> InstantiateAsync(int id)
        {
            await Task.Yield();
            if (OnAwait != null) OnAwait();
            var item = new Item { TypeID = id };
            if (id == 500005 || id == 500020) item.Gun = new ItemSetting_Gun { Owner = item };
            Created.Add(item); return item;
        }
    }
    static class ModBehaviour { public static void DevLog(string message) { } }
    // Config doubles are identifiers only; actual content values remain guarded by their own suites.
    static class DragonDescendantConfig
    {
        public const string BOSS_NAME_KEY = "dragon";
        public const float DamageMultiplier = .3f, BaseHealth = 500f;
        public const int DRAGON_HELM_TYPE_ID = 500003, DRAGON_ARMOR_TYPE_ID = 500004, DRAGON_BREATH_TYPE_ID = 500005;
    }
    static class DragonKingConfig
    {
        public const string BossNameKey = "king";
        public const float DamageMultiplier = .3f, BaseHealth = 800f;
        public const int DRAGON_KING_HELM_TYPE_ID = 500011, DRAGON_KING_ARMOR_TYPE_ID = 500012, FEN_HUANG_HALBERD_TYPE_ID = 500013;
    }
    static class DragonKingBossGunConfig { public const int WeaponTypeId = 500020; }
    static class PhantomWitchConfig
    {
        public const string BossNameKey = "witch";
        public const float DamageMultiplier = 1.1f, BaseHealth = 1000f;
        public const int ReservedScytheTypeId = 500044;
    }
}
