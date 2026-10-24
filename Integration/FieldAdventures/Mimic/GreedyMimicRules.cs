using System;

namespace BossRush
{
    internal enum GreedyMimicFood { Ammunition = 0, Explosive = 1, Medicine = 2 }
    internal enum GreedyMimicAttack { Bite = 0, Volley = 1, Bombs = 2, Recovery = 3 }

    /// <summary>局内菜单与战斗预算。纯 C#；输入失败不改变成长，三次投喂后不可继续扣物品。</summary>
    internal sealed class GreedyMimicRules
    {
        internal const int MaximumMeals = 3;
        private readonly int[] meals = new int[3];
        internal int Meals { get; private set; }
        internal int Level(GreedyMimicFood food) { int index = (int)food; return index >= 0 && index < 3 ? meals[index] : 0; }
        internal float MaximumHealth { get { return 600f + Meals * 240f; } }
        internal float Scale { get { return 1f + Meals * 0.18f; } }
        internal static int Cost(GreedyMimicFood food) { return food == GreedyMimicFood.Ammunition ? 12 : 1; }
        internal bool CanFeed(GreedyMimicFood food) { return (int)food >= 0 && (int)food < 3 && Meals < MaximumMeals; }
        internal bool Feed(GreedyMimicFood food)
        {
            if (!CanFeed(food)) return false;
            meals[(int)food]++;
            Meals++;
            return true;
        }
        internal GreedyMimicAttack AttackAt(int turn)
        {
            if (turn < 0 || turn % 2 == 0) return GreedyMimicAttack.Bite;
            int options = (meals[0] > 0 ? 1 : 0) + (meals[1] > 0 ? 1 : 0) + (meals[2] > 0 ? 1 : 0);
            if (options == 0) return GreedyMimicAttack.Bite;
            int chosen = (turn / 2) % options;
            for (int i = 0; i < 3; i++) if (meals[i] > 0 && chosen-- == 0) return (GreedyMimicAttack)(i + 1);
            return GreedyMimicAttack.Bite;
        }
        internal float Damage(GreedyMimicAttack attack)
        {
            if (attack == GreedyMimicAttack.Volley) return 16f + Level(GreedyMimicFood.Ammunition) * 5f;
            if (attack == GreedyMimicAttack.Bombs) return 24f + Level(GreedyMimicFood.Explosive) * 6f;
            return 20f + Meals * 4f;
        }
        internal static float WarningSeconds(GreedyMimicAttack attack) { return attack == GreedyMimicAttack.Recovery ? 2.8f : attack == GreedyMimicAttack.Bombs ? 2f : 1.35f; }
        internal static bool Interrupted(float receivedDamage) { return receivedDamage >= 45f; }
        internal float Recovery(float currentHealth) { return Math.Max(0f, Math.Min(MaximumHealth - currentHealth, 75f + Level(GreedyMimicFood.Medicine) * 35f)); }
        internal static bool InCircle(float dx, float dz, float radius) { return dx * dx + dz * dz <= radius * radius; }
        internal static float DistanceToSegmentSquared(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, denominator = dx * dx + dz * dz;
            float t = denominator <= 0.0001f ? 0f : Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (pz - az) * dz) / denominator));
            dx = px - ax - t * dx; dz = pz - az - t * dz;
            return dx * dx + dz * dz;
        }
    }
}
