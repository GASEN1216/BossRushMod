using System;

namespace BossRush
{
    /// <summary>Wiki 原版战斗基准。独立于随机底模、装备与掉落；不存入玩家存档。</summary>
    internal sealed class SkyIslandCombatBaseline
    {
        internal string PresetId;
        internal float Health, Damage, MoveSpeed, BulletSpeed, Range, Scatter, Crit;
        internal float NightVision, AiCombat, Sight, Hearing, Reaction, ShootDelay, NightReaction;
    }

    /// <summary>
    /// COMPAT：原版属性倍率。来源：
    /// https://escapefromduckov.net/zh/wiki/creatures （按精确 preset ID 区分本体、风暴区与强化版）。
    /// 小兵与精英用实际抽到的那位官方 Boss 底模（SkyIslandEnemySources），剧情对手 / 头目 / 岛主 / 噬风用下面的固定基准。
    /// 此表只定义基础战斗能力；经济、护甲、元素克制与自定义招式仍由各自系统负责。
    ///
    /// 2026-10-01 owner 先定「属性翻 3 倍、整体难度比原版地图高 3 倍，反应和开火都要更快」，同日实测后改为「整体 Boss 数值改为现在的一半」：
    /// <see cref="Multiplier"/> 从 3 降到 1.5，管生命、伤害、反应、开火前摇、散布与暴击（仍比原版强，只是不再是 3 倍）；
    /// 移速、弹速、射程、视野、听觉与夜视另走 <see cref="PerceptionMultiplier"/>（仍是 2026-09-26 的 1.5 倍）——
    /// 这几项是「看得见、追得上」的口径，减半会低于原版（敌人比官方地图还瞎），不是 owner 说的「数值减半」，所以不动。
    /// </summary>
    internal static class SkyIslandCombatBalance
    {
        internal const float Multiplier = 1.5f;
        internal const float PerceptionMultiplier = 1.5f;

        /// <summary>
        /// 序章守卫（零号区「断风游猎·守」）在统一倍率之上再乘的生命系数。它倒下必掉品质 5–6 的枪，
        /// owner 2026-10-01 要求别让刚开档的玩家轻易打下来：K3 参照 250 × 1.5 = 375，先抬到 <see cref="BossHealthFloor"/> 1000，再 × 2 = 2000（再受游戏难度影响）。
        /// 只用于零号区序章，岛上 K3 中继平台那一位不受影响。
        /// </summary>
        internal const float PreludeWardenHealthFactor = 2f;

