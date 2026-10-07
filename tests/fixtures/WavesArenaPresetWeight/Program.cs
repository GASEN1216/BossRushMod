using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class Mathf
    {
        internal static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
    }

    internal static class Random
    {
        internal static int RangeCalls;
        internal static int ValueCalls;
        internal static int NextIndex;
        internal static float NextValue;

        internal static int Range(int min, int max)
        {
            RangeCalls++;
            if (NextIndex < min || NextIndex >= max) throw new Exception("Range bounds changed");
            return NextIndex;
        }

        internal static float value
        {
            get { ValueCalls++; return NextValue; }
        }

        internal static void Reset(float nextValue, int nextIndex = 0)
        {
            RangeCalls = 0;
            ValueCalls = 0;
            NextValue = nextValue;
            NextIndex = nextIndex;
        }
    }
}

namespace BossRush
{
    internal sealed class EnemyPresetInfo
    {
        internal string name;
        internal float baseHealth;
        internal string displayName;
        internal int team, expReward;
        internal float baseDamage, healthMultiplier, damageMultiplier;
    }

    internal sealed class ModBehaviour
    {
        internal readonly List<EnemyPresetInfo> Pool = new List<EnemyPresetInfo>();
        internal readonly Dictionary<string, float> Factors = new Dictionary<string, float>();
        internal int FactorCalls;

        internal List<EnemyPresetInfo> GetFilteredEnemyPresets() { return Pool; }
        internal float GetBossInfiniteHellFactor(string name)
        {
            FactorCalls++;
            return Factors[name];
        }
    }

    internal sealed partial class WavesArenaRuntimeModule
    {
        internal ModBehaviour owner;
        internal float MinBossBaseHealth;
        internal float MaxBossBaseHealth;
        internal int InfiniteHellWaveIndex;
    }

    internal static partial class Program
    {
        private static void Check(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }

        private static void Main()
        {
            CheckPresetRecovery();
            var owner = new ModBehaviour();
            var module = new WavesArenaRuntimeModule { owner = owner, MinBossBaseHealth = 100f, MaxBossBaseHealth = 100f };
            UnityEngine.Random.Reset(0.25f);
            Check(module.PickRandomEnemyForInfiniteHell() == null, "empty pool returns null");
            Check(UnityEngine.Random.RangeCalls == 0 && UnityEngine.Random.ValueCalls == 0, "empty pool must not draw");

            var low = new EnemyPresetInfo { name = "low", baseHealth = 100f };
            var high = new EnemyPresetInfo { name = "high", baseHealth = 500f };
            owner.Pool.Add(low);
            owner.Pool.Add(high);
            owner.Factors["low"] = 2f;
            owner.Factors["high"] = 1f;
            UnityEngine.Random.Reset(0.25f);
            Check(module.PickRandomEnemyForInfiniteHell() == low, "degenerate health range uses factor weights");
            Check(owner.FactorCalls == 2 && UnityEngine.Random.ValueCalls == 1 && UnityEngine.Random.RangeCalls == 0,
                "factor branch must draw once");

            owner.Factors["low"] = 0f;
            owner.Factors["high"] = 0f;
            UnityEngine.Random.Reset(0.6f, 1);
            Check(module.PickRandomEnemyForInfiniteHell() == high, "zero factor fallback uses indexed draw");
            Check(UnityEngine.Random.RangeCalls == 1 && UnityEngine.Random.ValueCalls == 0,
                "zero factor fallback must draw only Range");

            owner.Factors["low"] = 1f;
            owner.Factors["high"] = 1f;
            module.MaxBossBaseHealth = 500f;
            module.InfiniteHellWaveIndex = 50;
            UnityEngine.Random.Reset(0.5f);
            Check(module.PickRandomEnemyForInfiniteHell() == high, "later wave increases high health weight");
            Check(UnityEngine.Random.ValueCalls == 1 && UnityEngine.Random.RangeCalls == 0,
                "weighted branch must draw once");

            UnityEngine.Random.Reset(0.15f);
            Check(module.PickRandomEnemyForInfiniteHell() == high, "wave term changes the selection boundary");
            Check(UnityEngine.Random.ValueCalls == 1 && UnityEngine.Random.RangeCalls == 0,
                "boundary case must draw once");

            owner.Factors["high"] = 0.1f;
            UnityEngine.Random.Reset(0.3f);
            Check(module.PickRandomEnemyForInfiniteHell() == low, "user factor still suppresses high health boss");
            Check(UnityEngine.Random.ValueCalls == 1 && UnityEngine.Random.RangeCalls == 0,
                "factor adjusted branch must draw once");

            Console.WriteLine("WavesArenaPresetWeight: PASS");
        }
    }
}
