using System;

namespace BossRush
{
    internal enum GreedyMimicFood { Ammunition = 0, Explosive = 1, Medicine = 2 }
    internal enum GreedyMimicAttack { Bite = 0, Volley = 1, Bombs = 2, Recovery = 3, Pounce = 4 }

    /// <summary>局内菜单与战斗预算。纯 C#；输入失败不改变成长，三次投喂后不可继续扣物品。</summary>
    internal sealed class GreedyMimicRules
    {
        internal const int MaximumMeals = 3;
        /// <summary>舌击只在这个平面距离内出手；更远的「咬」改为扑跳追过去。</summary>
        internal const float TongueRange = 9f;
        internal const float TongueHalfWidth = 0.9f;
        internal const float MaximumLeap = 16f;
        internal const float LeapSeconds = 0.42f;
        internal const float ShellSpeed = 17f;
        internal const float ShellRange = 32f;
        internal const float ShellHalfWidth = 0.8f;
        internal const float EnragedHealthFraction = 0.4f;
        /// <summary>闭着嘴时铁皮箱体只吃这么多伤害；张嘴（蓄力预警、吞咽、扑跳）时全额。</summary>
        internal const float ShellArmor = 0.45f;
        /// <summary>每趟普通出击遇到宝箱怪的概率；遇上时出现的时间与位置也随机。</summary>
        internal const float SpawnChance = 0.35f;
        private readonly int[] meals = new int[3];
        internal int Meals { get; private set; }
        internal int Level(GreedyMimicFood food) { int index = (int)food; return index >= 0 && index < 3 ? meals[index] : 0; }
        internal float MaximumHealth { get { return 900f + Meals * 300f; } }
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
            if (attack == GreedyMimicAttack.Pounce) return 26f + Meals * 5f;
            return 20f + Meals * 4f;
        }
        internal static float WarningSeconds(GreedyMimicAttack attack)
        {
            if (attack == GreedyMimicAttack.Recovery) return 2.8f;
            if (attack == GreedyMimicAttack.Bombs) return 2f;
            if (attack == GreedyMimicAttack.Volley) return 1.35f;
            if (attack == GreedyMimicAttack.Pounce) return 1.05f;
            return 0.75f;
        }
        /// <summary>两招之间的喘息：满血 2.2 秒，越残越急，空血 1.15 秒。</summary>
        internal static float Cooldown(float healthFraction)
        {
            float f = float.IsNaN(healthFraction) ? 1f : Math.Max(0f, Math.Min(1f, healthFraction));
            return 1.15f + 1.05f * f;
        }
        internal static bool Enraged(float healthFraction) { return healthFraction <= EnragedHealthFraction && healthFraction > 0f; }
        /// <summary>扑跳落地圈随体型变大：不喂 2.4 m，三口 3.2 m。</summary>
        internal float PounceRadius { get { return 2.4f + (Scale - 1f) * 1.5f; } }
        /// <summary>弹药吃到两口起，第一波打完立刻按玩家新位置补第二波。</summary>
        internal int VolleyWaves { get { return Level(GreedyMimicFood.Ammunition) >= 2 ? 2 : 1; } }
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