        /// <summary>
        /// 本 Mod 专属 Boss（头目、岛主、折翎与守钟装置、噬风、序章守卫）的生命下限（owner 2026-10-02「我们自己的专属 Boss 血量都要 1000 以上」）。
        /// 统一倍率之后取大：参照 Boss 原版血少的（机械雪人 320、BA 头目 250、三枪 400……）抬到这里，Alex 这类本来更高的不动。
        /// 序章守卫在这之后再乘 <see cref="PreludeWardenHealthFactor"/>，所以零号区那一位是 2000（再受游戏难度影响）。
        /// 小兵、精英与巡守不是专属 Boss，不受影响。
        /// </summary>
        internal const float BossHealthFloor = 1000f;
        private static readonly SkyIslandCombatBaseline[] baselines =
        {
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Scav_Snow_Elete",
                Health = 180f, Damage = 1.35f, MoveSpeed = 1f, BulletSpeed = 0.8f, Range = 1f, Scatter = 0.6f, Crit = 0f, NightVision = 0.5f, AiCombat = 1.5f, Sight = 15f, Hearing = 1f, Reaction = 0.45f, ShootDelay = 0.35f, NightReaction = 1.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Alex",
                Health = 800f, Damage = 1f, MoveSpeed = 1.5f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_3Shot",
                Health = 400f, Damage = 1f, MoveSpeed = 1.2f, BulletSpeed = 0.8f, Range = 1.4f, Scatter = 0.05f, Crit = 0.15f, NightVision = 1f, AiCombat = 2f, Sight = 40f, Hearing = 2f, Reaction = 0.25f, ShootDelay = 0.2f, NightReaction = 1f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Hunter",
                Health = 450f, Damage = 1.15f, MoveSpeed = 1.5f, BulletSpeed = 1.15f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 4f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Speedy",
                Health = 170f, Damage = 0.8f, MoveSpeed = 1.2f, BulletSpeed = 0.9f, Range = 1.25f, Scatter = 0.35f, Crit = 0f, NightVision = 0.5f, AiCombat = 2f, Sight = 18f, Hearing = 1f, Reaction = 0.4f, ShootDelay = 0.15f, NightReaction = 1.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Tagilla",
                Health = 610f, Damage = 1.35f, MoveSpeed = 1.35f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 3f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Grenade",
                Health = 300f, Damage = 1f, MoveSpeed = 1.5f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Fly",
                Health = 160f, Damage = 0.9f, MoveSpeed = 1.2f, BulletSpeed = 1f, Range = 1.4f, Scatter = 0.35f, Crit = 0f, NightVision = 0.5f, AiCombat = 2f, Sight = 18f, Hearing = 1f, Reaction = 0.4f, ShootDelay = 0.15f, NightReaction = 1.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Vida",
                Health = 220f, Damage = 1f, MoveSpeed = 1.5f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Speedy_Ice",
                Health = 226f, Damage = 0.8f, MoveSpeed = 1.2f, BulletSpeed = 0.9f, Range = 1.25f, Scatter = 0.35f, Crit = 0f, NightVision = 0.5f, AiCombat = 2f, Sight = 18f, Hearing = 1f, Reaction = 0.4f, ShootDelay = 0.15f, NightReaction = 1.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Deng",
                Health = 190f, Damage = 0.85f, MoveSpeed = 1.2f, BulletSpeed = 0.8f, Range = 1.4f, Scatter = 0.05f, Crit = 100f, NightVision = 1f, AiCombat = 2f, Sight = 35f, Hearing = 2f, Reaction = 0.8f, ShootDelay = 0.3f, NightReaction = 1f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_BALeader",
                Health = 250f, Damage = 1f, MoveSpeed = 1.15f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.2f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Killa",
                Health = 415f, Damage = 1.15f, MoveSpeed = 1.5f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.25f, Crit = 0.25f, NightVision = 0.75f, AiCombat = 2f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_SnowMan",
                Health = 320f, Damage = 1f, MoveSpeed = 1.5f, BulletSpeed = 1f, Range = 1.35f, Scatter = 0.3f, Crit = 0f, NightVision = 0.75f, AiCombat = 5f, Sight = 17f, Hearing = 1f, Reaction = 0.3f, ShootDelay = 0.15f, NightReaction = 0.5f },
            new SkyIslandCombatBaseline { PresetId = "EnemyPreset_Boss_Island_Koukou",
                Health = 2000f, Damage = 1f, MoveSpeed = 1f, BulletSpeed = 0.4f, Range = 1.2f, Scatter = 0.35f, Crit = 0f, NightVision = 1f, AiCombat = 2f, Sight = 21f, Hearing = 0.75f, Reaction = 0.5f, ShootDelay = 0.5f, NightReaction = 1.5f },
        };

        internal static SkyIslandCombatBaseline Find(string presetId)
        {
            for (int i = 0; i < baselines.Length; i++)
                if (baselines[i].PresetId == presetId) return baselines[i];
            throw new ArgumentException("天空岛战斗基准缺失：" + presetId);
        }

        internal static SkyIslandCombatBaseline For(string encounterId, int index, SkyIslandEnemyTier tier)
        {
            SkyIslandBossProfile profile = SkyIslandBossRules.Find(encounterId, index);
            if (profile != null) return Find(profile.VanillaPresetId);
            if (tier == SkyIslandEnemyTier.Storm) return Find("EnemyPreset_Boss_Island_Koukou");
            if (tier == SkyIslandEnemyTier.Champion)
                return Find(encounterId == "BellKeeper" ? "EnemyPreset_Boss_SnowMan" : "EnemyPreset_Boss_Killa");
            // 小兵与精英（断风游猎）的底模是从官方 Boss 池抽的那一位，数值就按它自己的原版（owner 2026-10-01「全按各自 Boss 原版 ×3」）；
            // 雪地精英拾荒者那一行留在表里只作 Wiki 快照对照，不再有人引用。
            if (tier == SkyIslandEnemyTier.Elite || tier == SkyIslandEnemyTier.Scav) return null;
            throw new ArgumentException("天空岛头目未绑定战斗基准：" + encounterId);
        }

        /// <summary>负暴击修正是惩罚，向零缩小；正修正乘 <see cref="Multiplier"/>，上限为 100%。</summary>
        internal static float BoostCrit(float value)
        {
            return Math.Min(1f, value < 0f ? value / Multiplier : value * Multiplier);
        }
    }
}
