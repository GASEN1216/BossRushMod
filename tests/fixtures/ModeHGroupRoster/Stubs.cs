// 宿主替身：只替 Boss 预设表、官方预设目录与本地化；抽取 / 配平 / 排名全部是生产代码。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static int RoundToInt(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }
    }
}

namespace BossRush
{
    public class EnemyPresetInfo
    {
        public string name;
        public string displayName;
        public float baseHealth;
    }

    public class CharacterRandomPreset
    {
        public float health = 1000f;
        public float damageMultiplier = 1f;
        public float meleeDamageMultiplier = 1f;
        public float moveSpeedFactor = 1f;
    }

    public class ModBehaviour
    {
        internal List<EnemyPresetInfo> BossFilterEnemyPresets = new List<EnemyPresetInfo>();
        internal int InitializeCalls;
        internal void InitializeBossFilterEnemyPresets() { InitializeCalls++; }
    }

    internal static class ModeHProductionCertification
    {
        internal static readonly Dictionary<string, CharacterRandomPreset> Catalog =
            new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);

        internal static CharacterRandomPreset ResolveAuditedPreset(string key)
        {
            CharacterRandomPreset preset;
            return key != null && Catalog.TryGetValue(key, out preset) ? preset : null;
        }
    }

    internal static class L10n
    {
        public static string T(string cn, string en) { return cn; }
        public static string T(string key) { return "*" + key + "*"; }
    }

    internal static class DragonDescendantConfig
    {
        public const string BOSS_NAME_KEY = "DragonDescendant";
        public const string BOSS_NAME_CN = "龙裔遗族";
        public const string BOSS_NAME_EN = "Dragon Descendant";
    }

    internal static class DragonKingConfig
    {
        public const string BossNameKey = "boss_dragonking";
        public const string BossNameCN = "焚天龙皇";
        public const string BossNameEN = "Dragon King";
    }

    internal static class PhantomWitchConfig
    {
        public const string BossNameKey = "PhantomWitch";
        public const string BossNameCN = "幽灵女巫";
        public const string BossNameEN = "Phantom Witch";
    }
}
