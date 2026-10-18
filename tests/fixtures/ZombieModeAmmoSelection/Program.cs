using System;
using System.Linq;
using BossRush;
using ItemStatsSystem;

internal static class Program
{
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void Add(int id, int quality, string caliber, bool stackable, int maxStack, params string[] tags)
    {
        Table.Entries.Add(new TableEntry { Id = id, Quality = quality, Caliber = caliber, Stackable = stackable, MaxStack = maxStack, Tags = tags });
    }

    private static void Seed()
    {
        Table.Reset();
        Add(1, 1, "9mm", true, 60, "Ammo", "Bullet");
        Add(2, 3, "9mm", true, 60, "Ammo", "Bullet");
        // 火箭弹只有高品质两档、不堆叠（RPG 发枪配弹落空的原样）。
        Add(10, 5, "Rocket", false, 1, "Ammo");
        Add(11, 4, "Rocket", false, 1, "Ammo");
        // 小堆叠重弹药，且只挂 Bullet 不挂 Ammo。
        Add(20, 6, "Grenade", true, 2, "Bullet");
        // 同口径的发射器本体不是弹药，不能被当成弹药发出去。
        Add(30, 1, "Rocket", false, 1, "Gun");
    }

    public static void Main()
    {
        var module = new ZombieModeRuntimeModule();

        Seed();
        Assert(module.Give("9mm", 120, 1, 2) && Table.Sent.Count == 1 && Table.Sent[0].TypeID == 1 && Table.Sent[0].StackCount == 60,
            "Ordinary bullets keep the in-range quality and one full stack");

        Seed();
        Assert(module.Give("Rocket", 120, 1, 2), "RPG reward must still grant rockets when its caliber has no ammo in the starter quality band");
        Assert(Table.Sent.Count == ZombieModeTuning.HeavyAmmoMinimumRounds && Table.Sent.All(i => i.TypeID == 11),
            "Out-of-band fallback takes the lowest quality of that caliber and tops non-stackable rounds up to the minimum");
        Assert(Table.NonStackableCountWrites == 0, "Non-stackable ammo must not have its count written (official logs an error)");

        Seed();
        Assert(module.Give("Grenade", 120, 1, 2) && Table.Sent.Count == 2 && Table.Sent.Sum(i => i.StackCount) == 4 && Table.Sent.All(i => i.TypeID == 20),
            "Small-stack ammo tagged only Bullet is found and topped up with a second stack");

        Seed();
        Assert(module.Give("Rocket", 2, 1, 2) && Table.Sent.Count == 2, "The minimum never exceeds what the caller asked for");

        Seed();
        Assert(!module.Give("Laser", 120, 1, 2) && Table.Sent.Count == 0, "A caliber with no ammo at all grants nothing");

        // 区间高于该口径全部品质（契约换装要 5 品以上、9mm 只有 1 / 3 品）：取不超过上限的最高档，不是最差的 1 品。
        Seed();
        Assert(module.Give("9mm", 120, 5, 6) && Table.Sent.Count == 1 && Table.Sent[0].TypeID == 2,
            "A band above every quality of the caliber falls back to the nearest (highest) quality, not the lowest");

        // 商人保底 (6,6)：该口径没有 6 品就什么都不发，保底留给下次（调用方再按普通区间买）。
        Seed();
        Assert(!module.GiveStrict("9mm", 200, 6, 6) && Table.Sent.Count == 0,
            "Guaranteed-quality purchase must not be consumed by an out-of-band fallback");
        Seed();
        Assert(module.GiveStrict("Grenade", 4, 6, 6) && Table.Sent.All(i => i.TypeID == 20),
            "Guaranteed-quality purchase still grants when the caliber has that quality");

        // 属性满级判定（同一文件的生产方法）：减伤按负数累加到 -0.40，上限表给的是 0.40。
        Assert(ZombieModeRuntimeModule.IsZombieModeAttributeBonusAtCap(-0.40f, 0.40f), "Damage reduction stored as -0.40 is at its cap");
        Assert(!ZombieModeRuntimeModule.IsZombieModeAttributeBonusAtCap(-0.35f, 0.40f), "Damage reduction below the cap stays in the pool");
        Assert(ZombieModeRuntimeModule.IsZombieModeAttributeBonusAtCap(0.99999994f, 1.00f), "Ten 0.1 steps reach the 100% cap despite float error");
        Assert(!ZombieModeRuntimeModule.IsZombieModeAttributeBonusAtCap(0.9f, 1.00f), "A bonus one step below the cap stays in the pool");

        Console.WriteLine("PASS ZombieModeAmmoSelection");
    }
}
